using System;
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
        private static ExpressionGeneratorProvider GetPrimitiveProvider<T>(MethodInfo serialize, MethodInfo deserialize)
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

        private static ExpressionGeneratorProvider GetEnumerableProvider(Func<Type, bool> isInterested, 
            Type genericBackingType, GenerateAddElementExpressionDelegate generateAddElement)
        {
            return new(
                IsInterested: isInterested,
                Validate: type => type.GetGenericArguments().All(RuntimeTypeModel.IsTypeSerializable),
                GenerateSerializeExpression: (serializer, type, dataAccess, stackFrame)
                    => serializer.GenerateEnumerableSerialization(type, dataAccess, stackFrame),
                GenerateDeserializeExpression: (serializer, type, stackFrame)
                    => serializer.GenerateEnumerableDeserializer(type, stackFrame, genericBackingType, generateAddElement)
            );
        }
        
        public static IEnumerable<ExpressionGeneratorProvider> GetDefaultProviders()
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

            // Providers for collection types
            
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
                    var counter = blockFrame.GetOrCreateVariable(typeof(int));
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
                    
                    var deserialized = Expression.Variable(type);

                    // Read the element count
                    var elementCount = Expression.Variable(typeof(int));
                    var readElementCount = Expression.Assign(elementCount, 
                        Expression.Convert(Expression.Call(stackFrame.GetParameter("buffer"), 
                            typeof(SpanBufferReader).GetMethod("ReadUInt16")), typeof(int)));

                    // now deserialize each element
                    var constructed = Expression.Assign(deserialized, Expression.New(arrayConstructor, elementCount));

                    var exitLabel = Expression.Label(type);
                    var counter = Expression.Variable(typeof(int));
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
            
                    return Expression.Block(new[] { elementCount, counter, deserialized }, new Expression[]
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
