using System;
using System.Linq.Expressions;

namespace BinaryRecords.Providers
{
    public delegate Expression GenerateSerializeExpressionDelegate(BinarySerializer serializer, Type type, Expression dataAccess, StackFrame stackFrame);

    public delegate Expression GenerateDeserializeExpressionDelegate(BinarySerializer serializer, Type type, StackFrame stackFrame);

    public record ExpressionGeneratorProvider(
        Func<Type, bool> IsInterested,
        Func<Type, bool> Validate,
        GenerateSerializeExpressionDelegate GenerateSerializeExpression,
        GenerateDeserializeExpressionDelegate GenerateDeserializeExpression);
}
