using System;
using System.Buffers;
using System.Linq.Expressions;

namespace BinaryRecords.Util
{
    public static class ByteArrayPoolExpressions
    {
        private static readonly Type ByteArrayPoolType = typeof(ArrayPool<byte>);

        public static Expression Rent(int size) =>
            Expression.Call(
                Expression.Call(ByteArrayPoolType.GetProperty("Shared")!.GetMethod!),
                ByteArrayPoolType.GetMethod("Rent")!,
                Expression.Constant(size));
        
        public static Expression Return(Expression rented) =>
            Expression.Call(
                Expression.Call(ByteArrayPoolType.GetProperty("Shared")!.GetMethod!),
                ByteArrayPoolType.GetMethod("Return")!,
                rented,
                Expression.Constant(false));
    }
}