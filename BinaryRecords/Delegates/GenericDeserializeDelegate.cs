using Krypton.Buffers;

namespace BinaryRecords.Delegates
{
    public delegate T GenericDeserializeDelegate<T>(ref SpanBufferReader bufferReader);
}
