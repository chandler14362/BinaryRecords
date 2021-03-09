using BinaryRecords.Enums;
using BinaryRecords.Util;
using Krypton.Buffers;

namespace BinaryRecords.Records
{
    public record ListTypeRecord(TypeRecord ElementType) 
        : TypeRecord(SerializableDataTypes.List)
    {
        protected override void DoHash(
            ref SpanBufferWriter bufferWriter, 
            ConstructableHashTracker constructableHashTracker)
        {
            ElementType.Hash(ref bufferWriter, constructableHashTracker);
        }
    }
}