using BinaryRecords.Enums;
using BinaryRecords.Util;

namespace BinaryRecords.Records
{
    public record ListTypeRecord(TypeRecord ElementType) 
        : TypeRecord(SerializableDataTypes.List)
    {
    }
}