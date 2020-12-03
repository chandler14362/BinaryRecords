using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BinaryRecords.Delegates;
using BinaryRecords.Providers;

namespace BinaryRecords
{
    public record RecordConstructionModel(Type type, PropertyInfo[] Properties, ConstructorInfo? Constructor);
    
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
            if (_generatorProviders.Any(model => model.IsInterested(type)))
                throw new Exception($"Failed to add already existing type: {nameof(T)}");

            var serializerDelegate = serializer;
            var deserializerDelegate = deserializer;
            var provider = new ExpressionGeneratorProvider(
                IsInterested: type => type == typeof(T),
                Validate: type => true,
                GenerateSerializeExpression: (serializer, type, dataAccess, stackFrame) =>
                {
                    var callable = Expression.Constant(serializerDelegate.Target);
                    return Expression.Call(callable, serializerDelegate.Method, stackFrame.GetParameter("buffer"),
                        dataAccess);
                },
                GenerateDeserializeExpression: (serializer, type, stackFrame) =>
                {
                    var callable = Expression.Constant(deserializerDelegate.Target);
                    return Expression.Call(callable, deserializerDelegate.Method, stackFrame.GetParameter("buffer"));
                }
            );
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
            // Get every record type 
            var recordTypes = assembly.GetTypes().Where(TypeIsRecord);
            foreach (var type in recordTypes)
            {
                // Try generating a construction model
                if (_constructionModels.ContainsKey(type) 
                    || !TryGenerateConstructionModel(type, out var constructionModel))
                    continue;

                _constructionModels[type] = constructionModel;
            }
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
                    Debug.WriteLine($"Can't serialize property {property.PropertyType} {property.Name}");
                    model = null;
                    return false;
                }
                
                serializable.Add(property);
            }

            // TODO: Figure out if we need to do more constructor checks, maybe if a constructor exists where our
            // properties don't line up. This would happen with inheritance, but inheritance isn't encouraged
            model = new(type, serializable.ToArray(), 
                type.GetConstructor(serializable.Select(s => s.PropertyType).ToArray()));
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
            // See if any providers want to handle the type
            var provider = _generatorProviders.FirstOrDefault(model => model.IsInterested(type));
            if (provider != null)
                return provider.Validate(type);

            // Now get the underlying type and see if we can serialize it
            if (_constructionModels.ContainsKey(type))
                return true;
            
            // Try to generate a construction model for the type
            if (TryGenerateConstructionModel(type, out var constructionModel))
            {
                _constructionModels[type] = constructionModel;
                return true;
            }
            
            return false;
        }
    }
}
