using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using BinaryRecords.Delegates;
using BinaryRecords.Extensions;
using Krypton.Buffers;

namespace BinaryRecords.Providers
{
    public static class BuiltinGeneratorProviders
    {
        public static ExpressionGeneratorProvider GetPrimitiveProvider<T>(MethodInfo serialize, MethodInfo deserialize)
        {
            return new(
                IsInterested: type => type == typeof(T),
                Validate: type => true,
                GenerateSerializeExpression: (serializer, type, dataAccess, stackFrame)
                    => Expression.Call(stackFrame.GetParameter("buffer"), serialize, dataAccess),
                GenerateDeserializeExpression: (serializer, type, stackFrame) 
                    => Expression.Call(stackFrame.GetParameter("buffer"), deserialize)
            );
        }

        public static ExpressionGeneratorProvider GetEnumerableProvider(Func<Type, bool> isInterested, 
            Type genericBackingType, GenerateAddElementExpressionDelegate generateAddElement)
        {
            return new(
                IsInterested: isInterested,
                Validate: type => type.GetGenericArguments().All(RuntimeTypeModel.IsTypeSerializable),
                GenerateSerializeExpression: (serializer, type, dataAccess, stackFrame) =>
                {
                    var enumerableInterface = 
                        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>) 
                            ? type 
                            : type.GetGenericInterface(typeof(IEnumerable<>));
                    var generics = enumerableInterface.GetGenericArguments();
                    var genericType = generics[0];
                    
                    var enumerableType = typeof(IEnumerable<>).MakeGenericType(generics);
                    var enumeratorType = typeof(IEnumerator<>).MakeGenericType(generics);
                    
                    var blockFrame = new StackFrame();
                    var enumerable = blockFrame.CreateVariable(enumerableType);
                    var enumerator = blockFrame.CreateVariable(enumeratorType);
                    
                    var assignEnumerable = Expression.Assign(enumerable, 
                        Expression.Convert(dataAccess, enumerableType));
                    var assignEnumerator = Expression.Assign(enumerator,
                        Expression.Call(enumerable, enumerableType.GetMethod("GetEnumerator")));
                    
                    var countBookmark = blockFrame.CreateVariable<SpanBufferWriter.Bookmark>();
                    var assignBookmark = Expression.Assign(countBookmark, 
                        Expression.Call(stackFrame.GetParameter("buffer"), 
                            typeof(SpanBufferWriter).GetMethod("ReserveBookmark"), 
                            Expression.Constant(sizeof(ushort))));

                    var written = blockFrame.CreateVariable<ushort>();
                    
                    var loopExit = Expression.Label();
                    var writeLoop = Expression.Loop(
                        Expression.IfThenElse(
                            Expression.Call(enumerator, typeof(IEnumerator).GetMethod("MoveNext")),
                            Expression.Block(new []
                            {
                                serializer.GenerateTypeSerializer(genericType, 
                                    Expression.PropertyOrField(enumerator, "Current"), 
                                    stackFrame),
                                Expression.PostIncrementAssign(written)
                            }),
                            Expression.Break(loopExit))
                    );

                    var writeBookmarkMethod = typeof(SpanBufferWriterExtensions).GetMethod("WriteUInt16Bookmark");
                    var writeBookmark = Expression.Call(
                        writeBookmarkMethod,
                        stackFrame.GetParameter("buffer"),
                        countBookmark,
                        written
                    );

                    return Expression.Block(blockFrame.Variables, new Expression[]
                    {
                        assignEnumerable,
                        assignEnumerator,
                        assignBookmark,
                        writeLoop,
                        Expression.Label(loopExit),
                        writeBookmark
                    });
                },
                GenerateDeserializeExpression: (serializer, type, stackFrame) =>
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
                    
                    var blockFrame = new StackFrame();
                    var deserialized = blockFrame.CreateVariable(constructingCollectionType);

                    // Read the element count
                    var elementCount = blockFrame.CreateVariable<int>();
                    var readElementCount = Expression.Assign(elementCount, 
                        Expression.Convert(Expression.Call(stackFrame.GetParameter("buffer"), 
                            typeof(SpanBufferReader).GetMethod("ReadUInt16")), typeof(int)));

                    // now deserialize each element
                    var constructed = Expression.Assign(deserialized, 
                        collectionConstructor.GetParameters().Length == 1 
                            ? Expression.New(collectionConstructor, elementCount) 
                            : Expression.New(collectionConstructor));

                    var exitLabel = Expression.Label(constructingCollectionType);
                    var counter = blockFrame.CreateVariable<int>();
                    var assignCounter = Expression.Assign(counter, Expression.Constant(0));
                    var deserializationLoop = Expression.Loop(
                        Expression.IfThenElse(
                            Expression.LessThan(counter, elementCount),
                            Expression.Block(new []
                            {
                                generateAddElement(deserialized, genericType, 
                                    () => serializer.GenerateTypeDeserializer(genericType, stackFrame)),
                                Expression.PostIncrementAssign(counter)
                            }),
                            Expression.Break(exitLabel, deserialized))
                    );
                    
                    return Expression.Block(blockFrame.Variables, new Expression[]
                    {
                        readElementCount,
                        constructed,
                        assignCounter,
                        deserializationLoop,
                        Expression.Label(exitLabel, Expression.Constant(null, constructingCollectionType))
                    });
                }
            );
        }

        public static IEnumerable<ExpressionGeneratorProvider> GetDefaultPrimitiveProviders()
        {
            // Create the providers for each primitive type
            var bufferType = typeof(SpanBufferWriter);
            var bufferReaderType = typeof(SpanBufferReader);

            // byte types
            yield return GetPrimitiveProvider<byte>(bufferType.GetMethod("WriteUInt8"),
                bufferReaderType.GetMethod("ReadUInt8"));
            yield return GetPrimitiveProvider<sbyte>(bufferType.GetMethod("WriteInt8"),
                bufferReaderType.GetMethod("ReadInt8"));   
            
            // short types
            yield return GetPrimitiveProvider<ushort>(bufferType.GetMethod("WriteUInt16"),
                bufferReaderType.GetMethod("ReadUInt16"));
            yield return GetPrimitiveProvider<short>(bufferType.GetMethod("WriteInt16"),
                bufferReaderType.GetMethod("ReadInt16"));

            // int types
            yield return GetPrimitiveProvider<uint>(bufferType.GetMethod("WriteUInt32"),
                bufferReaderType.GetMethod("ReadUInt32"));
            yield return GetPrimitiveProvider<int>(bufferType.GetMethod("WriteInt32"),
                bufferReaderType.GetMethod("ReadInt32"));

            // long types
            yield return GetPrimitiveProvider<ulong>(bufferType.GetMethod("WriteUInt64"),
                bufferReaderType.GetMethod("ReadUInt64"));
            yield return GetPrimitiveProvider<long>(bufferType.GetMethod("WriteInt64"),
                bufferReaderType.GetMethod("ReadInt64"));

            // float types
            yield return GetPrimitiveProvider<float>(bufferType.GetMethod("WriteFloat32"),
                bufferReaderType.GetMethod("ReadFloat32"));
            yield return GetPrimitiveProvider<double>(bufferType.GetMethod("WriteFloat64"),
                bufferReaderType.GetMethod("ReadFloat64"));

            // string type
            yield return GetPrimitiveProvider<string>(bufferType.GetMethod("WriteUTF8String"),
                bufferReaderType.GetMethod("ReadUTF8String"));
        }

        public static IEnumerable<ExpressionGeneratorProvider> GetDefaultCollectionProviders()
        {
            // Provider for array types
            yield return new(
                IsInterested: type => type.BaseType == typeof(Array),
                Validate: type 
                    => RuntimeTypeModel.IsTypeSerializable(
                        type.GetGenericInterface(typeof(IEnumerable<>)).GetGenericArguments()[0]),
                GenerateSerializeExpression: (serializer, type, dataAccess, stackFrame) =>
                {
                    var genericTypes = type.GetGenericInterface(typeof(IEnumerable<>)).GetGenericArguments();
                    var genericType = genericTypes[0];

                    var blockFrame = new StackFrame();
            
                    // Write the element count
                    var arrayLength = Expression.PropertyOrField(dataAccess, "Length");
                    var writeElementCount = Expression.Call(
                        stackFrame.GetParameter("buffer"),
                        typeof(SpanBufferWriter).GetMethod("WriteUInt16"), 
                        Expression.Convert(arrayLength, typeof(ushort)));

                    // Write each element
                    var exitLabel = Expression.Label(type);
                    var counter = blockFrame.CreateVariable<int>();
                    var assignCounter = Expression.Assign(counter, Expression.Constant(0));
                    var serializeLoop = Expression.Loop(
                        Expression.IfThenElse(
                            Expression.LessThan(counter, arrayLength), 
                            Expression.Block(
                                serializer.GenerateTypeSerializer(genericType, Expression.ArrayAccess(dataAccess, counter), stackFrame),
                                Expression.PostIncrementAssign(counter)
                            ),
                            Expression.Break(exitLabel, Expression.Constant(null, type)))
                    );
            
                    // Construct the expression block
                    return Expression.Block(blockFrame.Variables, 
                        writeElementCount, 
                        assignCounter, 
                        serializeLoop, 
                        Expression.Label(exitLabel, Expression.Constant(null, type)));
                },
                GenerateDeserializeExpression: (serializer, type, stackFrame) =>
                {
                    var genericTypes = type.GetGenericInterface(typeof(IEnumerable<>)).GetGenericArguments();
                    var genericType = genericTypes[0];
            
                    var arrayConstructor = type.GetConstructor(new[] {typeof(int)});

                    var blockFrame = new StackFrame();
                    var deserialized = blockFrame.CreateVariable(type);

                    // Read the element count
                    var elementCount = blockFrame.CreateVariable<int>();
                    var readElementCount = Expression.Assign(elementCount, 
                        Expression.Convert(Expression.Call(stackFrame.GetParameter("buffer"), 
                            typeof(SpanBufferReader).GetMethod("ReadUInt16")), typeof(int)));

                    // now deserialize each element
                    var constructed = Expression.Assign(deserialized, Expression.New(arrayConstructor, elementCount));

                    var exitLabel = Expression.Label(type);
                    var counter = blockFrame.CreateVariable<int>();
                    var assignCounter = Expression.Assign(counter, Expression.Constant(0));
                    var deserializationLoop = Expression.Loop(
                        Expression.IfThenElse(
                            Expression.LessThan(counter, elementCount), 
                            Expression.Block(
                                Expression.Assign(
                                    Expression.ArrayAccess(deserialized, counter), 
                                    serializer.GenerateTypeDeserializer(genericType, stackFrame)),
                                Expression.PostIncrementAssign(counter)
                            ),
                            Expression.Break(exitLabel, deserialized))
                    );
            
                    return Expression.Block(blockFrame.Variables, new Expression[]
                    {
                        readElementCount,
                        constructed,
                        assignCounter,
                        deserializationLoop,
                        Expression.Label(exitLabel, Expression.Constant(null, type))
                    });
                }
            );
            
            // Provider for IList<> and IEnumerable<>
            yield return GetEnumerableProvider(
                isInterested: type => type.IsOrImplementsGenericType(typeof(IList<>))
                                      || type.IsGenericType(typeof(IEnumerable<>)),
                genericBackingType: typeof(List<>),
                generateAddElement: (instance, generic, deserialize)
                    => Expression.Call(instance,
                        typeof(List<>).MakeGenericType(generic).GetMethod("Add"),
                        deserialize())
            );

            // Provider for HashSet<>
            yield return GetEnumerableProvider(
                isInterested: type => type.IsGenericType(typeof(HashSet<>)),
                genericBackingType: typeof(HashSet<>),
                generateAddElement: (instance, generic, deserialize)
                    => Expression.Call(instance,
                        typeof(HashSet<>).MakeGenericType(generic).GetMethod("Add"),
                        deserialize())
            );
            
            // LinkedList<> support
            yield return GetEnumerableProvider(
                isInterested: type => type.IsGenericType(typeof(LinkedList<>)),
                genericBackingType: typeof(LinkedList<>),
                generateAddElement: (instance, generic, deserialize) 
                    => Expression.Call(instance, 
                        typeof(LinkedList<>).MakeGenericType(generic).GetMethod("AddLast", new [] {generic}), 
                        deserialize())
            );
            
            // IDictionary<> support
            yield return GetEnumerableProvider(
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

        public static IEnumerable<ExpressionGeneratorProvider> GetDefaultProviders()
        {
            foreach (var provider in GetDefaultPrimitiveProviders()
                .Concat(GetDefaultCollectionProviders())) yield return provider;

            // Provider for tuples
            yield return new(
                IsInterested: type => type.GetInterface(typeof(ITuple).FullName) != null,
                Validate: type => type.GetGenericArguments().All(RuntimeTypeModel.IsTypeSerializable),
                GenerateSerializeExpression: (serializer, type, dataAccess, stackFrame) =>
                {
                    var expressions = type.GetGenericArguments()
                        .Select((genericType, i) =>
                        {
                            var access = Expression.PropertyOrField(dataAccess, $"Item{i + 1}");
                            return serializer.GenerateTypeSerializer(genericType, access, stackFrame);
                        });
                    return Expression.Block(expressions);
                },
                GenerateDeserializeExpression: (serializer, type, stackFrame) =>
                {
                    var genericTypes = type.GetGenericArguments();
                    var constructor = type.GetConstructor(genericTypes);
                    return Expression.New(constructor,
                        genericTypes.Select(t => serializer.GenerateTypeDeserializer(t, stackFrame)));
                }
            );
            
            // Provider for KeyValuePair<>
            yield return new(
                IsInterested: type => type.IsGenericType(typeof(KeyValuePair<,>)),
                Validate: type => type.GetGenericArguments().All(RuntimeTypeModel.IsTypeSerializable),
                GenerateSerializeExpression: (serializer, type, dataAccess, stackFrame) =>
                {
                    var generics = type.GetGenericArguments();
                    var keyType = generics[0];
                    var valueType = generics[1];
                    return Expression.Block(
                        serializer.GenerateTypeSerializer(keyType, 
                            Expression.PropertyOrField(dataAccess, "Key"), 
                            stackFrame),
                        serializer.GenerateTypeSerializer(valueType, 
                            Expression.PropertyOrField(dataAccess, "Value"), 
                            stackFrame)
                    );
                },
                GenerateDeserializeExpression: (serializer, type, stackFrame) =>
                {
                    var generics = type.GetGenericArguments();
                    var keyType = generics[0];
                    var valueType = generics[1];
                    var ctor = type.GetConstructor(generics);
                    return Expression.New(ctor,
                        serializer.GenerateTypeDeserializer(keyType, stackFrame),
                        serializer.GenerateTypeDeserializer(valueType, stackFrame));
                }
            );
        }
    }
}
