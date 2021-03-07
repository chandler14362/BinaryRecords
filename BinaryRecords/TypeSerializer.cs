using System;
using System.Buffers;
using BinaryRecords.Abstractions;
using BinaryRecords.Delegates;
using Krypton.Buffers;

namespace BinaryRecords
{
    // TODO: Need to revaluate the non-generic apis, probably going to wrap the generic ones using
    //       function pointers
    public class TypeSerializer
    {
        private readonly ITypingLibrary _typingLibrary;
        
        internal TypeSerializer(ITypingLibrary typingLibrary)
        {
            _typingLibrary = typingLibrary;
        }
        
        public void Serialize<T>(T obj, ref SpanBufferWriter buffer)
        {
            var serializeDelegate = _typingLibrary.GetSerializeDelegate(typeof(T));
            ((GenericSerializeDelegate<T>) serializeDelegate)(obj, ref buffer);
        }

        // TODO: Non generic API
        public void Serialize(Type type, object obj, ref SpanBufferWriter bufferWriter) =>
            throw new NotImplementedException();

        public void Serialize<T, TState>(T obj, TState state, ReadOnlySpanAction<byte, TState> callback, int stackSize = 512)
        {
            var serializeDelegate = _typingLibrary.GetSerializeDelegate(typeof(T));
            var buffer = new SpanBufferWriter(stackalloc byte[stackSize]);
            ((GenericSerializeDelegate<T>) serializeDelegate)(obj, ref buffer);
            callback(buffer.Data, state);
        }
        
        // TODO: Non generic API
        public void Serialize<TState>(Type type, object obj, TState state, ReadOnlySpanAction<byte, TState> callback, int stackSize = 512) =>
            throw new NotImplementedException();

        public void Serialize<T>(T obj, StatelessSerializationCallback callback, int stackSize = 512)
        {
            var serializeDelegate = _typingLibrary.GetSerializeDelegate(typeof(T));
            var buffer = new SpanBufferWriter(stackalloc byte[stackSize]);
            ((GenericSerializeDelegate<T>) serializeDelegate)(obj, ref buffer);
            callback(buffer.Data);
        }

        // TODO: Non generic API
        public void Serialize(Type type, object obj, StatelessSerializationCallback callback, int stackSize = 512) =>
            throw new NotImplementedException();

        public int Serialize<T>(T obj, Memory<byte> memory)
        {
            var serializeDelegate = _typingLibrary.GetSerializeDelegate(typeof(T));
            var buffer = new SpanBufferWriter(memory.Span, resize: false);
            try
            {
                ((GenericSerializeDelegate<T>) serializeDelegate)(obj, ref buffer);
            }
            catch(OutOfSpaceException)
            {
                return -1;
            }
            return buffer.Size;
        }

        // TODO: Non generic API
        public int Serialize(Type objType, object obj, Memory<byte> memory) =>
            throw new NotImplementedException();

        public byte[] Serialize<T>(T obj)
        {
            var serializeDelegate = _typingLibrary.GetSerializeDelegate(typeof(T));
            var buffer = new SpanBufferWriter(stackalloc byte[512]);
            ((GenericSerializeDelegate<T>) serializeDelegate)(obj, ref buffer);
            return buffer.Data.ToArray();
        }

        // TODO: Non generic API
        public byte[] Serialize(Type objType, object obj) =>
            throw new NotImplementedException();
        
        public T Deserialize<T>(ReadOnlySpan<byte> buffer)
        {
            var bufferReader = new SpanBufferReader(buffer);
            return Deserialize<T>(ref bufferReader);
        }

        // TODO: Non generic API
        public object Deserialize(Type type, ReadOnlySpan<byte> buffer) =>
            throw new NotImplementedException();
        
        public T Deserialize<T>(ref SpanBufferReader bufferReader)
        {
            var deserializeDelegate = _typingLibrary.GetDeserializeDelegate(typeof(T));
            return ((GenericDeserializeDelegate<T>) deserializeDelegate)(ref bufferReader);
        }

        // TODO: Non generic API
        public object Deserialize(Type type, ref SpanBufferReader bufferReader) =>
            throw new NotImplementedException();
    }
}
