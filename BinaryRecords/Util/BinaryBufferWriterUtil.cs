using System;
using System.Reflection;

namespace BinaryRecords.Util
{
    public static class BinaryBufferWriterUtil
    {
        public static readonly MethodInfo FixedFromArrayMethod = typeof(BinaryBufferWriterUtil).GetMethod("FixedFromArray")!;
        
        public static BinaryBufferWriter FixedFromArray(byte[] backingArray, int size) =>
            new (backingArray.AsSpan(0, size), false);
    }
}