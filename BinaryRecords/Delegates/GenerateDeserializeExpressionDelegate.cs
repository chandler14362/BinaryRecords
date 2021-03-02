using System;
using System.Linq.Expressions;

namespace BinaryRecords.Delegates
{
    public delegate Expression GenerateDeserializeExpressionDelegate(
        TypeSerializer serializer, 
        Type type, 
        Expression bufferAccess);
}