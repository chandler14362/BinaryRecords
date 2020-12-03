using System;
using System.Linq.Expressions;

namespace BinaryRecords.Delegates
{
    public delegate Expression GenerateAddElementExpressionDelegate(Expression instance, Type type,
        Func<Expression> deserializeType);
}
