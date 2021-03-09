using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using BinaryRecords.Abstractions;
using BinaryRecords.Util;
using BinaryRecords.Extensions;
using BinaryRecords.Records;
using Krypton.Buffers;

namespace BinaryRecords.Providers
{
    public static class MiscExpressionGeneratorProviders
    {
        public static readonly IReadOnlyList<ExpressionGeneratorProvider> Builtin = CreateBuiltinProviders().ToList();

        private static IEnumerable<ExpressionGeneratorProvider> CreateBuiltinProviders()
        {
            // Provider for Nullable<T>
            yield return new(
                Name: "NullableProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, typeLibrary) => 
                    type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) && 
                    typeLibrary.IsTypeSerializable(type.GetGenericArguments()[0]),
                GenerateSerializeExpression: (typingLibrary, type, dataAccess, bufferAccess, autoVersioning) =>
                {
                    var blockBuilder = new ExpressionBlockBuilder();
                    autoVersioning?.StartVersioning(blockBuilder, bufferAccess);
                    
                    var endLabel = Expression.Label();
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
                            Expression.Goto(endLabel)
                        )
                    );
                    blockBuilder += Expression.Call(
                        bufferAccess, 
                        typeof(SpanBufferWriter).GetMethod("WriteUInt8")!,
                        Expression.Constant((byte) 1));
                    blockBuilder += typingLibrary.GenerateSerializeExpression(type.GetGenericArguments()[0],
                        Expression.Property(dataAccess, "Value"), bufferAccess, null);
                    blockBuilder += Expression.Label(endLabel);
                    
