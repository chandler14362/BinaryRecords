using System;
using BinaryRecords.Delegates;
using BinaryRecords.Interfaces;

namespace BinaryRecords.Providers
{
    public record ExpressionGeneratorProvider(
        ProviderPriority Priority,
        Func<Type, ITypeLibrary, bool> IsInterested,
        GenerateSerializeExpressionDelegate GenerateSerializeExpression,
        GenerateDeserializeExpressionDelegate GenerateDeserializeExpression);
}
