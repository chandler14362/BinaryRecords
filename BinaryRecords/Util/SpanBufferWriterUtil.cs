using System;
using System.Reflection;
using Krypton.Buffers;

namespace BinaryRecords.Util
{
    public static class SpanBufferWriterUtil
    {
        public static MethodInfo FixedFromArrayMethod = typeof(SpanBufferWriterUtil).GetMethod("FixedFromArray")!;
        
        public static SpanBufferWriter FixedFromArray(byte[] backingArray, int size) =>
            new (backingArray.AsSpan(0, size), false);
    }
}