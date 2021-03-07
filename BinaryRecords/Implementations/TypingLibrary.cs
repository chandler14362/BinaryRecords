using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BinaryRecords.Abstractions;
using BinaryRecords.Delegates;
using BinaryRecords.Extensions;
using BinaryRecords.Models;
using BinaryRecords.Providers;
using BinaryRecords.Records;

namespace BinaryRecords.Implementations
{
    public sealed class TypingLibrary : ITypingLibrary
    {
        private readonly List<ExpressionGeneratorProvider> _expressionGeneratorProviders = new();
        private readonly Dictionary<Type, RecordConstructionModel> _recordConstructionModels = new();
        private readonly Dictionary<Type, ExpressionGeneratorProvider> _typeProviderCache = new();
        private readonly Dictionary<Type, TypeRecord> _typeRecords = new();
        private uint _lastExtensionId = 0;
        
        public void AddGeneratorProvider<T>(
            SerializeGenericDelegate<T> serializerDelegate, 
            DeserializeGenericDelegate<T> deserializerDelegate,
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
                throw new Exception($"No provider interested in type: {type.FullName}");
            return _typeRecords[type] = provider.GenerateTypeRecord(type, this);
        }
        
        public bool IsTypeSerializable(Type type) => 
            _expressionGeneratorProviders.TryGetInterestedProvider(type, this, out _) || 
            TryGenerateConstructionModel(type, out _);

        public bool IsTypeBlittable(Type type) => 
            _expressionGeneratorProviders.TryGetInterestedProvider(type, this, out var provider) && 
            provider is BlittableExpressionGeneratorProvider;
        
        private bool TryGenerateConstructionModel(Type type, out RecordConstructionModel? model)
        {
            // See if this is a type we already have a construction model for
            if (_recordConstructionModels.TryGetValue(type, out model))
                return true;
            
            // Don't try to generate construction models for non record types
            if (!type.IsRecord())
            {
                model = null;
                return false;
            }

            var serializable = new List<PropertyInfo>();
            
            // Go through our properties
            var properties = type.GetProperties();
            foreach (var property in properties)
            {
                if (!property.HasPublicSetAndGet())
                    continue;
                
                // Check if the backing type is serializable
                if (!IsTypeSerializable(property.PropertyType))
                {
                    model = null;
                    return false;
                }
                
                serializable.Add(property);
            }

            // TODO: Figure out if we need to do more constructor checks, maybe if a constructor exists where our
            // properties don't line up. This would happen with inheritance, but inheritance isn't encouraged
            var constructor = type.GetConstructor(Array.Empty<Type>()) ?? 
                type.GetConstructor(serializable.Select(s => s.PropertyType).ToArray());
            _recordConstructionModels[type] = model = new(type, serializable.ToArray(), constructor!);
            return true;
        }

        public RecordConstructionModel GetRecordConstructionModel(Type recordType)
        {
            if (_recordConstructionModels.TryGetValue(recordType, out var model))
                return model;
            if (!TryGenerateConstructionModel(recordType, out model))
                throw new Exception($"Unable to generate record construction model for type: {recordType.Name}");
            return _recordConstructionModels[recordType] = model!;
        }

        public bool TryGetRecordConstructionModel(Type recordType, out RecordConstructionModel? model)
        {
            if (_recordConstructionModels.TryGetValue(recordType, out model))
                return true;
            if (!TryGenerateConstructionModel(recordType, out model))
                return false;
            _recordConstructionModels[recordType] = model!;
            return true;
        }
        
        public IEnumerable<RecordConstructionModel> GetRecordConstructionModels() =>
            _recordConstructionModels.Values;

        public IReadOnlyList<ExpressionGeneratorProvider> GetExpressionGeneratorProviders() =>
            _expressionGeneratorProviders;
    }
}