using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BinaryRecords.Enums;
using BinaryRecords.Records;
using Krypton.Buffers;

namespace BinaryRecords.Providers
{
    public static class BufferExpressionGeneratorProviders
    {
        public static IReadOnlyList<ExpressionGeneratorProvider> Builtin = CreateBuiltinProviders().ToList();

        private static BlittableExpressionGeneratorProvider CreateBlittableBufferProvider<T>(
            MethodInfo serialize, 
            MethodInfo deserialize,
            SerializableDataTypes serializableDataType)
        {
            return new(
                Name: $"{typeof(T)}BlittableBufferProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, _) => type == typeof(T),
                GenerateSerializeExpression: (serializer, type, dataAccess, bufferAccess) => 
                    Expression.Call(bufferAccess, serialize, dataAccess),
                GenerateDeserializeExpression: (serializer, type, bufferAccess) => 
                    Expression.Call(bufferAccess, deserialize),
                GenerateTypeRecord: (_, _) => new PrimitiveTypeRecord(serializableDataType)
            );
        }
        
        private static IEnumerable<ExpressionGeneratorProvider> CreateBuiltinProviders()
        {
            // Create the providers for each primitive type
            var bufferWriterType = typeof(SpanBufferWriter);
            var bufferReaderType = typeof(SpanBufferReader);

            // bool type
            yield return CreateBlittableBufferProvider<bool>(
                bufferWriterType.GetMethod("WriteBool")!,
                bufferReaderType.GetMethod("ReadBool")!,
                SerializableDataTypes.Bool
                );
            
            // byte types
            yield return CreateBlittableBufferProvider<byte>(
                bufferWriterType.GetMethod("WriteUInt8")!,
                bufferReaderType.GetMethod("ReadUInt8")!,
                SerializableDataTypes.Byte
            );
            yield return CreateBlittableBufferProvider<sbyte>(
                bufferWriterType.GetMethod("WriteInt8")!,
                bufferReaderType.GetMethod("ReadInt8")!,
                SerializableDataTypes.SByte
                );   
            
            // short types
            yield return CreateBlittableBufferProvider<ushort>(
                bufferWriterType.GetMethod("WriteUInt16")!, 
                bufferReaderType.GetMethod("ReadUInt16")!,
                SerializableDataTypes.UShort
                );
            yield return CreateBlittableBufferProvider<short>(
                bufferWriterType.GetMethod("WriteInt16")!,
                bufferReaderType.GetMethod("ReadInt16")!,
                SerializableDataTypes.Short
                );

            // int types
            yield return CreateBlittableBufferProvider<uint>(
                bufferWriterType.GetMethod("WriteUInt32")!,
                bufferReaderType.GetMethod("ReadUInt32")!,
                SerializableDataTypes.UInt
                );
            yield return CreateBlittableBufferProvider<int>(
                bufferWriterType.GetMethod("WriteInt32")!, 
                bufferReaderType.GetMethod("ReadInt32")!,
                SerializableDataTypes.Int
                );

            // long types
            yield return CreateBlittableBufferProvider<ulong>(
                bufferWriterType.GetMethod("WriteUInt64")!,
                bufferReaderType.GetMethod("ReadUInt64")!,
                SerializableDataTypes.ULong
                );
            yield return CreateBlittableBufferProvider<long>(
                bufferWriterType.GetMethod("WriteInt64")!,
                bufferReaderType.GetMethod("ReadInt64")!,
                SerializableDataTypes.Long
                );

            // float types
            yield return CreateBlittableBufferProvider<float>(
                bufferWriterType.GetMethod("WriteFloat32")!,
                bufferReaderType.GetMethod("ReadFloat32")!,
                SerializableDataTypes.Float
                );
            yield return CreateBlittableBufferProvider<double>(
                bufferWriterType.GetMethod("WriteFloat64")!,
                bufferReaderType.GetMethod("ReadFloat64")!,
                SerializableDataTypes.Double
                );

            // string type
            yield return new ExpressionGeneratorProvider(
                Name: "StringProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, _) => type == typeof(string),
                GenerateSerializeExpression: (serializer, type, dataAccess, bufferAccess) => 
                    Expression.Call(bufferAccess, bufferWriterType.GetMethod("WriteUTF8String")!, dataAccess),
                GenerateDeserializeExpression: (serializer, type, bufferAccess) => 
                    Expression.Call(bufferAccess, bufferReaderType.GetMethod("ReadUTF8String")!),
                GenerateTypeRecord: (_, typingLibrary) => new ListTypeRecord(typingLibrary.GetTypeRecord(typeof(byte)))
            );
        }
    }
}