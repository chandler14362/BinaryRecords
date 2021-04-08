#if NETSTANDARD2_0
namespace System.Buffers
{
    public delegate void SpanAction<T, in TArg>(Span<T> span, TArg arg);
}
#endif
