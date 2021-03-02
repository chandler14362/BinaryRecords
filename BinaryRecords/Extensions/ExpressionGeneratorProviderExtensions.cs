#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BinaryRecords.Abstractions;
using BinaryRecords.Providers;

namespace BinaryRecords.Extensions
{
    public static class ExpressionGeneratorProviderExtensions
    {
        public static ExpressionGeneratorProvider? GetInterestedProvider(
            this IEnumerable<ExpressionGeneratorProvider> providers, Type type, ITypingLibrary library)
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
            };
        }

        public static bool TryGetInterestedProvider(
            this IEnumerable<ExpressionGeneratorProvider> providers, Type type, ITypingLibrary library,
            [MaybeNullWhen(false)] out ExpressionGeneratorProvider provider) => 
            (provider = GetInterestedProvider(providers, type, library)) != null;
    }
}
