using System.Collections.Generic;
using BinaryRecords.Enums;
using BinaryRecords.Util;
using Krypton.Buffers;

namespace BinaryRecords.Records
{
    public record SequenceTypeRecord(IReadOnlyList<TypeRecord> MemberTypes) 
        : TypeRecord(SerializableDataTypes.Sequence)
    {
        protected override void DoHash(
            ref SpanBufferWriter bufferWriter, 
            ConstructableHashTracker constructableHashTracker)
        {
            foreach (var member in MemberTypes)
                member.Hash(ref bufferWriter, constructableHashTracker);
        }
    }
}