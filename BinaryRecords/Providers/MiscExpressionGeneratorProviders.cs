using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using BinaryRecords.Extensions;

namespace BinaryRecords.Providers
{
    public class MiscExpressionGeneratorProviders
    {
        public static IReadOnlyList<ExpressionGeneratorProvider> Builtin = CreateBuiltinProviders().ToList();

        private static IEnumerable<ExpressionGeneratorProvider> CreateBuiltinProviders()
        {
            // Provider for enums
            yield return new(
                Priority: ProviderPriority.Normal,
                IsInterested: (type, _) => type.BaseType == typeof(Enum),
                GenerateSerializeExpression: (serializer, type, dataAccess, bufferAccess) =>
                {
                    var enumType = type.GetFields()[0].FieldType;
                    return serializer.GenerateTypeSerializer(enumType, 
                        Expression.Convert(dataAccess, enumType), 
                        bufferAccess);
                },
                GenerateDeserializeExpression: (serializer, type, bufferAccess) =>
                {
                    var enumType = type.GetFields()[0].FieldType;
                    return Expression.Convert(serializer.GenerateTypeDeserializer(enumType, bufferAccess), type);
                }
            );
            
            // Provider for tuples
            yield return new(
                Priority: ProviderPriority.Normal,
                IsInterested: (type, library) => type.IsTuple() && type.GetGenericArguments().All(library.IsTypeSerializable),
                GenerateSerializeExpression: (serializer, type, dataAccess, bufferAccess) =>
                {
                    var expressions = type.GetGenericArguments().Select((genericType, i) =>
                        {
                            var access = Expression.PropertyOrField(dataAccess, $"Item{i + 1}");
                            return serializer.GenerateTypeSerializer(genericType, access, bufferAccess);
                        });
                    return Expression.Block(expressions);
                },
                GenerateDeserializeExpression: (serializer, type, bufferAccess) =>
                {
                    var genericTypes = type.GetGenericArguments();
                    var constructor = type.GetConstructor(genericTypes);
                    return Expression.New(constructor,
                        genericTypes.Select(t => serializer.GenerateTypeDeserializer(t, bufferAccess)));
                }
            );
            
            // Provider for KeyValuePair<>
            yield return new(
                Priority: ProviderPriority.Normal,
                IsInterested: (type, library) => type.IsGenericType(typeof(KeyValuePair<,>)) && 
                                                 type.GetGenericArguments().All(library.IsTypeSerializable),
                GenerateSerializeExpression: (serializer, type, dataAccess, bufferAccess) =>
                {
                    var generics = type.GetGenericArguments();
                    var keyType = generics[0];
                    var valueType = generics[1];
                    return Expression.Block(
                        serializer.GenerateTypeSerializer(keyType, 
                            Expression.PropertyOrField(dataAccess, "Key"), 
                            bufferAccess),
                        serializer.GenerateTypeSerializer(valueType, 
                            Expression.PropertyOrField(dataAccess, "Value"), 
                            bufferAccess)
                    );
                },
                GenerateDeserializeExpression: (serializer, type, bufferAccess) =>
                {
                    var generics = type.GetGenericArguments();
                    var keyType = generics[0];
                    var valueType = generics[1];
                    var ctor = type.GetConstructor(generics);
                    return Expression.New(ctor,
                        serializer.GenerateTypeDeserializer(keyType, bufferAccess),
                        serializer.GenerateTypeDeserializer(valueType, bufferAccess));
                }
            );
            
            // DateTimeOffset provider
            yield return new(
                Priority: ProviderPriority.Normal,
                IsInterested: (type, _) => type == typeof(DateTimeOffset),
                GenerateSerializeExpression: (serializer, type, dataAccess, bufferAccess) => 
                    Expression.Call(bufferAccess, typeof(BufferExtensions).GetMethod("WriteDateTimeOffset"), 
                        dataAccess),
                GenerateDeserializeExpression: (serializer, type, bufferAccess) => 
                    Expression.Call(bufferAccess, typeof(BufferExtensions).GetMethod("ReadDateTimeOffset"))
            );
        }
    }
}