using Krypton.Buffers;

namespace BinaryRecords.Delegates
{
    public delegate void GenericSerializeDelegate<T>(T obj, ref SpanBufferWriter buffer);
}
