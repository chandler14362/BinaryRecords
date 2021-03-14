using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using BinaryRecords.Delegates;
using BinaryRecords.Expressions;
using BinaryRecords.Util;
using BinaryRecords.Extensions;
using BinaryRecords.Records;

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
                GenerateSerializeExpression: (typingLibrary, type, buffer, data, versioning) =>
                {
                    var blockBuilder = new ExpressionBlockBuilder();
                    versioning?.Start(blockBuilder, buffer, typingLibrary.BitSize);
                    
                    var endLabel = Expression.Label();
                    blockBuilder += Expression.IfThen(
                        Expression.Equal(
                            Expression.Property(data, "HasValue"),
                            Expression.Constant(false)
                            ),
                        Expression.Block(
                            BufferWriterExpressions.WriteBool(buffer, Expression.Constant(false)),
                            Expression.Goto(endLabel)
                        )
                    );
                    blockBuilder += BufferWriterExpressions.WriteBool(buffer, Expression.Constant(true));
                    blockBuilder += typingLibrary.GenerateSerializeExpression(
                        type.GetGenericArguments()[0], 
                        buffer, 
                        Expression.Property(data, "Value"));
                    blockBuilder += Expression.Label(endLabel);
                    
                    versioning?.Stop(blockBuilder, buffer, typingLibrary.BitSize);
                    return blockBuilder;
                },
                GenerateDeserializeExpression: (typingLibrary, type, buffer) =>
                {
                    var nullableConstructor = type.GetConstructor(type.GetGenericArguments())!;
                    var blockBuilder = new ExpressionBlockBuilder();
                    var returnLabel = Expression.Label(type);
                    blockBuilder += Expression.IfThenElse(
                        Expression.Equal(
                            BufferReaderExpressions.ReadBool(buffer), 
                            Expression.Constant(false)),
                        Expression.Return(returnLabel, Expression.Default(type)),
                        Expression.Return(
                            returnLabel,
                            Expression.New(
                                nullableConstructor, 
                                typingLibrary.GenerateDeserializeExpression(type.GetGenericArguments()[0], buffer)
                                )
                            ));
                    return blockBuilder += Expression.Label(returnLabel, Expression.Default(type));
                },
                GenerateTypeRecord: (typingLibrary, type) => new SequenceTypeRecord(new []
                {
                    typingLibrary.GetTypeRecord(typeof(bool)),
                    typingLibrary.GetTypeRecord(type.GetGenericArguments()[0])
                }));
        
            // Provider for enums
            yield return new(
                Name: "EnumProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, _) => type.BaseType == typeof(Enum),
                GenerateSerializeExpression: (typingLibrary, type, buffer, data, versioning) =>
                {
                    var enumType = type.GetFields()[0].FieldType;
                    var blockBuilder = new ExpressionBlockBuilder();
                    versioning?.Start(blockBuilder, buffer, typingLibrary.BitSize);
                    blockBuilder += typingLibrary.GenerateSerializeExpression(
                        enumType, 
                        buffer, 
                        Expression.Convert(data, enumType));
                    versioning?.Stop(blockBuilder, buffer, typingLibrary.BitSize);
                    return blockBuilder;
                },
                GenerateDeserializeExpression: (typingLibrary, type, buffer) => 
                    Expression.Convert(
                        typingLibrary.GenerateDeserializeExpression(type.GetFields()[0].FieldType, buffer), 
                        type),
                GenerateTypeRecord: (typingLibrary, type) => 
                    typingLibrary.GetTypeRecord(type.GetFields()[0].FieldType));
            
            // Provider for tuples
            yield return new(
                Name: "TupleProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, library) => type.IsTuple() && type.GetGenericArguments().All(library.IsTypeSerializable),
                GenerateSerializeExpression: (typingLibrary, type, buffer, data, versioning) =>
                {
                    var blockBuilder = new ExpressionBlockBuilder();
                    versioning?.Start(blockBuilder, buffer, typingLibrary.BitSize);
                    blockBuilder += type.GetGenericArguments().Select((genericType, i) =>
                        typingLibrary.GenerateSerializeExpression(genericType, buffer,
                            Expression.PropertyOrField(data, $"Item{i + 1}")));
                    versioning?.Stop(blockBuilder, buffer, typingLibrary.BitSize);
                    return blockBuilder;
                },
                GenerateDeserializeExpression: (typingLibrary, type, buffer) =>
                {
                    var genericTypes = type.GetGenericArguments();
                    var constructor = type.GetConstructor(genericTypes)!;
                    return Expression.New(
                        constructor,
                        genericTypes.Select(t => typingLibrary.GenerateDeserializeExpression(t, buffer)));
                },
                GenerateTypeRecord: (typingLibrary, type) =>
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
                GenerateSerializeExpression: (typingLibrary, type, buffer, data, versioning) =>
                {
                    var generics = type.GetGenericArguments();
                    var keyType = generics[0];
                    var valueType = generics[1];
                    
                    var blockBuilder = new ExpressionBlockBuilder();
                    versioning?.Start(blockBuilder, buffer, typingLibrary.BitSize);
                    blockBuilder += typingLibrary.GenerateSerializeExpression(
                        keyType, 
                        buffer, 
                        Expression.PropertyOrField(data, "Key"));
                    blockBuilder += typingLibrary.GenerateSerializeExpression(
                        valueType, 
                        buffer, 
                        Expression.PropertyOrField(data, "Value"));
                    versioning?.Stop(blockBuilder, buffer, typingLibrary.BitSize);
                    return blockBuilder;
                },
                GenerateDeserializeExpression: (typingLibrary, type, buffer) =>
                {
                    var generics = type.GetGenericArguments();
                    var keyType = generics[0];
                    var valueType = generics[1];
                    var ctor = type.GetConstructor(generics)!;
                    return Expression.New(ctor,
                        typingLibrary.GenerateDeserializeExpression(keyType, buffer),
                        typingLibrary.GenerateDeserializeExpression(valueType, buffer));
                },
                GenerateTypeRecord: (typingLibrary, type) =>
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
                GenerateSerializeExpression: (typingLibrary, type, buffer, data, versioning) =>
                {
                    var blockBuilder = new ExpressionBlockBuilder();
                    versioning?.Start(blockBuilder, buffer, typingLibrary.BitSize);
                    blockBuilder += BufferWriterExpressions.WriteInt64(
                        buffer,
                        Expression.Property(data, "Ticks"));
                    versioning?.Stop(blockBuilder, buffer, typingLibrary.BitSize);
                    return blockBuilder;
                },
                GenerateDeserializeExpression: (typingLibrary, type, buffer) =>
                    Expression.New(
                        typeof(TimeSpan).GetConstructor(new [] {typeof(long)})!,
                        BufferReaderExpressions.ReadInt64(buffer)
                    ),
                GenerateTypeRecord: (typingLibrary, type) =>
                {
                    return new SequenceTypeRecord(new []{typingLibrary.GetTypeRecord(typeof(long))});
                }
            );
        }
    }
}