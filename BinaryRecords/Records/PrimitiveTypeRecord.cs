using BinaryRecords.Enums;
using BinaryRecords.Util;

namespace BinaryRecords.Records
{
    public record PrimitiveTypeRecord(SerializableDataTypes SerializableType) 
        : TypeRecord(SerializableType)
    {
    }
}