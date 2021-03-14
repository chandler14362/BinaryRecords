using System;
using System.Runtime.CompilerServices;

namespace BinaryRecords.Util
{
    internal static class ThrowHelpers
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowNoInterestedProvider(Type type) =>
            throw new Exception($"No provider interested in type: {type.FullName}");
        
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowInconsistentVersioning(Type type) =>
            throw new Exception($"Inconsistent versioning on type: {type.FullName}");
    }
}