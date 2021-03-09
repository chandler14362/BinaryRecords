using BinaryRecords.Enums;
using BinaryRecords.Util;
using Krypton.Buffers;

namespace BinaryRecords.Records
{
    public record PrimitiveTypeRecord(SerializableDataTypes SerializableType) 
        : TypeRecord(SerializableType)
    {
        protected override void DoHash(
            ref SpanBufferWriter bufferWriter, 
            ConstructableHashTracker constructableHashTracker)
        {
        }
    }
}