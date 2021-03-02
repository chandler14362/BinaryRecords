using System;
using BinaryRecords.Abstractions;
using BinaryRecords.Delegates;

namespace BinaryRecords.Providers
{
    public record ExpressionGeneratorProvider(
        string Name,
        ProviderPriority Priority,
        Func<Type, ITypingLibrary, bool> IsInterested,
        GenerateSerializeExpressionDelegate GenerateSerializeExpression,
        GenerateDeserializeExpressionDelegate GenerateDeserializeExpression);
}
