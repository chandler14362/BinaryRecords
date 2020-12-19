using System;
using System.Linq.Expressions;

namespace BinaryRecords.Delegates
{
    public delegate Expression GenerateDeserializeExpressionDelegate(
        BinarySerializer serializer, 
        Type type, 
        Expression bufferAccess);
}