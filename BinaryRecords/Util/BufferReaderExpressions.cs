using System;
using System.Linq.Expressions;
using Krypton.Buffers;

namespace BinaryRecords.Util
{
    public static class BufferReaderExpressions
    {
        private static readonly Type SpanBufferReaderType = typeof(SpanBufferReader);

        public static Expression ReadBool(Expression bufferAccess) =>
            Expression.Call(
                bufferAccess,
                SpanBufferReaderType.GetMethod("ReadBool")!);
        
        public static Expression ReadGuid(Expression bufferAccess) =>
            Expression.Call(
                bufferAccess,
                SpanBufferReaderType.GetMethod("ReadGuid")!);
    }
}