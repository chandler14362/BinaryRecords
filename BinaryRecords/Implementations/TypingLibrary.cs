using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using BinaryRecords.Abstractions;
using BinaryRecords.Delegates;
using BinaryRecords.Extensions;
using BinaryRecords.Models;
using BinaryRecords.Providers;
using BinaryRecords.Records;
using BinaryRecords.Util;

namespace BinaryRecords.Implementations
{
    public sealed class TypingLibrary : ITypingLibrary
    {
        private readonly List<ExpressionGeneratorProvider> _expressionGeneratorProviders = new();
        private readonly Dictionary<Type, RecordConstructionModel> _recordConstructionModels = new();
        private readonly Dictionary<Type, ExpressionGeneratorProvider> _typeProviderCache = new();
        private readonly Dictionary<Type, TypeRecord> _typeRecords = new();
        private readonly Dictionary<string, ConstructableTypeRecord> _aliasToConstructable = new();
        private uint _lastExtensionId = 0;
        
        public void AddGeneratorProvider<T>(
            GenericSerializeDelegate<T> serializerDelegate, 
            GenericDeserializeDelegate<T> deserializerDelegate,
            string? name=null,
            ProviderPriority priority=ProviderPriority.High)
        {
            name ??= $"{typeof(T).FullName}ExpressionGeneratorProvider";
            var provider = new ExpressionGeneratorProvider(
                Name: name,
                Priority: priority,
                IsInterested: (type, _) => type == typeof(T),
                GenerateSerializeExpression: (_, _, dataAccess, bufferAccess) => 
                    Expression.Invoke(Expression.Constant(serializerDelegate), bufferAccess, dataAccess),
                GenerateDeserializeExpression: (_, _, bufferAccess) => 
                    Expression.Invoke(Expression.Constant(deserializerDelegate), bufferAccess),
                GenerateTypeRecord: (type, typingLibrary) => new ExtensionTypeRecord(_lastExtensionId++));
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
            typeRecord = _typeRecords[type] = provider!.GenerateTypeRecord(type, this);
            
            // If it's a constructable keep track of the alias
            if (typeRecord is ConstructableTypeRecord constructable)
                _aliasToConstructable[constructable.Alias] = constructable;
            
            return typeRecord;
        }

        public bool IsTypeSerializable(Type type) =>
            _expressionGeneratorProviders.TryGetInterestedProvider(type, this, out _);

        public bool IsTypeBlittable(Type type) => 
            _expressionGeneratorProviders.TryGetInterestedProvider(type, this, out var provider) && 
            provider is BlittableExpressionGeneratorProvider;

        public Expression GenerateSerializeExpression(Type type, Expression dataAccess, Expression bufferAccess)
        {
            var provider = GetInterestedGeneratorProvider(type);
            if (provider == null)
                ThrowHelpers.ThrowNoInterestedProvider(type);
            return provider!.GenerateSerializeExpression(this, type, dataAccess, bufferAccess);
        }

        public Delegate GetSerializeDelegate(Type type) =>
            ExpressionGeneratorDelegateProvider.CreateSerializeDelegate(type, this);

        public Expression GenerateDeserializeExpression(Type type, Expression bufferAccess)
        {
            var provider = GetInterestedGeneratorProvider(type);
            if (provider == null)
                ThrowHelpers.ThrowNoInterestedProvider(type);
            return provider!.GenerateDeserializeExpression(this, type, bufferAccess);
        }

        public Delegate GetDeserializeDelegate(Type type) =>
            ExpressionGeneratorDelegateProvider.CreateDeserializeDelegate(type, this);
        
        public IEnumerable<RecordConstructionModel> GetRecordConstructionModels() =>
            _recordConstructionModels.Values;

        public IReadOnlyList<ExpressionGeneratorProvider> GetExpressionGeneratorProviders() =>
            _expressionGeneratorProviders;
    }
}