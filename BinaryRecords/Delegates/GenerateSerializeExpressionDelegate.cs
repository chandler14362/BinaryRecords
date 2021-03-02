using System;
using System.Linq.Expressions;

namespace BinaryRecords.Delegates
{
    public delegate Expression GenerateSerializeExpressionDelegate(
        BinarySerializer serializer, 
        Type type, 
        Expression dataAccess, 
        Expression bufferAccess);
}