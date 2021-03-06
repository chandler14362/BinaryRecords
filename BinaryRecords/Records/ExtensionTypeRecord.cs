using BinaryRecords.Enums;
using Krypton.Buffers;

namespace BinaryRecords.Records
{
    public record ExtensionTypeRecord(uint ExtensionId) : TypeRecord(SerializableDataTypes.Extension)
    {
        protected override void DoHash(ref SpanBufferWriter bufferWriter)
        {
            bufferWriter.WriteUInt32(ExtensionId);
        }
    }
}