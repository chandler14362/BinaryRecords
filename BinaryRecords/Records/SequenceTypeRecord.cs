using System.Collections.Generic;
using BinaryRecords.Enums;
using Krypton.Buffers;

namespace BinaryRecords.Records
{
    public record SequenceTypeRecord(IReadOnlyList<TypeRecord> MemberTypes) : TypeRecord(SerializableDataTypes.Sequence)
    {
        protected override void DoHash(ref SpanBufferWriter bufferWriter)
        {
            foreach (var member in MemberTypes)
                member.Hash(ref bufferWriter);
        }
    }
}