using System;
using System.Buffers;
using System.Linq.Expressions;

namespace BinaryRecords.Expressions
{
    public static class ArrayPoolExpressions<T>
    {
        private static readonly Type ArrayPoolType = typeof(ArrayPool<T>);

        public static Expression Rent(Expression size) =>
            Expression.Call(
                Expression.Call(ArrayPoolType.GetProperty("Shared")!.GetMethod!),
                ArrayPoolType.GetMethod("Rent")!,
                size);

        public static Expression Return(Expression rented) =>
            Expression.Call(
                Expression.Call(ArrayPoolType.GetProperty("Shared")!.GetMethod!),
                ArrayPoolType.GetMethod("Return")!,
                rented,
                Expression.Constant(false));
    }
}