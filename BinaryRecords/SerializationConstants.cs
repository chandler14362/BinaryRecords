using System;
using System.Collections.Generic;
using System.Reflection;
using Krypton.Buffers;

namespace BinaryRecords
{
    public static class SerializationConstants
    {
        public record SerializationPair(MethodInfo Serialize, MethodInfo Deserialize);
        
        public delegate void SerializeDelegate<T>(ref SpanBufferWriter buffer, T obj);

        public delegate T DeserializeDelegate<T>(ref SpanBufferReader bufferReader);

        public static readonly Type BufferWriterType =
            Type.GetType($"{typeof(SpanBufferWriter).FullName}&,Krypton.Buffers");

        public static readonly Type BufferReaderType =
            Type.GetType($"{typeof(SpanBufferReader).FullName}&,Krypton.Buffers");
        
        private static Dictionary<Type, SerializationPair> _primitiveTypes = new();

        public static bool TryGetPrimitiveSerializationPair(Type type, out SerializationPair serializationPair)
            => _primitiveTypes.TryGetValue(type, out serializationPair);

        static SerializationConstants()
        {
            var bufferType = typeof(SpanBufferWriter);
            var bufferReaderType = typeof(SpanBufferReader);

            // byte types
            var serialize = bufferType.GetMethod("WriteUInt8");
            var deserialize = bufferReaderType.GetMethod("ReadUInt8");
            _primitiveTypes.Add(typeof(byte), new(serialize, deserialize));
            
            serialize = bufferType.GetMethod("WriteInt8");
            deserialize = bufferReaderType.GetMethod("ReadInt8");
            _primitiveTypes.Add(typeof(sbyte), new(serialize, deserialize));
            
            // short types
            serialize = bufferType.GetMethod("WriteUInt16");
            deserialize = bufferReaderType.GetMethod("ReadUInt16");
            _primitiveTypes.Add(typeof(ushort), new(serialize, deserialize));
            
            serialize = bufferType.GetMethod("WriteInt16");
            deserialize = bufferReaderType.GetMethod("ReadInt16");
            _primitiveTypes.Add(typeof(short), new(serialize, deserialize));
            
            // int types
            serialize = bufferType.GetMethod("WriteUInt32");
            deserialize = bufferReaderType.GetMethod("ReadUInt32");
            _primitiveTypes.Add(typeof(uint), new(serialize, deserialize));
            
            serialize = bufferType.GetMethod("WriteInt32");
            deserialize = bufferReaderType.GetMethod("ReadInt32");
            _primitiveTypes.Add(typeof(int), new(serialize, deserialize));
            
            // long types
            serialize = bufferType.GetMethod("WriteUInt64");
            deserialize = bufferReaderType.GetMethod("ReadUInt64");
            _primitiveTypes.Add(typeof(ulong), new(serialize, deserialize));
            
            serialize = bufferType.GetMethod("WriteInt64");
            deserialize = bufferReaderType.GetMethod("ReadInt64");
            _primitiveTypes.Add(typeof(long), new(serialize, deserialize));
            
            // float types
            serialize = bufferType.GetMethod("WriteFloat32");
            deserialize = bufferReaderType.GetMethod("ReadFloat32");
            _primitiveTypes.Add(typeof(float), new(serialize, deserialize));
            
            serialize = bufferType.GetMethod("WriteFloat64");
            deserialize = bufferReaderType.GetMethod("ReadFloat64");
            _primitiveTypes.Add(typeof(double), new(serialize, deserialize));
            
            // string type
            serialize = bufferType.GetMethod("WriteUTF8String");
            deserialize = bufferReaderType.GetMethod("ReadUTF8String");
            _primitiveTypes.Add(typeof(string), new(serialize, deserialize));
        }
    }
}
