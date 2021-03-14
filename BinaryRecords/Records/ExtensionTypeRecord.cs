using BinaryRecords.Enums;
using BinaryRecords.Util;

namespace BinaryRecords.Records
{
    public record ExtensionTypeRecord() 
        : TypeRecord(SerializableDataTypes.Extension)
    {
    }
}