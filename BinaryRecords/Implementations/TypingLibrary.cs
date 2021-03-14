using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using BinaryRecords.Abstractions;
using BinaryRecords.Delegates;
using BinaryRecords.Enums;
using BinaryRecords.Expressions;
using BinaryRecords.Extensions;
using BinaryRecords.Providers;
using BinaryRecords.Records;
using BinaryRecords.Util;

namespace BinaryRecords.Implementations
{
    public sealed class TypingLibrary : ITypingLibrary
    {
        private readonly List<ExpressionGeneratorProvider> _expressionGeneratorProviders = new();
        private readonly Dictionary<Type, ExpressionGeneratorProvider> _typeProviderCache = new();
        private readonly Dictionary<Type, TypeRecord> _typeRecords = new();

        private readonly Dictionary<Type, Delegate> _serializeDelegates = new();
        private readonly Dictionary<Type, Delegate> _deserializeDelegates = new();
        
        public BitSize BitSize { get; init; }

        public void AddGeneratorProvider<T>(
            SerializeExtensionDelegate<T> serializerDelegate, 
            DeserializeExtensionDelegate<T> deserializerDelegate,
            string? name=null,
            ProviderPriority priority=ProviderPriority.High)
        {
            name ??= $"{typeof(T).FullName}ExpressionGeneratorProvider";
            var provider = new ExpressionGeneratorProvider(
                Name: name,
                Priority: priority,
                IsInterested: (type, _) => type == typeof(T),
                GenerateSerializeExpression: (typingLibrary, type, buffer, data, versioning) =>
                {
                    var blockBuilder = new ExpressionBlockBuilder();
                    versioning?.Start(blockBuilder, buffer, typingLibrary.BitSize);
                    blockBuilder += Expression.Invoke(Expression.Constant(serializerDelegate),data, buffer);
                    versioning?.Stop(blockBuilder, buffer, typingLibrary.BitSize);
                    return blockBuilder;
                },
                GenerateDeserializeExpression: (typingLibrary, type, buffer) => 
                    Expression.Invoke(Expression.Constant(deserializerDelegate), buffer),
                GenerateTypeRecord: (_, _) => new ExtensionTypeRecord());
            _expressionGeneratorProviders.Add(provider);
        }

        public void AddGeneratorProvider(ExpressionGeneratorProvider expressionGeneratorProvider) =>
            _expressionGeneratorProviders.Add(expressionGeneratorProvider);

        public ExpressionGeneratorProvider? GetInterestedGeneratorProvider(Type type)
        {
            if (_typeProviderCache.TryGetValue(type, out var provider)) 
                return provider;
            if (!_expressionGeneratorProviders.TryGetInterestedProvider(type, this, out provider)) 
                return null;
            return _typeProviderCache[type] = provider;
        }

        public TypeRecord GetTypeRecord(Type type)
        {
            if (_typeRecords.TryGetValue(type, out var typeRecord))
                return typeRecord;
            var provider = GetInterestedGeneratorProvider(type);
            if (provider is null)
                ThrowHelpers.ThrowNoInterestedProvider(type);
            return _typeRecords[type] = provider!.GenerateTypeRecord(this, type);
        }

        public bool IsTypeSerializable(Type type) => GetInterestedGeneratorProvider(type) != null;

        public bool IsTypeBlittable(Type type)
        {
            var provider = GetInterestedGeneratorProvider(type);
            return provider != null && provider is BlittableExpressionGeneratorProvider;
        }

        public Expression GenerateSerializeExpression(Type type, Expression buffer, Expression data, VersionWriter? versioning = null)
        {
            var provider = GetInterestedGeneratorProvider(type);
            if (provider == null)
                ThrowHelpers.ThrowNoInterestedProvider(type);
            return provider!.GenerateSerializeExpression(this, type, buffer, data, versioning);
        }

        public Delegate GetSerializeDelegate(Type type)
        {
            if (_serializeDelegates.TryGetValue(type, out var @delegate))
                return @delegate;
            return _serializeDelegates[type] = ExpressionGeneratorDelegateProvider.CreateSerializeDelegate(this, type);
        }

        public Expression GenerateDeserializeExpression(Type type, Expression buffer)
        {
            var provider = GetInterestedGeneratorProvider(type);
            if (provider == null)
                ThrowHelpers.ThrowNoInterestedProvider(type);
            return provider!.GenerateDeserializeExpression(this, type, buffer);
        }

        public Delegate GetDeserializeDelegate(Type type)
        {
            if (_deserializeDelegates.TryGetValue(type, out var @delegate))
                return @delegate;
            return _deserializeDelegates[type] = ExpressionGeneratorDelegateProvider.CreateDeserializeDelegate(this, type);
        }

        public IReadOnlyList<ExpressionGeneratorProvider> GetExpressionGeneratorProviders() => 
            _expressionGeneratorProviders;
    }
}