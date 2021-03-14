using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using BinaryRecords.Delegates;
using BinaryRecords.Enums;
using BinaryRecords.Expressions;
using BinaryRecords.Providers;
using BinaryRecords.Records;
using BinaryRecords.Util;

namespace BinaryRecords.Abstractions
{
    public interface ITypingLibrary
    {
        BitSize BitSize { get; init; }
        
        void AddGeneratorProvider<T>(
            SerializeExtensionDelegate<T> serializerDelegate,
            DeserializeExtensionDelegate<T> deserializerDelegate,
            string? name = null,
            ProviderPriority priority = ProviderPriority.High);
        void AddGeneratorProvider(ExpressionGeneratorProvider expressionGeneratorProvider);
        ExpressionGeneratorProvider? GetInterestedGeneratorProvider(Type type);

        TypeRecord GetTypeRecord(Type type);

        bool IsTypeSerializable(Type type);
        bool IsTypeBlittable(Type type);

        Expression GenerateSerializeExpression(
            Type type, 
            Expression buffer, 
            Expression data, 
            VersionWriter? versioning = null);
        Delegate GetSerializeDelegate(Type type);

        Expression GenerateDeserializeExpression(Type type, Expression buffer);
        Delegate GetDeserializeDelegate(Type type);
        
        IReadOnlyList<ExpressionGeneratorProvider> GetExpressionGeneratorProviders();
    }
}
