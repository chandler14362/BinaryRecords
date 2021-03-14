using System;
using System.Linq.Expressions;
using BinaryRecords.Abstractions;
using BinaryRecords.Expressions;

namespace BinaryRecords.Delegates
{
    public delegate Expression GenerateSerializeExpressionDelegate(
        ITypingLibrary typingLibrary,
        Type type,
        Expression buffer,
        Expression data,
        VersionWriter? versioning = null);
}