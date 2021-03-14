using BinaryRecords.Delegates;

namespace BinaryRecords.Providers
{
    public record ExpressionGeneratorProvider(
        string Name,
        ProviderPriority Priority,
        ProviderIsInterestedDelegate IsInterested,
        GenerateSerializeExpressionDelegate GenerateSerializeExpression,
        GenerateDeserializeExpressionDelegate GenerateDeserializeExpression,
        GenerateTypeRecordDelegate GenerateTypeRecord);
}
