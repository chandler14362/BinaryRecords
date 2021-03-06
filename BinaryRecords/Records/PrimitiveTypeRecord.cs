using BinaryRecords.Enums;
using Krypton.Buffers;

namespace BinaryRecords.Records
{
    public record PrimitiveTypeRecord(SerializableDataTypes SerializableType) : TypeRecord(SerializableType)
    {
        protected override void DoHash(ref SpanBufferWriter bufferWriter)
        {
        }
    }
}