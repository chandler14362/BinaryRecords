#if NETSTANDARD2_0
using System;

namespace BinaryRecords.Delegates
{
    public delegate void ReadOnlySpanAction<T,in TArg>(ReadOnlySpan<T> span, TArg arg);
}
#endif