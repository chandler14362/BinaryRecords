using System.Collections.Generic;
using BinaryRecords.Enums;
using Krypton.Buffers;

namespace BinaryRecords.Records
{
    public record ConstructableTypeRecord(IReadOnlyList<(uint Key, TypeRecord MemberType)> Members) 
        : TypeRecord(SerializableDataTypes.Constructable)
    {
        protected override void DoHash(ref SpanBufferWriter bufferWriter)
        {
            foreach (var (key, memberType) in Members)
            {
                bufferWriter.WriteUInt32(key);
                memberType.Hash(ref bufferWriter);
            }
        }
    }
}