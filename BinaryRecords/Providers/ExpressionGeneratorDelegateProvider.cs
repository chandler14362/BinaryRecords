using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using BinaryRecords.Abstractions;
using BinaryRecords.Delegates;
using BinaryRecords.Util;
using Krypton.Buffers;

namespace BinaryRecords.Providers
{
    public static class ExpressionGeneratorDelegateProvider
    {
        private static readonly Dictionary<Type, Delegate> CachedSerializeDelegates = new();
        private static readonly Dictionary<Type, Delegate> CachedDeserializeDelegates = new();
        
        public static Delegate CreateSerializeDelegate(Type type, ITypingLibrary typingLibrary)
        {
            if (CachedSerializeDelegates.TryGetValue(type, out var cachedDelegate))
                return cachedDelegate;
            
            var bufferAccess = Expression.Parameter(typeof(SpanBufferWriter).MakeByRefType(), "buffer");
            var dataAccess = Expression.Parameter(type, "obj");
            
            var delegateType = typeof(GenericSerializeDelegate<>).MakeGenericType(type);
            var lambda = Expression.Lambda(
                delegateType, 
                typingLibrary.GenerateSerializeExpression(type, dataAccess, bufferAccess), 
                dataAccess, bufferAccess);
            return CachedSerializeDelegates[type] = lambda.Compile();
        }

        public static Delegate CreateDeserializeDelegate(Type type, ITypingLibrary typingLibrary)
        {
            if (CachedDeserializeDelegates.TryGetValue(type, out var cachedDelegate))
                return cachedDelegate;

            var bufferAccess = Expression.Parameter(typeof(SpanBufferReader).MakeByRefType(), "buffer");
            
            var blockBuilder = new ExpressionBlockBuilder();
            var returnTarget = Expression.Label(type);
            blockBuilder += Expression.Return(returnTarget, typingLibrary.GenerateDeserializeExpression(type, bufferAccess));

            var delegateType = typeof(GenericDeserializeDelegate<>).MakeGenericType(type);
            var lambda = Expression.Lambda(delegateType, blockBuilder, bufferAccess);
            return CachedDeserializeDelegates[type] = lambda.Compile();
        }
    }
}