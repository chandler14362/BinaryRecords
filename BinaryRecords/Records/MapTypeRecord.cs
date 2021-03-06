using BinaryRecords.Enums;
using Krypton.Buffers;

namespace BinaryRecords.Records
{
    public record MapDataRecord(TypeRecord KeyType, TypeRecord ValueType) : TypeRecord(SerializableDataTypes.Map)
    {
        protected override void DoHash(ref SpanBufferWriter bufferWriter)
        {
            KeyType.Hash(ref bufferWriter);
            ValueType.Hash(ref bufferWriter);
        }
    }
}