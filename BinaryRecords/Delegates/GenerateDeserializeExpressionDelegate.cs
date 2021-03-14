using System;
using System.Linq.Expressions;
using BinaryRecords.Abstractions;

namespace BinaryRecords.Delegates
{
    public delegate Expression GenerateDeserializeExpressionDelegate(            
        ITypingLibrary typingLibrary,
        Type type,
        Expression buffer);
}