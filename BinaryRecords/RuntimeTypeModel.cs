using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace BinaryRecords
{
    public record RecordConstructionModel(Type type, PropertyInfo[] Properties, ConstructorInfo? Constructor);
    
    public static class RuntimeTypeModel
    {
        private static Dictionary<Type, (Delegate serialize, Delegate deserialize)> _registeredTypes = new();

        private static Dictionary<Type, RecordConstructionModel> _constructionModels = new();

        public static IReadOnlyDictionary<Type, RecordConstructionModel> ConstructionModels => _constructionModels;
        
        public static void Register<T>(SerializationConstants.SerializeDelegate<T> serializer, SerializationConstants.DeserializeDelegate<T> deserializer)
        {
            var type = typeof(T);
            if (SerializationConstants.TryGetPrimitiveSerializationPair(type, out _) ||
                !_registeredTypes.TryAdd(type, (serializer, deserializer)))
            {
                throw new Exception($"Failed to add already existing type: {nameof(T)}");
            }
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

            var serializer = new BinarySerializer(_constructionModels, _registeredTypes);
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

        private static bool IsTypeSerializable(Type type)
        {
            // Now get the underlying type and see if we can serialize it
            if (SerializationConstants.TryGetPrimitiveSerializationPair(type, out var primitivePair) ||
                _registeredTypes.ContainsKey(type) ||
                _constructionModels.ContainsKey(type))
                return true;

            RecordConstructionModel constructionModel;
            
            // Check if we are dealing with a serializable collection
            if (type.ImplementsGenericInterface(typeof(ICollection<>)))
            {
                // Try to generate a construction model for each generic arg
                var genericArgs = type.GetGenericArguments();
                return genericArgs.Length != 0 && genericArgs.All(IsTypeSerializable);
            }
            
            // Check if we are dealing with a tuple type
            if (type.GetInterface(nameof(System.Runtime.CompilerServices.ITuple)) != null)
            {
                // Try to generate a construction model for each generic arg
                var genericArgs = type.GetGenericArguments();
                return genericArgs.Length != 0 && genericArgs.All(IsTypeSerializable);
            }
            
            // Try to generate a construction model for the type
            if (TryGenerateConstructionModel(type, out constructionModel))
            {
                _constructionModels[type] = constructionModel;
                return true;
            }
            
            return false;
        }
    }
}
