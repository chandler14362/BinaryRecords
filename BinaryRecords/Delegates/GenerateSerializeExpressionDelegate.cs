using System;
using System.Linq.Expressions;
using BinaryRecords.Abstractions;

namespace BinaryRecords.Delegates
{
    public delegate Expression GenerateSerializeExpressionDelegate(
        ITypingLibrary typingLibrary, 
        Type type, 
        Expression dataAccess, 
        Expression bufferAccess);
}