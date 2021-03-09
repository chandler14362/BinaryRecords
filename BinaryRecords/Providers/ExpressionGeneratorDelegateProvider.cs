using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using BinaryRecords.Abstractions;
using BinaryRecords.Delegates;
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
                typingLibrary.GenerateSerializeExpression(type, dataAccess, bufferAccess, null), 
                dataAccess, bufferAccess);
            return CachedSerializeDelegates[type] = lambda.Compile();
        }

        public static Delegate CreateDeserializeDelegate(Type type, ITypingLibrary typingLibrary)
        {
            if (CachedDeserializeDelegates.TryGetValue(type, out var cachedDelegate))
                return cachedDelegate;
            var bufferAccess = Expression.Parameter(typeof(SpanBufferReader).MakeByRefType(), "buffer");
            var delegateType = typeof(GenericDeserializeDelegate<>).MakeGenericType(type);
            var lambda = Expression.Lambda(
                delegateType, 
                typingLibrary.GenerateDeserializeExpression(type, bufferAccess), 
                bufferAccess);
            return CachedDeserializeDelegates[type] = lambda.Compile();
        }
    }
}