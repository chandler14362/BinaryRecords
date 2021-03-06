using BinaryRecords.Enums;
using Krypton.Buffers;

namespace BinaryRecords.Records
{
    public record ListTypeRecord(TypeRecord ElementType) : TypeRecord(SerializableDataTypes.List)
    {
        protected override void DoHash(ref SpanBufferWriter bufferWriter)
        {
            ElementType.Hash(ref bufferWriter);
        }
    }
}