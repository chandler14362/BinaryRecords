using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Krypton.Buffers;

namespace BinaryRecords.Providers
{
    public static class BufferExpressionGeneratorProviders
    {
        public static IReadOnlyList<ExpressionGeneratorProvider> Builtin = CreateBuiltinProviders().ToList();

        private static ExpressionGeneratorProvider CreateBufferProvider<T>(
            MethodInfo serialize, 
            MethodInfo deserialize)
        {
            return new(
                Name: $"{typeof(T)}BufferProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, _) => type == typeof(T),
                GenerateSerializeExpression: (serializer, type, dataAccess, bufferAccess) => 
                    Expression.Call(bufferAccess, serialize, dataAccess),
                GenerateDeserializeExpression: (serializer, type, bufferAccess) => 
                    Expression.Call(bufferAccess, deserialize)
            );
        }
        
        private static BlittableExpressionGeneratorProvider CreateBlittableBufferProvider<T>(
            MethodInfo serialize, 
            MethodInfo deserialize)
        {
            return new(
                Name: $"{typeof(T)}BlittableBufferProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, _) => type == typeof(T),
                GenerateSerializeExpression: (serializer, type, dataAccess, bufferAccess) => 
                    Expression.Call(bufferAccess, serialize, dataAccess),
                GenerateDeserializeExpression: (serializer, type, bufferAccess) => 
                    Expression.Call(bufferAccess, deserialize)
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
                bufferReaderType.GetMethod("ReadBool")!
                );
            
            // byte types
            yield return CreateBlittableBufferProvider<byte>(
                bufferWriterType.GetMethod("WriteUInt8")!,
                bufferReaderType.GetMethod("ReadUInt8")!
                );
            yield return CreateBlittableBufferProvider<sbyte>(
                bufferWriterType.GetMethod("WriteInt8")!,
                bufferReaderType.GetMethod("ReadInt8")!
                );   
            
            // short types
            yield return CreateBlittableBufferProvider<ushort>(
                bufferWriterType.GetMethod("WriteUInt16")!, 
                bufferReaderType.GetMethod("ReadUInt16")!
                );
            yield return CreateBlittableBufferProvider<short>(
                bufferWriterType.GetMethod("WriteInt16")!,
                bufferReaderType.GetMethod("ReadInt16")!
                );

            // int types
            yield return CreateBlittableBufferProvider<uint>(
                bufferWriterType.GetMethod("WriteUInt32")!,
                bufferReaderType.GetMethod("ReadUInt32")!
                );
            yield return CreateBlittableBufferProvider<int>(
                bufferWriterType.GetMethod("WriteInt32")!, 
                bufferReaderType.GetMethod("ReadInt32")!
                );

            // long types
            yield return CreateBlittableBufferProvider<ulong>(
                bufferWriterType.GetMethod("WriteUInt64")!,
                bufferReaderType.GetMethod("ReadUInt64")!
                );
            yield return CreateBlittableBufferProvider<long>(
                bufferWriterType.GetMethod("WriteInt64")!,
                bufferReaderType.GetMethod("ReadInt64")!
                );

            // float types
            yield return CreateBlittableBufferProvider<float>(
                bufferWriterType.GetMethod("WriteFloat32")!,
                bufferReaderType.GetMethod("ReadFloat32")!
                );
            yield return CreateBlittableBufferProvider<double>(
                bufferWriterType.GetMethod("WriteFloat64")!,
                bufferReaderType.GetMethod("ReadFloat64")!
                );

            // string type
            yield return CreateBufferProvider<string>(
                bufferWriterType.GetMethod("WriteUTF8String")!,
                bufferReaderType.GetMethod("ReadUTF8String")!
                );
        }
    }
}