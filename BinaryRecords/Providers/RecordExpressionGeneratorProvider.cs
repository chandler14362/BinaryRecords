using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BinaryRecords.Abstractions;
using BinaryRecords.Attributes;
using BinaryRecords.Extensions;
using BinaryRecords.Records;

namespace BinaryRecords.Providers
{
    public static class RecordExpressionGeneratorProvider
    {
        private static readonly Dictionary<Type, RecordConstructionRecord> _recordConstructionRecords = new();

        public static IReadOnlyList<ExpressionGeneratorProvider> Builtin = new[] {CreateBuiltinProvider()};
        
        private record RecordConstructionRecord(
            Type Type, 
            IReadOnlyList<(uint Key, PropertyInfo)> Properties,
            ConstructorInfo ConstructorInfo,
            bool Versioned,
            ConstructableTypeRecord ConstructableTypeRecord)
        {
            public bool UsesMemberInit => ConstructorInfo.GetParameters().Length == 0;
        }
        
        private static bool IsInterested(Type type, ITypingLibrary typingLibrary)
        {
            if (!type.IsRecord())
                return false;
            // Make sure all our properties are serializable
            var properties = type.GetProperties();
            foreach (var property in properties)
            {
                if (!property.HasPublicSetAndGet())
                    continue;
                if (!typingLibrary.IsTypeSerializable(property.PropertyType))
                    return false;
            }
            return true;
        }

        private static ConstructableTypeRecord GenerateConstructableTypeRecord(
            Type type, 
            ITypingLibrary typingLibrary)
        {
            bool? versioned = default;
            var heldKeys = new HashSet<uint>();
            var recordProperties = new List<(uint, PropertyInfo)>();
            var typeRecords = new List<(uint, TypeRecord)>();
            
            // We need to check if we use memberinit before hand so we know where to check for keys
            var constructor = type.GetConstructors()[0];
            
            // TODO: Make the memberinit stuff work with inheritance, right now I don't think this current code will do the trick
            var usesMemberInit = constructor.GetParameters().Length == 0;
            var parameterIndex = 0;
            
            var properties = type.GetProperties();
            foreach (var property in properties)
            {
                if (!property.HasPublicSetAndGet())
                    continue;
                
                uint keyId = 0;
                
                // We check for key differently depending if we use memberinit
                KeyAttribute[] keyAttributes;
                if (usesMemberInit)
                {
                    keyAttributes = property.GetCustomAttributes<KeyAttribute>().ToArray();
                }
                else
                {
                    var constructorParameter = constructor.GetParameters()[parameterIndex];
                    keyAttributes = constructorParameter.GetCustomAttributes<KeyAttribute>().ToArray();
                }
                
                if (keyAttributes.Length == 0)
                {
                    if (versioned != null && versioned != false)
                        throw new Exception($"Inconsistent type versioning on record: {type.FullName}");
                    versioned = false;
                    recordProperties.Add((0, property));
                }
                else if (keyAttributes.Length == 1)
                {
                    if (versioned != null && versioned != true)
                        throw new Exception($"Inconsistent type versioning on record: {type.FullName}");
                    versioned = true;
                    keyId = keyAttributes[0].Index;
                    if (!heldKeys.Add(keyId))
                        throw new Exception($"Duplicate key value used on property: {property.PropertyType.FullName}, key: {keyId}");
                    recordProperties.Add((keyId, property));
                }
                else
                {
                    throw new Exception($"More than one key attribute on property: {property.PropertyType.FullName}");
                }

                var propertyTypeRecord = typingLibrary.GetTypeRecord(property.PropertyType);
                typeRecords.Add((keyId, propertyTypeRecord));

                parameterIndex++;
            }
            
            Debug.Assert(usesMemberInit || typeRecords.Count == constructor.GetParameters().Length);
            
            var constructableTypeRecord = new ConstructableTypeRecord(type, typeRecords);
            
            // TODO: Maybe try to phase out the RecordConstructionRecord, might not even be needed
            _recordConstructionRecords[type] = new(type, recordProperties, constructor!, versioned ?? false, constructableTypeRecord);
            return constructableTypeRecord;
        }
        
        private static Expression GenerateSerializeExpression(
            TypeSerializer serializer, 
            Type type, 
            Expression dataAccess, 
            Expression bufferAccess)
        {
            throw new NotImplementedException();
        }

        private static Expression GenerateDeserializeExpression(
            TypeSerializer serializer,
            Type type,
            Expression bufferAccess)
        {
            throw new NotImplementedException();
        }
        
        private static ExpressionGeneratorProvider CreateBuiltinProvider()
        {
            return new(
                Name: "RecordProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: IsInterested,
                GenerateSerializeExpression: GenerateSerializeExpression,
                GenerateDeserializeExpression: GenerateDeserializeExpression,
                GenerateTypeRecord: GenerateConstructableTypeRecord
            );
        }
    }
}