using System;
using System.Linq.Expressions;

namespace BinaryRecords.Expressions
{
    public static class BufferReaderExpressions
    {
        private static readonly Type BinaryBufferReaderType = typeof(BinaryBufferReader);

        public static Expression ReadBool(Expression buffer) =>
            Expression.Call(buffer, BinaryBufferReaderType.GetMethod("ReadBool")!);
        
        public static Expression ReadInt8(Expression buffer) =>
            Expression.Call(buffer, BinaryBufferReaderType.GetMethod("ReadInt8")!);
        
        public static Expression ReadUInt8(Expression buffer) =>
            Expression.Call(buffer, BinaryBufferReaderType.GetMethod("ReadUInt8")!);

        public static Expression ReadInt16(Expression buffer) => 
            Expression.Call(buffer, BinaryBufferReaderType.GetMethod("ReadInt16")!);

        public static Expression ReadUInt16(Expression buffer) => 
            Expression.Call(buffer, BinaryBufferReaderType.GetMethod("ReadUInt16")!);

        public static Expression ReadInt32(Expression buffer) => 
            Expression.Call(buffer, BinaryBufferReaderType.GetMethod("ReadInt32")!);

        public static Expression ReadUInt32(Expression buffer) => 
            Expression.Call(buffer, BinaryBufferReaderType.GetMethod("ReadUInt32")!);

        public static Expression ReadInt64(Expression buffer) =>
            Expression.Call(buffer, BinaryBufferReaderType.GetMethod("ReadInt64")!);

        public static Expression ReadUInt64(Expression buffer) => 
            Expression.Call(buffer, BinaryBufferReaderType.GetMethod("ReadUInt64")!);

        public static Expression ReadSingle(Expression buffer) => 
            Expression.Call(buffer, BinaryBufferReaderType.GetMethod("ReadSingle")!);

        public static Expression ReadDouble(Expression buffer) => 
            Expression.Call(buffer, BinaryBufferReaderType.GetMethod("ReadDouble")!);

        public static Expression ReadUTF8String(Expression buffer) =>
            Expression.Call(buffer, BinaryBufferReaderType.GetMethod("ReadUTF8String")!);

        public static Expression ReadGuid(Expression buffer) =>
            Expression.Call(buffer, BinaryBufferReaderType.GetMethod("ReadGuid")!);

        public static Expression SkipBytes(Expression buffer, Expression count) =>
            Expression.Call(buffer, BinaryBufferReaderType.GetMethod("SkipBytes")!, count);
    }
}