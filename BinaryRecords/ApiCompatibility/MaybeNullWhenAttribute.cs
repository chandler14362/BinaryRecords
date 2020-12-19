#if NETSTANDARD2_0
namespace System.Diagnostics.CodeAnalysis
{
    [System.AttributeUsageAttribute(System.AttributeTargets.Parameter, Inherited=false)]
    public sealed class MaybeNullWhenAttribute : System.Attribute
    {
        public MaybeNullWhenAttribute(bool returnValue) { }
        public bool ReturnValue { get { throw null; } }
    }
}
#endif
