using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Krypton.Buffers;

namespace BinaryRecords.Providers
{
    public static class PrimitiveExpressionGeneratorProviders
    {
        private static List<ExpressionGeneratorProvider> _blittableProviders = new();
        
        public static IReadOnlyList<ExpressionGeneratorProvider> Builtin = CreateBuiltinProviders().ToArray();
        
        public static bool IsBlittable(ExpressionGeneratorProvider provider)
        {
            // In order for a type to be blittable, it needs to be registered and the machine needs to be 
            // little endian
            return _blittableProviders.Contains(provider) && BitConverter.IsLittleEndian;
        }

        private static ExpressionGeneratorProvider CreatePrimitiveProvider<T>(MethodInfo serialize, MethodInfo deserialize)
        {
            return new(
                Name: $"{typeof(T)}PrimitiveProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, _) => type == typeof(T),
                GenerateSerializeExpression: (serializer, type, dataAccess, bufferAccess) => 
                    Expression.Call(bufferAccess, serialize, dataAccess),
                GenerateDeserializeExpression: (serializer, type, bufferAccess) => 
                    Expression.Call(bufferAccess, deserialize)
            );
        }

        private static ExpressionGeneratorProvider RegisterBlittable(ExpressionGeneratorProvider generatorProvider)
        {
            if (_blittableProviders.Contains(generatorProvider)) throw new Exception();
            _blittableProviders.Add(generatorProvider);
            return generatorProvider;
        }
        
        private static IEnumerable<ExpressionGeneratorProvider> CreateBuiltinProviders()
        {
            // Create the providers for each primitive type
            var bufferType = typeof(SpanBufferWriter);
            var bufferReaderType = typeof(SpanBufferReader);

            // bool type
            yield return CreatePrimitiveProvider<bool>(bufferType.GetMethod("WriteBool"),
                bufferReaderType.GetMethod("ReadBool"));
            
            // byte types
            yield return RegisterBlittable(CreatePrimitiveProvider<byte>(bufferType.GetMethod("WriteUInt8"),
                bufferReaderType.GetMethod("ReadUInt8")));
            yield return RegisterBlittable(CreatePrimitiveProvider<sbyte>(bufferType.GetMethod("WriteInt8"),
                bufferReaderType.GetMethod("ReadInt8")));   
            
            // short types
            yield return RegisterBlittable(CreatePrimitiveProvider<ushort>(bufferType.GetMethod("WriteUInt16"),
                bufferReaderType.GetMethod("ReadUInt16")));
            yield return RegisterBlittable(CreatePrimitiveProvider<short>(bufferType.GetMethod("WriteInt16"),
                bufferReaderType.GetMethod("ReadInt16")));

            // int types
            yield return RegisterBlittable(CreatePrimitiveProvider<uint>(bufferType.GetMethod("WriteUInt32"),
                bufferReaderType.GetMethod("ReadUInt32")));
            yield return RegisterBlittable(CreatePrimitiveProvider<int>(bufferType.GetMethod("WriteInt32"),
                bufferReaderType.GetMethod("ReadInt32")));

            // long types
            yield return RegisterBlittable(CreatePrimitiveProvider<ulong>(bufferType.GetMethod("WriteUInt64"),
                bufferReaderType.GetMethod("ReadUInt64")));
            yield return RegisterBlittable(CreatePrimitiveProvider<long>(bufferType.GetMethod("WriteInt64"),
                bufferReaderType.GetMethod("ReadInt64")));

            // float types
            yield return RegisterBlittable(CreatePrimitiveProvider<float>(bufferType.GetMethod("WriteFloat32"),
                bufferReaderType.GetMethod("ReadFloat32")));
            yield return RegisterBlittable(CreatePrimitiveProvider<double>(bufferType.GetMethod("WriteFloat64"),
                bufferReaderType.GetMethod("ReadFloat64")));

            // string type
            yield return CreatePrimitiveProvider<string>(bufferType.GetMethod("WriteUTF8String"),
                bufferReaderType.GetMethod("ReadUTF8String"));
        }
    }
}