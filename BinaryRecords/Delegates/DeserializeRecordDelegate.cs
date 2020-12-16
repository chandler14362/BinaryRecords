using Krypton.Buffers;

namespace BinaryRecords.Delegates
{
    internal delegate object DeserializeRecordDelegate(ref SpanBufferReader bufferReader);
}
