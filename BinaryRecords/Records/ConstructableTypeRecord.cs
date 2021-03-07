using System.Collections.Generic;
using BinaryRecords.Enums;
using Krypton.Buffers;

namespace BinaryRecords.Records
{
    public record ConstructableTypeRecord(
            string Alias, 
            IReadOnlyList<(uint Key, TypeRecord MemberType)> Members, 
            bool Versioned) 
        : TypeRecord(SerializableDataTypes.Constructable)
    {
        protected override void DoHash(ref SpanBufferWriter bufferWriter)
        {
            bufferWriter.WriteUTF8String(Alias);
            foreach (var (key, memberType) in Members)
            {
                bufferWriter.WriteUInt32(key);
                memberType.Hash(ref bufferWriter);
            }
        }
    }
}