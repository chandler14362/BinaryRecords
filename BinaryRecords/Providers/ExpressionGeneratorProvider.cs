using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace BinaryRecords.Providers
{
    public delegate Expression GenerateSerializeExpressionDelegate(BinarySerializer serializer, Type type, 
        Expression dataAccess, Expression bufferAccess);

    public delegate Expression GenerateDeserializeExpressionDelegate(BinarySerializer serializer, Type type, 
        Expression bufferAccess);

    public record ExpressionGeneratorProvider(
        ProviderPriority Priority,
        Func<Type, bool> IsInterested,
        Func<Type, bool> Validate,
        GenerateSerializeExpressionDelegate GenerateSerializeExpression,
        GenerateDeserializeExpressionDelegate GenerateDeserializeExpression)
    {
        // TODO: I don't think this really belongs here, it should probably be moved
        /// <summary>
        /// The ExpressionGeneratorProviders builtin to BinaryRecords
        /// </summary>
        public static IEnumerable<ExpressionGeneratorProvider> Builtins
            => PrimitiveExpressionGeneratorProviders.Builtin
                .Concat(CollectionExpressionGeneratorProviders.Builtin)
                .Concat(MiscExpressionGeneratorProviders.Builtin);
    };
}
