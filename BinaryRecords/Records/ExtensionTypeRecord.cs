using BinaryRecords.Enums;
using BinaryRecords.Util;
using Krypton.Buffers;

namespace BinaryRecords.Records
{
    public record ExtensionTypeRecord() 
        : TypeRecord(SerializableDataTypes.Extension)
    {
        protected override void DoHash(
            ref SpanBufferWriter bufferWriter, 
            ConstructableHashTracker constructableHashTracker)
        {
        }
    }
}