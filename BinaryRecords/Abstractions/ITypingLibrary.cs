using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using BinaryRecords.Delegates;
using BinaryRecords.Providers;
using BinaryRecords.Records;
using BinaryRecords.Util;

namespace BinaryRecords.Abstractions
{
    public interface ITypingLibrary
    {
        void AddGeneratorProvider<T>(
            GenericSerializeDelegate<T> serializerDelegate,
            GenericDeserializeDelegate<T> deserializerDelegate,
            string? name = null,
            ProviderPriority priority = ProviderPriority.High);
        void AddGeneratorProvider(ExpressionGeneratorProvider expressionGeneratorProvider);
        ExpressionGeneratorProvider? GetInterestedGeneratorProvider(Type type);

        TypeRecord GetTypeRecord(Type type);

        bool IsTypeSerializable(Type type);
        bool IsTypeBlittable(Type type);

        Expression GenerateSerializeExpression(
            Type type, 
            Expression dataAccess, 
            Expression bufferAccess, 
            AutoVersioning? autoVersioning);
        Delegate GetSerializeDelegate(Type type);

        Expression GenerateDeserializeExpression(Type type, Expression bufferAccess);
        Delegate GetDeserializeDelegate(Type type);
        
        IReadOnlyList<ExpressionGeneratorProvider> GetExpressionGeneratorProviders();
    }
}
