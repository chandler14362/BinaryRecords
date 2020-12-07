using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using BinaryRecords.Delegates;
using BinaryRecords.Expressions;
using BinaryRecords.Extensions;
using Krypton.Buffers;

namespace BinaryRecords.Providers
{
    public static class CollectionExpressionGeneratorProviders
    {
        public static IReadOnlyList<ExpressionGeneratorProvider> Builtin = CreateBuiltinProviders().ToList();

        public static unsafe void WriteBlittableArray<T>(ref SpanBufferWriter buffer, T[] values) where T : unmanaged
        {
            var size = sizeof(T) * values.Length;
            var bookmark = buffer.ReserveBookmark(size);
            buffer.WriteBookmark(bookmark, values, (buffer, values) =>
            {
                var casted = MemoryMarshal.Cast<T, byte>(values.AsSpan());
                casted.CopyTo(buffer);
            });
        }

        public static unsafe void ReadBlittableArray<T>(ref SpanBufferReader buffer, T[] outValues) where T : unmanaged
        {
            var size = sizeof(T) * outValues.Length;
            var values = buffer.ReadBytes(size);
            var casted = MemoryMarshal.Cast<byte, T>(values);
            casted.CopyTo(outValues);
        }
        
        public static Expression GenerateSerializeEnumerable(BinarySerializer serializer, Type type, 
            Expression dataAccess, Expression bufferAccess)
        {
            var enumerableInterface = 
                type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>) 
                    ? type
                    : type.GetGenericInterface(typeof(IEnumerable<>));
            var generics = enumerableInterface.GetGenericArguments();
            var genericType = generics[0];
            
            var enumerableType = typeof(IEnumerable<>).MakeGenericType(generics);
            var enumeratorType = typeof(IEnumerator<>).MakeGenericType(generics);

            var blockBuilder = new ExpressionBlockBuilder();
            var enumerable = blockBuilder.CreateVariable(enumerableType);
            var enumerator = blockBuilder.CreateVariable(enumeratorType);
            
            blockBuilder += Expression.Assign(enumerable, 
                Expression.Convert(dataAccess, enumerableType));
            blockBuilder += Expression.Assign(enumerator,
                Expression.Call(enumerable, enumerableType.GetMethod("GetEnumerator")));
            
            var countBookmark = blockBuilder.CreateVariable<SpanBufferWriter.Bookmark>();
            blockBuilder += Expression.Assign(countBookmark, 
                Expression.Call(bufferAccess, 
                    typeof(SpanBufferWriter).GetMethod("ReserveBookmark"), 
                    Expression.Constant(sizeof(ushort))));

            var written = blockBuilder.CreateVariable<ushort>();
            
            var loopExit = Expression.Label();
            blockBuilder += Expression.Loop(
                Expression.IfThenElse(
                    Expression.Call(enumerator, typeof(IEnumerator).GetMethod("MoveNext")),
                    Expression.Block(new []
                    {
                        serializer.GenerateTypeSerializer(genericType, 
                            Expression.PropertyOrField(enumerator, "Current"), 
                            bufferAccess),
                        Expression.PostIncrementAssign(written)
                    }),
                    Expression.Break(loopExit))
            );
            blockBuilder += Expression.Label(loopExit);

            var writeBookmarkMethod = typeof(BufferExtensions).GetMethod("WriteUInt16Bookmark");
            return blockBuilder += Expression.Call(
                writeBookmarkMethod,
                bufferAccess,
                countBookmark,
                written
            );
        }

        public static Expression GenerateDeserializeEnumerable(BinarySerializer serializer, Type type, 
            Expression bufferAccess, Type genericBackingType, GenerateAddElementExpressionDelegate generateAddElement)
        {
            var enumerableInterface = 
                type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>) 
                    ? type 
                    : type.GetGenericInterface(typeof(IEnumerable<>));
            var genericTypes = enumerableInterface.GetGenericArguments();
            var genericType = genericTypes[0];
        
            var constructingCollectionType = genericBackingType.MakeGenericType(type.GetGenericArguments());
            
            var collectionConstructor = constructingCollectionType.GetConstructor(new[] {typeof(int)});
            if (collectionConstructor == null)
                collectionConstructor = constructingCollectionType.GetConstructor(Array.Empty<Type>());
            
            var blockBuilder = new ExpressionBlockBuilder();
            var deserialized = blockBuilder.CreateVariable(constructingCollectionType);

            // Read the element count
            var elementCount = blockBuilder.CreateVariable<int>();
            blockBuilder += Expression.Assign(elementCount, 
                Expression.Convert(Expression.Call(bufferAccess, 
                    typeof(SpanBufferReader).GetMethod("ReadUInt16")), typeof(int)));

            // now deserialize each element
            blockBuilder += Expression.Assign(deserialized, 
                collectionConstructor.GetParameters().Length == 1 
                    ? Expression.New(collectionConstructor, elementCount) 
                    : Expression.New(collectionConstructor));

