using Krypton.Buffers;

namespace BinaryRecords.Delegates
{
    public delegate void SerializeGenericDelegate<T>(ref SpanBufferWriter buffer, T obj);
}
