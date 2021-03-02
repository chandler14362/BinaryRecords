using BinaryRecords.Delegates;

namespace BinaryRecords.Models
{
    internal record RecordSerializationInvocationModel(
        SerializeRecordDelegate Serialize,
        DeserializeRecordDelegate Deserialize);
}
