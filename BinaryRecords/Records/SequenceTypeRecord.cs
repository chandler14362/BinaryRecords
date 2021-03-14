using System.Collections.Generic;
using BinaryRecords.Enums;
using BinaryRecords.Util;

namespace BinaryRecords.Records
{
    public record SequenceTypeRecord(IReadOnlyList<TypeRecord> MemberTypes) 
        : TypeRecord(SerializableDataTypes.Sequence)
    {
    }
}