using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using BinaryRecords.Interfaces;

namespace BinaryRecords.Providers
{
    public delegate Expression GenerateSerializeExpressionDelegate(BinarySerializer serializer, Type type, 
        Expression dataAccess, Expression bufferAccess);

    public delegate Expression GenerateDeserializeExpressionDelegate(BinarySerializer serializer, Type type, 
        Expression bufferAccess);

    public record ExpressionGeneratorProvider(
        ProviderPriority Priority,
        Func<Type, ITypeLibrary, bool> IsInterested,
        GenerateSerializeExpressionDelegate GenerateSerializeExpression,
        GenerateDeserializeExpressionDelegate GenerateDeserializeExpression);

    public static class ExpressionGeneratorProviderExtensions
    {
        public static ExpressionGeneratorProvider GetInterestedProvider(
            this IEnumerable<ExpressionGeneratorProvider> providers, Type type, ITypeLibrary library)
        {
            // We take the first 2 interested providers, this helps us check for ambiguous interest
            var interested = providers
                .Where(p => p.IsInterested(type, library))
                .OrderByDescending(p => p.Priority)
                .Take(2).ToArray();
            
            return interested.Length switch
            {
                1 => interested[0],
                2 => interested[0].Priority != interested[1].Priority 
                    ? interested[0] 
                    : throw new Exception($"Multiple providers have ambiguous interest in type: {type.Name}, priority: {interested[0].Priority}"),
                _ => null
            } ;
        }
    }
}
