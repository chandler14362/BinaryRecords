using System;
using System.Linq.Expressions;

namespace BinaryRecords.Providers
{
    public delegate Expression GenerateSerializeExpressionDelegate(BinarySerializer serializer, Type type, 
        Expression dataAccess, Expression bufferAccess);

    public delegate Expression GenerateDeserializeExpressionDelegate(BinarySerializer serializer, Type type, 
        Expression bufferAccess);

    public record ExpressionGeneratorProvider(
        Func<Type, bool> IsInterested,
        Func<Type, bool> Validate,
        GenerateSerializeExpressionDelegate GenerateSerializeExpression,
        GenerateDeserializeExpressionDelegate GenerateDeserializeExpression);
}
