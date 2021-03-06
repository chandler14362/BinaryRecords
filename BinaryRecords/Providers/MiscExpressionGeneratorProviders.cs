using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using BinaryRecords.Expressions;
using BinaryRecords.Extensions;
using Krypton.Buffers;

namespace BinaryRecords.Providers
{
    public class MiscExpressionGeneratorProviders
    {
        public static IReadOnlyList<ExpressionGeneratorProvider> Builtin = CreateBuiltinProviders().ToList();

        private static IEnumerable<ExpressionGeneratorProvider> CreateBuiltinProviders()
        {
            // Provider for Nullable<T>
            yield return new(
                Name: "NullableProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, typeLibrary) => 
                    type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) && 
                    typeLibrary.IsTypeSerializable(type.GetGenericArguments()[0]),
                GenerateSerializeExpression: (serializer, type, dataAccess, bufferAccess) =>
                {
                    var blockBuilder = new ExpressionBlockBuilder();
                    var returnLabel = Expression.Label();
                    blockBuilder += Expression.IfThen(
                        Expression.Equal(
                            Expression.Property(dataAccess, "HasValue"),
                            Expression.Constant(false)
                            ),
                        Expression.Block(
                            Expression.Call(
                                bufferAccess, 
                                typeof(SpanBufferWriter).GetMethod("WriteUInt8")!, 
                                Expression.Constant((byte) 0)),
                            Expression.Return(returnLabel)
                        )
                    );
                    blockBuilder += Expression.Call(
                        bufferAccess, 
                        typeof(SpanBufferWriter).GetMethod("WriteUInt8")!,
                        Expression.Constant((byte) 1));
                    blockBuilder += serializer.GenerateTypeSerializer(type.GetGenericArguments()[0],
                        Expression.Property(dataAccess, "Value"), bufferAccess);
                    return blockBuilder += Expression.Label(returnLabel);
                },
                GenerateDeserializeExpression: (serializer, type, bufferAccess) =>
                {
                    var nullableConstructor = type.GetConstructor(type.GetGenericArguments())!;
                    var blockBuilder = new ExpressionBlockBuilder();
                    var returnLabel = Expression.Label(type);
                    blockBuilder += Expression.IfThenElse(
                        Expression.Equal(
                            Expression.Call(bufferAccess, typeof(SpanBufferReader).GetMethod("ReadUInt8")!), 
                            Expression.Constant((byte) 0)),
                        Expression.Return(returnLabel, Expression.Default(type)),
                        Expression.Return(
                            returnLabel,
                            Expression.New(
                                nullableConstructor, 
                                serializer.GenerateTypeDeserializer(type.GetGenericArguments()[0], bufferAccess)
                                )
                        ));
                    return blockBuilder += Expression.Label(returnLabel, Expression.Default(type));
                });
        
            // Provider for enums
            yield return new(
                Name: "EnumProvider",
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
                Name: "TupleProvider",
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
                    var constructor = type.GetConstructor(genericTypes)!;
                    return Expression.New(constructor,
                        genericTypes.Select(t => serializer.GenerateTypeDeserializer(t, bufferAccess)));
                }
            );
            
            // Provider for KeyValuePair<>
            yield return new(
                Name: "KeyValuePairProvider",
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
                    var ctor = type.GetConstructor(generics)!;
                    return Expression.New(ctor,
                        serializer.GenerateTypeDeserializer(keyType, bufferAccess),
                        serializer.GenerateTypeDeserializer(valueType, bufferAccess));
                }
            );

            // DateTime provider
            yield return new(
                Name: "DateTimeProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, _) => type == typeof(DateTime),
                GenerateSerializeExpression: (serializer, type, dataAccess, bufferAccess) =>
                    Expression.Call(
                        typeof(BufferExtensions).GetMethod("WriteDateTime")!,
                        bufferAccess,
                        dataAccess),
                GenerateDeserializeExpression: (serializer, type, bufferAccess) =>
                    Expression.Call(
                        typeof(BufferExtensions).GetMethod("ReadDateTime")!,
                        bufferAccess)
            );

            // DateTimeOffset provider
            yield return new(
                Name: "DateTimeOffsetProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, _) => type == typeof(DateTimeOffset),
                GenerateSerializeExpression: (serializer, type, dataAccess, bufferAccess) => 
                    Expression.Call(bufferAccess, typeof(BufferExtensions).GetMethod("WriteDateTimeOffset")!, 
                        dataAccess),
                GenerateDeserializeExpression: (serializer, type, bufferAccess) => 
                    Expression.Call(bufferAccess, typeof(BufferExtensions).GetMethod("ReadDateTimeOffset")!)
            );

            // TimeSpan provider
            yield return new(
                Name: "TimeSpanProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, _) => type == typeof(TimeSpan),
                GenerateSerializeExpression: (serializer, type, dataAccess, bufferAccess) =>
                    Expression.Call(
                        bufferAccess,
                        typeof(SpanBufferWriter).GetMethod("WriteInt64")!,
                        Expression.Property(dataAccess, "Ticks")
                    ),
                GenerateDeserializeExpression: (serialize, type, bufferAccess) =>
                    Expression.New(
                        typeof(TimeSpan).GetConstructor(new [] {typeof(long)})!,
                        Expression.Call(
                            bufferAccess,
                            typeof(SpanBufferReader).GetMethod("ReadInt64")!
                        )
                    )
            );
        }
    }
}