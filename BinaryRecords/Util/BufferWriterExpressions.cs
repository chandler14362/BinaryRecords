using System;
using System.Linq.Expressions;
using BinaryRecords.Extensions;
using Krypton.Buffers;

namespace BinaryRecords.Util
{
    public static class BufferWriterExpressions
    {
        private static readonly Type SpanBufferWriterType = typeof(SpanBufferWriter);
        
        public static Expression BufferSize(Expression bufferAccess) =>
            Expression.PropertyOrField(bufferAccess, "Size");

        public static Expression WriteBool(Expression bufferAccess, Expression value) =>
            Expression.Call(
                bufferAccess,
                SpanBufferWriterType.GetMethod("WriteBool")!,
                value);

        public static Expression WriteUInt32(Expression bufferAccess, Expression value) =>
            Expression.Call(
                bufferAccess,
                SpanBufferWriterType.GetMethod("WriteUInt32")!,
                value);

        public static Expression WriteGuid(Expression bufferAccess, Expression value) =>
            Expression.Call(
                bufferAccess,
                SpanBufferWriterType.GetMethod("WriteGuid")!,
                value);
        
        public static Expression ReserveBookmark(Expression bufferAccess, int size) =>
            Expression.Call(
                bufferAccess, 
                SpanBufferWriterType.GetMethod("ReserveBookmark")!, 
                Expression.Constant(size));

        public static unsafe Expression ReserveBookmark<T>(Expression bufferAccess)
            where T : unmanaged =>
            ReserveBookmark(bufferAccess, sizeof(T));

        public static Expression WriteUInt16Bookmark(Expression bufferAccess, Expression bookmark, Expression value) =>
            Expression.Call(
                typeof(BufferExtensions).GetMethod("WriteUInt16Bookmark")!,
                bufferAccess,
                bookmark,
                value
            );
        
        public static Expression WriteUInt32Bookmark(Expression bufferAccess, Expression bookmark, Expression value) =>
            Expression.Call(
                typeof(BufferExtensions).GetMethod("WriteUInt32Bookmark")!,
                bufferAccess,
                bookmark,
                value
            );
        
        public static Expression WriteUInt64Bookmark(Expression bufferAccess, Expression bookmark, Expression value) =>
            Expression.Call(
                typeof(BufferExtensions).GetMethod("WriteUInt64Bookmark")!,
                bufferAccess,
                bookmark,
                value
            );
    }
}