using System.Collections.Generic;
using BinaryRecords.Enums;

namespace BinaryRecords.Records
{
    public record ConstructableTypeRecord(IReadOnlyList<(uint Key, TypeRecord MemberType)> Members, bool Versioned) 
        : TypeRecord(SerializableDataTypes.Constructable)
    {
    }
}