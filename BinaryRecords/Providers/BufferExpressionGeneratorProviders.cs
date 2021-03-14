using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using BinaryRecords.Delegates;
using BinaryRecords.Enums;
using BinaryRecords.Expressions;
using BinaryRecords.Records;
using BinaryRecords.Util;

namespace BinaryRecords.Providers
{
    public static class BufferExpressionGeneratorProviders
    {
        public static readonly IReadOnlyList<ExpressionGeneratorProvider> Builtin = CreateBuiltinProviders().ToList();

        private delegate Expression SerializeBlittable(Expression buffer, Expression data);
        private delegate Expression DeserializeBlittable(Expression buffer);
        
        private static BlittableExpressionGeneratorProvider CreateBlittableBufferProvider<T>(
            SerializeBlittable serialize, 
            DeserializeBlittable deserialize,
            SerializableDataTypes serializableDataType)
        {
            return new(
                Name: $"{typeof(T)}BlittableBufferProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, _) => type == typeof(T),
                GenerateSerializeExpression: (typingLibrary, type, buffer, data, versioning) =>
                {
                    var blockBuilder = new ExpressionBlockBuilder();
                    versioning?.Start(blockBuilder, buffer, typingLibrary.BitSize);
                    blockBuilder += serialize(buffer, data);
                    versioning?.Stop(blockBuilder, buffer, typingLibrary.BitSize);
                    return blockBuilder;
                },
                GenerateDeserializeExpression: (typingLibrary, type, buffer) => deserialize(buffer),
                GenerateTypeRecord: (_, _) => new PrimitiveTypeRecord(serializableDataType)
            );
        }
        
        private static IEnumerable<ExpressionGeneratorProvider> CreateBuiltinProviders()
        {
            // Create the providers for each primitive type
            var bufferWriterType = typeof(BinaryBufferWriter);
            var bufferReaderType = typeof(BinaryBufferReader);

            // bool type
            yield return CreateBlittableBufferProvider<bool>(
                BufferWriterExpressions.WriteBool,
                BufferReaderExpressions.ReadBool,
                SerializableDataTypes.Bool
                );
            
            // byte types
            yield return CreateBlittableBufferProvider<byte>(
                BufferWriterExpressions.WriteUInt8,
                BufferReaderExpressions.ReadUInt8,
                SerializableDataTypes.Byte
            );
            yield return CreateBlittableBufferProvider<sbyte>(
                BufferWriterExpressions.WriteInt8,
                BufferReaderExpressions.ReadUInt8,
                SerializableDataTypes.SByte
                );   
            
            // short types
            yield return CreateBlittableBufferProvider<ushort>(
                BufferWriterExpressions.WriteUInt16, 
                BufferReaderExpressions.ReadUInt16,
                SerializableDataTypes.UShort
                );
            yield return CreateBlittableBufferProvider<short>(
                BufferWriterExpressions.WriteInt16,
                BufferReaderExpressions.ReadUInt16,
                SerializableDataTypes.Short
                );

            // int types
            yield return CreateBlittableBufferProvider<uint>(
                BufferWriterExpressions.WriteUInt32,
                BufferReaderExpressions.ReadUInt32,
                SerializableDataTypes.UInt
                );
            yield return CreateBlittableBufferProvider<int>(
                BufferWriterExpressions.WriteInt32, 
                BufferReaderExpressions.ReadInt32,
                SerializableDataTypes.Int
                );

            // long types
            yield return CreateBlittableBufferProvider<ulong>(
                BufferWriterExpressions.WriteUInt64,
                BufferReaderExpressions.ReadUInt64,
                SerializableDataTypes.ULong
                );
            yield return CreateBlittableBufferProvider<long>(
                BufferWriterExpressions.WriteInt64,
                BufferReaderExpressions.ReadInt64,
                SerializableDataTypes.Long
                );

            // float types
            yield return CreateBlittableBufferProvider<float>(
                BufferWriterExpressions.WriteSingle,
                BufferReaderExpressions.ReadSingle,
                SerializableDataTypes.Float
                );
            yield return CreateBlittableBufferProvider<double>(
                BufferWriterExpressions.WriteDouble,
                BufferReaderExpressions.ReadDouble,
                SerializableDataTypes.Double
                );

            // string type
            yield return new ExpressionGeneratorProvider(
                Name: "StringProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, _) => type == typeof(string),
                GenerateSerializeExpression: (typingLibrary, type, buffer, data, versioning) =>
                {
                    var blockBuilder = new ExpressionBlockBuilder();
                    versioning?.Start(blockBuilder, buffer, typingLibrary.BitSize);
                    blockBuilder += BufferWriterExpressions.WriteUTF8String(buffer, data);
                    versioning?.Stop(blockBuilder, buffer, typingLibrary.BitSize);
                    return blockBuilder;
                },
                GenerateDeserializeExpression: (typingLibrary, type, buffer) => 
                    BufferReaderExpressions.ReadUTF8String(buffer),
                GenerateTypeRecord: (typingLibrary, type) => new ListTypeRecord(typingLibrary.GetTypeRecord(typeof(byte)))
            );
        }
    }
}