                    autoVersioning?.EndVersioning(blockBuilder, bufferAccess);
                    return blockBuilder;
                },
                GenerateDeserializeExpression: (typingLibrary, type, bufferAccess) =>
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
                                typingLibrary.GenerateDeserializeExpression(type.GetGenericArguments()[0], bufferAccess)
                                )
                        ));
                    return blockBuilder += Expression.Label(returnLabel, Expression.Default(type));
                },
                GenerateTypeRecord: (type, typingLibrary) => new SequenceTypeRecord(new []
                {
                    typingLibrary.GetTypeRecord(typeof(bool)),
                    typingLibrary.GetTypeRecord(type.GetGenericArguments()[0])
                }));
        
            // Provider for enums
            yield return new(
                Name: "EnumProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, _) => type.BaseType == typeof(Enum),
                GenerateSerializeExpression: (typingLibrary, type, dataAccess, bufferAccess, autoVersioning) =>
                {
                    var enumType = type.GetFields()[0].FieldType;
                    var blockBuilder = new ExpressionBlockBuilder();
                    autoVersioning?.StartVersioning(blockBuilder, bufferAccess);
                    blockBuilder += typingLibrary.GenerateSerializeExpression(
                        enumType,
                        Expression.Convert(dataAccess, enumType), 
                        bufferAccess, 
                        null);
                    autoVersioning?.EndVersioning(blockBuilder, bufferAccess);
                    return blockBuilder;
                },
                GenerateDeserializeExpression: (typingLibrary, type, bufferAccess) => 
                    Expression.Convert(
                        typingLibrary.GenerateDeserializeExpression(type.GetFields()[0].FieldType, bufferAccess), 
                        type),
                GenerateTypeRecord: (type, typingLibrary) => 
                    typingLibrary.GetTypeRecord(type.GetFields()[0].FieldType));
            
            // Provider for tuples
            yield return new(
                Name: "TupleProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, library) => type.IsTuple() && type.GetGenericArguments().All(library.IsTypeSerializable),
                GenerateSerializeExpression: (typingLibrary, type, dataAccess, bufferAccess, autoVersioning) =>
                {
                    var blockBuilder = new ExpressionBlockBuilder();
                    autoVersioning?.StartVersioning(blockBuilder, bufferAccess);
                    blockBuilder += type.GetGenericArguments().Select((genericType, i) => 
                        typingLibrary.GenerateSerializeExpression(
                            genericType, 
                            Expression.PropertyOrField(dataAccess, $"Item{i + 1}"), 
                            bufferAccess, 
                            null)
                        );
                    autoVersioning?.EndVersioning(blockBuilder, bufferAccess);
                    return blockBuilder;
                },
                GenerateDeserializeExpression: (typingLibrary, type, bufferAccess) =>
                {
                    var genericTypes = type.GetGenericArguments();
                    var constructor = type.GetConstructor(genericTypes)!;
                    return Expression.New(constructor,
                        genericTypes.Select(t => typingLibrary.GenerateDeserializeExpression(t, bufferAccess)));
                },
                GenerateTypeRecord: (type, typingLibrary) =>
                {
                    var genericTypes = type.GetGenericArguments();
                    var memberTypeRecords = genericTypes.Select(typingLibrary.GetTypeRecord).ToArray();
                    return new SequenceTypeRecord(memberTypeRecords);
                }
            );
            
            // Provider for KeyValuePair<>
            yield return new(
                Name: "KeyValuePairProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, library) => type.IsGenericType(typeof(KeyValuePair<,>)) && 
                                                 type.GetGenericArguments().All(library.IsTypeSerializable),
                GenerateSerializeExpression: (serializer, type, dataAccess, bufferAccess, autoVersioning) =>
                {
                    var generics = type.GetGenericArguments();
                    var keyType = generics[0];
                    var valueType = generics[1];
                    
                    var blockBuilder = new ExpressionBlockBuilder();
                    autoVersioning?.StartVersioning(blockBuilder, bufferAccess);

                    blockBuilder += serializer.GenerateSerializeExpression(
                        keyType,
                        Expression.PropertyOrField(dataAccess, "Key"),
                        bufferAccess, 
                        null);
                    blockBuilder += serializer.GenerateSerializeExpression(
                        valueType,
                        Expression.PropertyOrField(dataAccess, "Value"),
                        bufferAccess, 
                        null);
                    
                    autoVersioning?.EndVersioning(blockBuilder, bufferAccess);
                    return blockBuilder;
                },
                GenerateDeserializeExpression: (serializer, type, bufferAccess) =>
                {
                    var generics = type.GetGenericArguments();
                    var keyType = generics[0];
                    var valueType = generics[1];
                    var ctor = type.GetConstructor(generics)!;
                    return Expression.New(ctor,
                        serializer.GenerateDeserializeExpression(keyType, bufferAccess),
                        serializer.GenerateDeserializeExpression(valueType, bufferAccess));
                },
                GenerateTypeRecord: (type, typingLibrary) =>
                {
                    var memberTypes = type.GetGenericArguments().Select(typingLibrary.GetTypeRecord).ToArray();
                    return new SequenceTypeRecord(memberTypes);
                }
            );

            /* TODO: Rewrite these to use Expression or maybe implement some sort of dynamic struct provider
                     These are types we might want to manually implement anyways
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
            */

            // TimeSpan provider
            yield return new(
                Name: "TimeSpanProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, _) => type == typeof(TimeSpan),
                GenerateSerializeExpression: (typingLibrary, type, dataAccess, bufferAccess, autoVersioning) =>
                {
                    var blockBuilder = new ExpressionBlockBuilder();
                    autoVersioning?.StartVersioning(blockBuilder, bufferAccess);
                    blockBuilder += Expression.Call(
                        bufferAccess,
                        typeof(SpanBufferWriter).GetMethod("WriteInt64")!,
                        Expression.Property(dataAccess, "Ticks")
                    );
                    autoVersioning?.EndVersioning(blockBuilder, bufferAccess);
                    return blockBuilder;
                },
                GenerateDeserializeExpression: (typingLibrary, type, bufferAccess) =>
                    Expression.New(
                        typeof(TimeSpan).GetConstructor(new [] {typeof(long)})!,
                        Expression.Call(
                            bufferAccess,
                            typeof(SpanBufferReader).GetMethod("ReadInt64")!
                        )
                    ),
                GenerateTypeRecord: (type, typingLibrary) =>
                {
                    return new SequenceTypeRecord(new []{typingLibrary.GetTypeRecord(typeof(long))});
                }
            );
        }
    }
}