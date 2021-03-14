using System;
using System.Linq.Expressions;
using BinaryRecords.Extensions;

namespace BinaryRecords.Expressions
{
    public static class BufferWriterExpressions
    {
        private static readonly Type BinaryBufferWriterType = typeof(BinaryBufferWriter);
        
        public static Expression Size(Expression buffer) =>
            Expression.PropertyOrField(buffer, "Size");

        public static Expression WriteBool(Expression buffer, Expression value) =>
            Expression.Call(buffer, BinaryBufferWriterType.GetMethod("WriteBool")!, value);
        
        public static Expression WriteInt8(Expression buffer, Expression value) =>
            Expression.Call(buffer, BinaryBufferWriterType.GetMethod("WriteInt8")!, value);
        
        public static Expression WriteUInt8(Expression buffer, Expression value) =>
            Expression.Call(buffer, BinaryBufferWriterType.GetMethod("WriteUInt8")!, value);

        public static Expression WriteInt16(Expression buffer, Expression value) => 
            Expression.Call(buffer, BinaryBufferWriterType.GetMethod("WriteInt16")!, value);

        public static Expression WriteUInt16(Expression buffer, Expression value) => 
            Expression.Call(buffer, BinaryBufferWriterType.GetMethod("WriteUInt16")!, value);

        public static Expression WriteInt32(Expression buffer, Expression value) => 
            Expression.Call(buffer, BinaryBufferWriterType.GetMethod("WriteInt32")!, value);

        public static Expression WriteUInt32(Expression buffer, Expression value) => 
            Expression.Call(buffer, BinaryBufferWriterType.GetMethod("WriteUInt32")!, value);

        public static Expression WriteInt64(Expression buffer, Expression value) => 
            Expression.Call(buffer, BinaryBufferWriterType.GetMethod("WriteInt64")!, value);

        public static Expression WriteUInt64(Expression buffer, Expression value) => 
            Expression.Call(buffer, BinaryBufferWriterType.GetMethod("WriteUInt64")!, value);

        public static Expression WriteSingle(Expression buffer, Expression value) => 
            Expression.Call(buffer, BinaryBufferWriterType.GetMethod("WriteSingle")!, value);

        public static Expression WriteDouble(Expression buffer, Expression value) => 
            Expression.Call(buffer, BinaryBufferWriterType.GetMethod("WriteDouble")!, value);

        public static Expression WriteUTF8String(Expression buffer, Expression value) =>
            Expression.Call(buffer, BinaryBufferWriterType.GetMethod("WriteUTF8String")!, value);

        public static Expression WriteGuid(Expression buffer, Expression value) =>
            Expression.Call(buffer, BinaryBufferWriterType.GetMethod("WriteGuid")!, value);
        
        public static Expression ReserveBookmark(Expression buffer, Expression size) =>
            Expression.Call(buffer, BinaryBufferWriterType.GetMethod("ReserveBookmark")!, size);

        public static unsafe Expression ReserveBookmark<T>(Expression buffer) where T : unmanaged =>
            ReserveBookmark(buffer, Expression.Constant(sizeof(T)));

        public static Expression WriteUInt16Bookmark(Expression buffer, Expression bookmark, Expression value) =>
            Expression.Call(
                typeof(BufferExtensions).GetMethod("WriteUInt16Bookmark")!, 
                buffer, 
                bookmark, 
                value);

        public static Expression PadBytes(Expression buffer, Expression count) =>
            Expression.Call(buffer, BinaryBufferWriterType.GetMethod("PadBytes")!, count);
    }
}