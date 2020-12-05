using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BinaryRecords.Delegates;
using BinaryRecords.Models;
using BinaryRecords.Providers;

namespace BinaryRecords
{
    public static class RuntimeTypeModel
    {
        private static Dictionary<Type, RecordConstructionModel> _constructionModels = new();

        private static List<ExpressionGeneratorProvider> _generatorProviders = new();
        
        public static IReadOnlyDictionary<Type, RecordConstructionModel> ConstructionModels => _constructionModels;

        static RuntimeTypeModel()
        {
            // Register the builtin generator providers
            foreach (var generatorProviderModel in BuiltinGeneratorProviders.GetDefaultProviders())
                Register(generatorProviderModel);
        }
        
        public static void Register<T>(SerializeGenericDelegate<T> serializer, DeserializeGenericDelegate<T> deserializer)
        {
            var type = typeof(T);
            
            // Check if we have any providers already interested in the type
            if (_generatorProviders.Any(provider => provider.IsInterested(type)))
                throw new Exception($"Failed to add already existing type: {nameof(T)}");

            var serializerDelegate = serializer;
            var deserializerDelegate = deserializer;
            var provider = new ExpressionGeneratorProvider(
                IsInterested: type => type == typeof(T),
                Validate: type => true,
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
        }
        
        public static void Register(ExpressionGeneratorProvider expressionGeneratorProvider)
        {
            if (_generatorProviders.Contains(expressionGeneratorProvider))
                throw new Exception();
            _generatorProviders.Add(expressionGeneratorProvider);
        }

        public static void LoadAssemblyRecordTypes(Assembly assembly)
        {
            var recordTypes = assembly.GetTypes().Where(TypeIsRecord);
            foreach (var type in recordTypes)
                TryGenerateConstructionModel(type, out _);
        }

        public static BinarySerializer CreateSerializer()
        {
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var loadedAssembly in loadedAssemblies)
            {
                LoadAssemblyRecordTypes(loadedAssembly);
            }

            var serializer = new BinarySerializer(_constructionModels, _generatorProviders);
            serializer.GenerateRecordSerializers();
            return serializer;
        }
        
        private static bool TypeIsRecord(Type type)
        {
            // Check if we have an EqualityContract
            var equalityContract = type.GetProperty("EqualityContract", 
                BindingFlags.Instance | BindingFlags.NonPublic);
            
            if (equalityContract is null)
                return false;
            
            // TODO: Check property info like return type, and bindingflags
            return true;
        }

        private static bool TryGenerateConstructionModel(Type type, out RecordConstructionModel model)
        {
            // See if this is a type we already have a construction model for
            if (_constructionModels.TryGetValue(type, out model))
                return true;
            
            // Don't try to generate construction models for non record types
            if (!TypeIsRecord(type))
            {
                model = null;
                return false;
            }
            
            var serializable = new List<PropertyInfo>();
            
            // Go through our properties
            var properties = type.GetProperties();
            foreach (var property in properties)
            {
                if (!IsPropertyContender(property))
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

        private static bool IsPropertyContender(PropertyInfo propertyInfo)
        {
            // TODO: Probably check more info than this.
            var setMethod = propertyInfo.SetMethod;
            return setMethod != null && setMethod.IsPublic;
        }

        public static bool IsTypeSerializable(Type type)
        {
            // See if any providers are interested in the type or if we can make a construction model for it
            var provider = _generatorProviders
                .FirstOrDefault(provider => provider.IsInterested(type));
            return provider != null ? provider.Validate(type) : TryGenerateConstructionModel(type, out _);
        }
    }
}