            var exitLabel = Expression.Label(constructingCollectionType);
            var counter = blockBuilder.CreateVariable<int>();
            blockBuilder += Expression.Assign(counter, Expression.Constant(0));
            blockBuilder += Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(counter, elementCount),
                    Expression.Block(new []
                    {
                        generateAddElement(deserialized, genericType, 
                            () => serializer.GenerateTypeDeserializer(genericType, bufferAccess)),
                        Expression.PostIncrementAssign(counter)
                    }),
                    Expression.Break(exitLabel, deserialized))
            );
            return blockBuilder += Expression.Label(exitLabel, 
                Expression.Constant(null, constructingCollectionType));
        }

        public static Expression GenerateSlowSerializeArray(BinarySerializer serializer, Type type, 
            Expression dataAccess, Expression bufferAccess)
        {
            var genericType = type.GetGenericInterface(typeof(IEnumerable<>)).GetGenericArguments()[0];
            
            var blockBuilder = new ExpressionBlockBuilder();
            
            var arrayLength = Expression.PropertyOrField(dataAccess, "Length");
            blockBuilder += Expression.Call(
                bufferAccess,
                typeof(SpanBufferWriter).GetMethod("WriteUInt16"), 
                Expression.Convert(arrayLength, typeof(ushort)));
            
            var exitLabel = Expression.Label();
            var counter = blockBuilder.CreateVariable<int>();
            blockBuilder += Expression.Assign(counter, Expression.Constant(0));
            blockBuilder += Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(counter, arrayLength), 
                    Expression.Block(
                        serializer.GenerateTypeSerializer(genericType, Expression.ArrayAccess(dataAccess, counter), bufferAccess),
                        Expression.PostIncrementAssign(counter)
                    ),
                    Expression.Break(exitLabel))
            );
            return blockBuilder += Expression.Label(exitLabel);    
        }

        public static Expression GenerateFastSerializeArray(BinarySerializer serializer, Type type,
            Expression dataAccess, Expression bufferAccess)
        {
            var genericType = type.GetGenericInterface(typeof(IEnumerable<>)).GetGenericArguments()[0];
            var blockBuilder = new ExpressionBlockBuilder();
            
            // Write the element count
            var arrayLength = Expression.PropertyOrField(dataAccess, "Length");
            blockBuilder += Expression.Call(
                bufferAccess,
                typeof(SpanBufferWriter).GetMethod("WriteUInt16"), 
                Expression.Convert(arrayLength, typeof(ushort)));
                        
            var writeArrayMethod = typeof(CollectionExpressionGeneratorProviders)
                .GetMethod("WriteBlittableArray").MakeGenericMethod(genericType);
            return blockBuilder += Expression.Call(writeArrayMethod, bufferAccess, dataAccess);
        }

        public static Expression GenerateSlowDeserializeArray(BinarySerializer serializer, Type type,
            Expression arrayAccess, Expression bufferAccess)
        {
            var genericType = type.GetGenericInterface(typeof(IEnumerable<>)).GetGenericArguments()[0];
            var blockBuilder = new ExpressionBlockBuilder();

            var elementCount = Expression.PropertyOrField(arrayAccess, "Length");
            var exitLabel = Expression.Label();
            var counter = blockBuilder.CreateVariable<int>();
            blockBuilder += Expression.Assign(counter, Expression.Constant(0));
            blockBuilder += Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(counter, elementCount), 
                    Expression.Block(
                        Expression.Assign(
                            Expression.ArrayAccess(arrayAccess, counter), 
                            serializer.GenerateTypeDeserializer(genericType, bufferAccess)),
                        Expression.PostIncrementAssign(counter)
                    ),
                    Expression.Break(exitLabel))
            );
            return blockBuilder += Expression.Label(exitLabel);
        }

        public static Expression GenerateFastDeserializeArray(BinarySerializer serializer, Type type,
            Expression arrayAccess, Expression bufferAccess)
        {
            var genericType = type.GetGenericInterface(typeof(IEnumerable<>)).GetGenericArguments()[0];
            var readArrayMethod = typeof(CollectionExpressionGeneratorProviders)
                .GetMethod("ReadBlittableArray").MakeGenericMethod(genericType);
            return Expression.Call(readArrayMethod, bufferAccess, arrayAccess);
        }
        
        public static ExpressionGeneratorProvider CreateEnumerableProvider(Func<Type, bool> isInterested, 
            Type genericBackingType, GenerateAddElementExpressionDelegate generateAddElement)
        {
            return new(
                Priority: ProviderPriority.Normal,
                IsInterested: isInterested,
                Validate: type => type.GetGenericArguments().All(RuntimeTypeModel.IsTypeSerializable),
                GenerateSerializeExpression: GenerateSerializeEnumerable,
                GenerateDeserializeExpression: (serializer, type, bufferAccess) 
                    => GenerateDeserializeEnumerable(serializer, type, bufferAccess, 
                        genericBackingType, generateAddElement)
            );
        }

        private static IEnumerable<ExpressionGeneratorProvider> CreateBuiltinProviders()
        {
            // Provider for IList<>, IEnumerable<>, and Array types
            yield return new(
                Priority: ProviderPriority.Normal,
                IsInterested: type => type.IsOrImplementsGenericType(typeof(IList<>)) 
                                      || type.IsGenericType(typeof(IEnumerable<>)),
                Validate: type => RuntimeTypeModel.IsTypeSerializable(
                        type.GetGenericInterface(typeof(IEnumerable<>)).GetGenericArguments()[0]),
                GenerateSerializeExpression: (serializer, type, dataAccess, bufferAccess) =>
                {
                    var genericType = type.GetGenericInterface(typeof(IEnumerable<>)).GetGenericArguments()[0];

                    // We check if our type is an Array or List<>, we do this to dodge writing as enum and maybe
                    // write as blittable
                    if (type.BaseType == typeof(Array) || type.IsGenericType(typeof(List<>)))
                    {
                        var backingArray = type.IsGenericType(typeof(List<>))
                            ? Expression.PropertyOrField(dataAccess, "_items")
                            : dataAccess;

                        return PrimitiveExpressionGeneratorProviders.IsBlittable(serializer.GetProviderForType(genericType)) 
                            ? GenerateFastSerializeArray(serializer, type, backingArray, bufferAccess)
                            : GenerateSlowSerializeArray(serializer, type, backingArray, bufferAccess);
                    }
                    
                    // Its not a type we are optimized for so just generate the enum writer
                    return GenerateSerializeEnumerable(serializer, type, dataAccess, bufferAccess);
                },
                GenerateDeserializeExpression: (serializer, type, bufferAccess) =>
                {
                    var genericType = type.GetGenericInterface(typeof(IEnumerable<>)).GetGenericArguments()[0];
                    var genericListType = typeof(List<>).MakeGenericType(genericType);

                    var arrayConstructor = type.BaseType == typeof(Array)
                        ? type.GetConstructor(new[] {typeof(int)})
                        : genericListType.GetConstructor(new[] {typeof(int)});

                    var arrayType = type.BaseType == typeof(Array)
                        ? type
                        : genericListType;
                    
                    var blockBuilder = new ExpressionBlockBuilder();
                    var deserialized = blockBuilder.CreateVariable(arrayType);
                    
                    // Read the element count
                    var elementCount = blockBuilder.CreateVariable<int>();
                    blockBuilder += Expression.Assign(elementCount, 
                        Expression.Convert(Expression.Call(bufferAccess, 
                            typeof(SpanBufferReader).GetMethod("ReadUInt16")), typeof(int)));
                    
                    // Construct our type
                    blockBuilder += Expression.Assign(deserialized, Expression.New(arrayConstructor, elementCount));

                    // Get the backing array
                    var backingArray = type.BaseType == typeof(Array)
                        ? deserialized
                        : (Expression) Expression.PropertyOrField(deserialized, "_items");

                    // Deserialize
                    blockBuilder += 
                        PrimitiveExpressionGeneratorProviders.IsBlittable(serializer.GetProviderForType(genericType))
                            ? GenerateFastDeserializeArray(serializer, arrayType, backingArray, bufferAccess)
                            : GenerateSlowDeserializeArray(serializer, arrayType, backingArray, bufferAccess);

                    // If our array type is the generic list, we need to set _size after deserializing
                    if (arrayType == genericListType)
                        blockBuilder += Expression.Assign(Expression.PropertyOrField(deserialized, "_size"),
                            elementCount);
                    
                    var returnLabel = Expression.Label(type);
                    blockBuilder += Expression.Break(returnLabel, deserialized);
                    blockBuilder += Expression.Label(returnLabel, Expression.Constant(null, type));
                    return blockBuilder;
                }
            );
            
            // Provider for HashSet<>
            yield return CreateEnumerableProvider(
                isInterested: type => type.IsGenericType(typeof(HashSet<>)),
                genericBackingType: typeof(HashSet<>),
                generateAddElement: (instance, generic, deserialize)
                    => Expression.Call(instance,
                        typeof(HashSet<>).MakeGenericType(generic).GetMethod("Add"),
                        deserialize())
            );
            
            // LinkedList<> support
            yield return CreateEnumerableProvider(
                isInterested: type => type.IsGenericType(typeof(LinkedList<>)),
                genericBackingType: typeof(LinkedList<>),
                generateAddElement: (instance, generic, deserialize) 
                    => Expression.Call(instance, 
                        typeof(LinkedList<>).MakeGenericType(generic).GetMethod("AddLast", new [] {generic}), 
                        deserialize())
            );
            
            // IDictionary<> support
            yield return CreateEnumerableProvider(
                isInterested: type => type.IsOrImplementsGenericType(typeof(IDictionary<,>)),
                genericBackingType: typeof(Dictionary<,>),
                generateAddElement: (instance, generic, deserialize)
                    => Expression.Call(
                        typeof(DictionaryExtensions).GetMethod("Add")
                            .MakeGenericMethod(generic.GetGenericArguments()), 
                        instance, 
                        deserialize())
            );
        }
    }
}