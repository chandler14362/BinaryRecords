using Krypton.Buffers;

namespace BinaryRecords.Delegates
{
    public delegate T DeserializeGenericDelegate<T>(ref SpanBufferReader bufferReader);
}
