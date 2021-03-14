using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using BinaryRecords.Abstractions;
using BinaryRecords.Delegates;
using BinaryRecords.Expressions;
using BinaryRecords.Util;
using BinaryRecords.Extensions;
using BinaryRecords.Implementations;
using BinaryRecords.Records;

namespace BinaryRecords.Providers
{
    public static class CollectionExpressionGeneratorProviders
    {
        public static readonly IReadOnlyList<ExpressionGeneratorProvider> Builtin = CreateBuiltinProviders().ToList();

        public static unsafe void WriteBlittableArray<T>(ref BinaryBufferWriter buffer, T[] values) 
            where T : unmanaged
        {
            var size = sizeof(T) * values.Length;
            var bookmark = buffer.ReserveBookmark(size);
            buffer.WriteBookmark(bookmark, values, 
                (buffer, values) => MemoryMarshal.Cast<T, byte>(values.AsSpan()).CopyTo(buffer));
        }

        public static unsafe void ReadBlittableArray<T>(ref BinaryBufferReader buffer, T[] outValues) 
            where T : unmanaged
        {
            var size = sizeof(T) * outValues.Length;
            var values = buffer.ReadBytes(size);
            var casted = MemoryMarshal.Cast<byte, T>(values);
            casted.CopyTo(outValues);
        }
        
        public static Expression GenerateSerializeEnumerable(
            ITypingLibrary typingLibrary,
            Type type,
            Expression buffer,
            Expression data,
            VersionWriter? versioning = null)
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
            versioning?.Start(blockBuilder, buffer, typingLibrary.BitSize);
            
            var enumerable = blockBuilder.CreateVariable(enumerableType);
            var enumerator = blockBuilder.CreateVariable(enumeratorType);
            
            blockBuilder += Expression.Assign(enumerable, Expression.Convert(data, enumerableType));
            blockBuilder += Expression.Assign(enumerator, 
                Expression.Call(enumerable, enumerableType.GetMethod("GetEnumerator")!));
            
            var countBookmark = blockBuilder.CreateVariable<BinaryBufferWriter.Bookmark>();
            blockBuilder += Expression.Assign(countBookmark, BufferWriterExpressions.ReserveBookmark<ushort>(buffer));

            var written = blockBuilder.CreateVariable<ushort>();
            
            var loopExit = Expression.Label();
            blockBuilder += Expression.Loop(
                Expression.IfThenElse(
                    Expression.Call(enumerator, typeof(IEnumerator).GetMethod("MoveNext")!),
                    Expression.Block(new []
                    {
                        typingLibrary.GenerateSerializeExpression(
                            genericType, 
                            buffer, 
                            Expression.PropertyOrField(enumerator, "Current")),
                        Expression.PostIncrementAssign(written)
                    }),
                    Expression.Break(loopExit))
            );
            blockBuilder += Expression.Label(loopExit);
            blockBuilder += BufferWriterExpressions.WriteUInt16Bookmark(buffer, countBookmark, written);
            versioning?.Stop(blockBuilder, buffer, typingLibrary.BitSize);
            return blockBuilder;
        }

