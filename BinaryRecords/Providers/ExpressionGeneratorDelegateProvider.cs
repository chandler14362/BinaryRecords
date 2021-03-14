using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using BinaryRecords.Abstractions;
using BinaryRecords.Delegates;

namespace BinaryRecords.Providers
{
    public static class ExpressionGeneratorDelegateProvider
    {
        public static Delegate CreateSerializeDelegate(ITypingLibrary typingLibrary, Type type)
        {
            var bufferAccess = Expression.Parameter(typeof(BinaryBufferWriter).MakeByRefType(), "buffer");
            var dataAccess = Expression.Parameter(type, "obj");
            var delegateType = typeof(GenericSerializeDelegate<>).MakeGenericType(type);
            var lambda = Expression.Lambda(
                delegateType, 
                typingLibrary.GenerateSerializeExpression(type, bufferAccess, dataAccess), 
                dataAccess, bufferAccess);
            return lambda.Compile();
        }

        public static Delegate CreateDeserializeDelegate(ITypingLibrary typingLibrary, Type type)
        {
            var bufferAccess = Expression.Parameter(typeof(BinaryBufferReader).MakeByRefType(), "buffer");
            var delegateType = typeof(GenericDeserializeDelegate<>).MakeGenericType(type);
            var lambda = Expression.Lambda(
                delegateType, 
                typingLibrary.GenerateDeserializeExpression(type, bufferAccess), 
                bufferAccess);
            return lambda.Compile();
        }
    }
}