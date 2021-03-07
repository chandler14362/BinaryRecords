using System;
using BinaryRecords.Abstractions;
using BinaryRecords.Delegates;

namespace BinaryRecords.Providers
{
    public record BlittableExpressionGeneratorProvider(
            string Name,
            ProviderPriority Priority,
            Func<Type, ITypingLibrary, bool> IsInterested,
            GenerateSerializeExpressionDelegate GenerateSerializeExpression,
            GenerateDeserializeExpressionDelegate GenerateDeserializeExpression,
            GenerateTypeRecordDelegate GenerateTypeRecord)
        : ExpressionGeneratorProvider(
            Name,
            Priority,
            IsInterested,
            GenerateSerializeExpression,
            GenerateDeserializeExpression,
            GenerateTypeRecord);
}