        public static Expression GenerateDeserializeEnumerable(
            ITypingLibrary typingLibrary,
            Type type,
            Expression buffer,
            Type genericBackingType, 
            GenerateAddElementExpressionDelegate generateAddElement)
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
                Expression.Convert(
                    BufferReaderExpressions.ReadUInt16(buffer), 
                    typeof(int)));

            // now deserialize each element
            blockBuilder += Expression.Assign(deserialized, 
                collectionConstructor!.GetParameters().Length == 1 
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
                            () => typingLibrary.GenerateDeserializeExpression(genericType, buffer)),
                        Expression.PostIncrementAssign(counter)
                    }),
                    Expression.Break(exitLabel, deserialized))
            );
            return blockBuilder += Expression.Label(exitLabel, deserialized);
        }

        public static Expression GenerateSlowSerializeArray(
            ITypingLibrary typingLibrary,
            Type type,
            Expression buffer,
            Expression data,
            VersionWriter? versioning = null)
        {
            var genericType = type.GetGenericInterface(typeof(IEnumerable<>)).GetGenericArguments()[0];
            
            var blockBuilder = new ExpressionBlockBuilder();
            versioning?.Start(blockBuilder, buffer, typingLibrary.BitSize);

            var arrayLength = Expression.PropertyOrField(data, "Length");
            blockBuilder += BufferWriterExpressions.WriteUInt16(
                buffer,
                Expression.Convert(arrayLength, typeof(ushort)));

            var exitLabel = Expression.Label();
            var counter = blockBuilder.CreateVariable<int>();
            blockBuilder += Expression.Assign(counter, Expression.Constant(0));
            blockBuilder += Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(counter, arrayLength), 
                    Expression.Block(
                        typingLibrary.GenerateSerializeExpression(
                            genericType, 
                            buffer, 
                            Expression.ArrayAccess(data, counter)),
                        Expression.PostIncrementAssign(counter)
                    ),
                    Expression.Break(exitLabel))
            );
            blockBuilder += Expression.Label(exitLabel);
            versioning?.Stop(blockBuilder, buffer, typingLibrary.BitSize);
            return blockBuilder;    
        }

        public static Expression GenerateFastSerializeArray(
            ITypingLibrary typingLibrary,
            Type type,
            Expression buffer,
            Expression data,
            VersionWriter? versioning = null)
        {
            var genericType = type.GetGenericInterface(typeof(IEnumerable<>)).GetGenericArguments()[0];
            var blockBuilder = new ExpressionBlockBuilder();
            versioning?.Start(blockBuilder, buffer, typingLibrary.BitSize);
            
            // Write the element count
            var arrayLength = Expression.PropertyOrField(data, "Length");

            blockBuilder += BufferWriterExpressions.WriteUInt16(
                buffer,
                Expression.Convert(arrayLength, typeof(ushort)));

            var writeArrayMethod = typeof(CollectionExpressionGeneratorProviders)
                .GetMethod("WriteBlittableArray")!.MakeGenericMethod(genericType);
            blockBuilder += Expression.Call(writeArrayMethod, buffer, data);
            versioning?.Stop(blockBuilder, buffer, typingLibrary.BitSize);
            return blockBuilder;
        }

        public static Expression GenerateSlowDeserializeArray(
            ITypingLibrary typingLibrary,
            Type type,
            Expression buffer,
            Expression arrayAccess)
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
                            typingLibrary.GenerateDeserializeExpression(genericType, buffer)),
                        Expression.PostIncrementAssign(counter)
                    ),
                    Expression.Break(exitLabel))
            );
            return blockBuilder += Expression.Label(exitLabel);
        }

        public static Expression GenerateFastDeserializeArray(
            Type type,
            Expression arrayAccess, 
            Expression bufferAccess)
        {
            var genericType = type.GetGenericInterface(typeof(IEnumerable<>)).GetGenericArguments()[0];
            var readArrayMethod = typeof(CollectionExpressionGeneratorProviders)
                .GetMethod("ReadBlittableArray")!.MakeGenericMethod(genericType);
            return Expression.Call(readArrayMethod, bufferAccess, arrayAccess);
        }
        
        public static ExpressionGeneratorProvider CreateEnumerableProvider(
            Func<Type, bool> isInterested, 
            Type genericBackingType, 
            GenerateAddElementExpressionDelegate generateAddElement)
        {
            return new(
                Name: $"{genericBackingType.Name}Provider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, library) => isInterested(type) && 
                                                 type.GetGenericArguments().All(library.IsTypeSerializable),
                GenerateSerializeExpression: GenerateSerializeEnumerable,
                GenerateDeserializeExpression: (typingLibrary, type, buffer) => 
                    GenerateDeserializeEnumerable(typingLibrary, type, buffer, genericBackingType, generateAddElement),
                GenerateTypeRecord: (typeLibrary, type) =>
                {
                    var enumerableInterface = 
                        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>) 
                            ? type
                            : type.GetGenericInterface(typeof(IEnumerable<>));
                    var genericType = enumerableInterface.GetGenericArguments()[0];
                    return new ListTypeRecord(typeLibrary.GetTypeRecord(genericType));
                }
            );
        }

        private static IEnumerable<ExpressionGeneratorProvider> CreateBuiltinProviders()
        {
            // Provider for IList<>, IEnumerable<>, and Array types
            yield return new(
                Name: "ValueSequenceProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: (type, library) => (type.IsOrImplementsGenericType(typeof(IList<>)) || 
                                                  type.IsGenericType(typeof(IEnumerable<>))) && 
                                                  library.IsTypeSerializable(type.GetGenericInterface(typeof(IEnumerable<>)).GetGenericArguments()[0]),
                GenerateSerializeExpression: (typingLibrary, type, buffer, data, versioning) =>
                {
                    var genericType = type.GetGenericInterface(typeof(IEnumerable<>)).GetGenericArguments()[0];

                    // We check if our type is an Array or List<>, we do this to dodge writing as enum and maybe
                    // write as blittable
                    if (type.BaseType == typeof(Array) || type.IsGenericType(typeof(List<>)))
                    {
                        var backingArray = type.IsGenericType(typeof(List<>))
                            ? Expression.PropertyOrField(data, "_items")
                            : data;

                        return typingLibrary.IsTypeBlittable(genericType) && BitConverter.IsLittleEndian
                            ? GenerateFastSerializeArray(typingLibrary, type, buffer, backingArray, versioning)
                            : GenerateSlowSerializeArray(typingLibrary, type, buffer, backingArray, versioning);
                    }
                    
                    // Its not a type we are optimized for so just generate the enum writer
                    return GenerateSerializeEnumerable(typingLibrary, type, buffer, data, versioning);
                },
                GenerateDeserializeExpression: (typingLibrary, type, buffer) =>
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
                        Expression.Convert(
                            BufferReaderExpressions.ReadUInt16(buffer), 
                            typeof(int)));
                    
                    // Construct our type
                    blockBuilder += Expression.Assign(
                        deserialized, 
                        Expression.New(arrayConstructor!, elementCount));

                    // Get the backing array
                    Expression backingArray = type.BaseType != typeof(Array)
                        ? Expression.PropertyOrField(deserialized, "_items")
                        : deserialized;

                    // Deserialize
                    blockBuilder += typingLibrary.IsTypeBlittable(genericType) && BitConverter.IsLittleEndian
                            ? GenerateFastDeserializeArray(arrayType, backingArray, buffer)
                            : GenerateSlowDeserializeArray(typingLibrary, arrayType, buffer, backingArray);

                    // If our array type is the generic list, we need to set _size after deserializing
                    if (arrayType == genericListType)
                        blockBuilder += Expression.Assign(Expression.PropertyOrField(deserialized, "_size"),
                            elementCount);
                    
                    return blockBuilder += deserialized;
                },
                GenerateTypeRecord: (typingLibrary, type) =>
                {
                    var genericType = type.GetGenericInterface(typeof(IEnumerable<>)).GetGenericArguments()[0];
                    return new ListTypeRecord(typingLibrary.GetTypeRecord(genericType));
                }
            );
            
            // Provider for HashSet<>
            yield return CreateEnumerableProvider(
                isInterested: type => type.IsGenericType(typeof(HashSet<>)),
                genericBackingType: typeof(HashSet<>),
                generateAddElement: (instance, generic, deserialize) => 
                    Expression.Call(instance, typeof(HashSet<>).MakeGenericType(generic).GetMethod("Add")!, deserialize())
            );
            
            // LinkedList<> support
            yield return CreateEnumerableProvider(
                isInterested: type => type.IsGenericType(typeof(LinkedList<>)),
                genericBackingType: typeof(LinkedList<>),
                generateAddElement: (instance, generic, deserialize) => 
                    Expression.Call(instance, 
                        typeof(LinkedList<>).MakeGenericType(generic).GetMethod("AddLast", new [] {generic})!, 
                        deserialize())
            );
            
            // IDictionary<> support
            yield return CreateEnumerableProvider(
                isInterested: type => type.IsOrImplementsGenericType(typeof(IDictionary<,>)),
                genericBackingType: typeof(Dictionary<,>),
                generateAddElement: (instance, generic, deserialize) => 
                    Expression.Call(
                        typeof(DictionaryExtensions).GetMethod("Add")!.MakeGenericMethod(generic.GetGenericArguments()), 
                        instance, 
                        deserialize())
            );
        }
    }
}