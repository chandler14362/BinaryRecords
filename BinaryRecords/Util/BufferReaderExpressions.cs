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

        public static Expression ReadUInt32(Expression bufferAccess) =>
            Expression.Call(
                bufferAccess,
                SpanBufferReaderType.GetMethod("ReadUInt32")!);
        
        public static Expression ReadGuid(Expression bufferAccess) =>
            Expression.Call(
                bufferAccess,
                SpanBufferReaderType.GetMethod("ReadGuid")!);

        public static Expression SkipBytes(Expression bufferAccess, Expression count) =>
            Expression.Call(
                bufferAccess,
                SpanBufferReaderType.GetMethod("SkipBytes")!,
                count);
    }
}