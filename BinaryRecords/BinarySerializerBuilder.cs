using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BinaryRecords.Delegates;
using BinaryRecords.Extensions;
using BinaryRecords.Interfaces;
using BinaryRecords.Models;
using BinaryRecords.Providers;

namespace BinaryRecords
{
    public class BinarySerializerBuilder : ITypeLibrary
    {
        public class Config
        {
            public bool LoadAllLoadedAssemblies { get; set; } = true;
        }

        private Dictionary<Type, RecordConstructionModel> _constructionModels = new();

        private List<ExpressionGeneratorProvider> _generatorProviders = new();

        private HashSet<Assembly> _assembliesToLoad = new();
        
        private Config _config = new();

        public BinarySerializerBuilder()
        {
            // Builtin generator providers
            var generatorProviders = PrimitiveExpressionGeneratorProviders.Builtin
                .Concat(CollectionExpressionGeneratorProviders.Builtin)
                .Concat(MiscExpressionGeneratorProviders.Builtin);
            foreach (var generatorProvider in generatorProviders) AddProvider(generatorProvider);
        }

        public BinarySerializerBuilder WithConfiguration(Action<Config> doConfiguration)
        {
            doConfiguration(_config);
            return this;
        }
        
        public BinarySerializerBuilder AddProvider<T>(SerializeGenericDelegate<T> serializer, DeserializeGenericDelegate<T> deserializer,
            ProviderPriority priority=ProviderPriority.High)
        {
            var serializerDelegate = serializer;
            var deserializerDelegate = deserializer;
            var provider = new ExpressionGeneratorProvider(
                Priority: priority,
                IsInterested: (type, _) => type == typeof(T),
                GenerateSerializeExpression: (serializer, type, dataAccess, bufferAccess) 
                    => Expression.Invoke(
                        Expression.Constant(serializerDelegate), 
                        bufferAccess, 
                        dataAccess),
                GenerateDeserializeExpression: (serializer, type, bufferAccess) 
                    => Expression.Invoke(
                        Expression.Constant(deserializerDelegate), 
                        bufferAccess));
            _generatorProviders.Add(provider);
            return this;
        }
        
        public BinarySerializerBuilder AddProvider(ExpressionGeneratorProvider expressionGeneratorProvider)
        {
            if (_generatorProviders.Contains(expressionGeneratorProvider))
                throw new Exception();
            _generatorProviders.Add(expressionGeneratorProvider);
            return this;
        }

        public BinarySerializerBuilder AddAssembly(Assembly assembly)
        {
            _assembliesToLoad.Add(assembly);
            return this;
        }

        private bool TryGenerateConstructionModel(Type type, out RecordConstructionModel model)
        {
            // See if this is a type we already have a construction model for
            if (_constructionModels.TryGetValue(type, out model))
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
            model = new(type, serializable.ToArray(), 
                type.GetConstructor(serializable.Select(s => s.PropertyType).ToArray()));
            _constructionModels[type] = model;
            return true;
        }

        /// <summary>
        /// Builds a new BinarySerializer from the current builder.
        /// </summary>
        /// <returns></returns>
        public BinarySerializer Build()
        {
            if (_config.LoadAllLoadedAssemblies)
            {
                var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var loadedAssembly in loadedAssemblies) _assembliesToLoad.Add(loadedAssembly);
            }          

            // Try generating a construction model for each record type in every assembly
            foreach (var assembly in _assembliesToLoad)
            {
                var recordTypes = assembly.GetTypes().Where(type => type.IsRecord());
                foreach (var type in recordTypes) TryGenerateConstructionModel(type, out _);
            }

            var serializer = new BinarySerializer(_constructionModels, _generatorProviders);
            serializer.GenerateRecordSerializers();
            return serializer;
        }
        
        /// <summary>
        /// Builds a new BinarySerializer with the builtin generator providers and default config
        /// </summary>
        /// <returns></returns>
        public static BinarySerializer BuildDefault()
        {
            return new BinarySerializerBuilder().Build();
        }

        public bool IsTypeSerializable(Type type)
        {
            // See if any providers are interested in the type or if we can make a construction model for it
            var provider = _generatorProviders.GetInterestedProvider(type, this);
            return provider != null || TryGenerateConstructionModel(type, out _);
        }

        public IList<Type> GetConstructableTypes()
        {
            return _constructionModels.Keys.ToList();
        }
    }
}
