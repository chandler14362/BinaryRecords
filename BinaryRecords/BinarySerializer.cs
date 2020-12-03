using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using BinaryRecords.Delegates;
using BinaryRecords.Extensions;
using BinaryRecords.Providers;
using Krypton.Buffers;

namespace BinaryRecords
{
    public class BinarySerializer
    {
        private delegate void SerializeRecordDelegate(Object obj, ref SpanBufferWriter buffer);

        private delegate object DeserializeRecordDelegate(ref SpanBufferReader bufferReader);

        public static readonly Type BufferWriterType =
            Type.GetType($"{typeof(SpanBufferWriter).FullName}&,Krypton.Buffers");

        public static readonly Type BufferReaderType =
            Type.GetType($"{typeof(SpanBufferReader).FullName}&,Krypton.Buffers");
        
        private record RecordSerializationPair(SerializeRecordDelegate Serialize, DeserializeRecordDelegate Deserialize);
        
        private Dictionary<Type, RecordSerializationPair> _serializers = new();

        private Dictionary<Type, RecordConstructionModel> _constructionModels;

        private List<ExpressionGeneratorProvider> _generatorProvider;

        public BinarySerializer(Dictionary<Type, RecordConstructionModel> constructionModels,
            List<ExpressionGeneratorProvider> generatorProviders)
        {
            _serializers = new();
            _constructionModels = new(constructionModels);
            _generatorProvider = new(generatorProviders);
        }
        
        public BinarySerializer()
        {
            _serializers = new();
            _constructionModels = new();
            _generatorProvider = new();
        }

        public void GenerateRecordSerializers()
        {
            // Clear our existing serializers
            _serializers.Clear();
            
            foreach (var type in _constructionModels.Keys)
                TryGetRecordSerializer(type, out _);
        }
        
        // TODO: Have construction model shit use providers
        private bool TryGetRecordSerializer(Type type, out RecordSerializationPair serializer)
        {
            // Ensure the type is one of our record types
            if (!_constructionModels.ContainsKey(type))
            {
                serializer = null;
                return false;
            }
            
            // Check if we already handle this type
            if (_serializers.TryGetValue(type, out serializer))
                return true;

            // Create new serializers
            serializer = new (GenerateSerializeDelegate(type), GenerateDeserializeDelegate(type));
            _serializers[type] = serializer;
            return true;
        }

        private SerializeRecordDelegate GenerateSerializeDelegate(Type type)
        {
            // Generate a serializer for each property
            var stackFrame = new StackFrame();
            var rootExpressions = new List<Expression>();
            
            // Declare parameters
            stackFrame.CreateParameter<object>("obj");
            stackFrame.CreateParameter(BufferWriterType, "buffer");

            // Cast the record from object to its exact type
            var recordInstance = Expression.Assign(
                stackFrame.GetOrCreateVariable(type),
                Expression.Convert(stackFrame.GetParameter("obj"), type)
            );
            rootExpressions.Add(recordInstance);

            // Generate a serializer for each record property
            var propertySerializers = _constructionModels[type].Properties
                .Select(property => GenerateTypeSerializer(property.PropertyType, Expression.Property(recordInstance, property), stackFrame));
            rootExpressions.AddRange(propertySerializers);

            // Compile and return
            var lambda = Expression.Lambda<SerializeRecordDelegate>(
                Expression.Block(stackFrame.Variables, rootExpressions), 
                stackFrame.Parameters
            );
            return lambda.Compile();
        }

        public Expression GenerateTypeSerializer(Type type, Expression dataAccess, StackFrame stackFrame)
        {
            // Check if any providers are interested in the type
            var provider = _generatorProvider.FirstOrDefault(model => model.IsInterested(type));
            if (provider != null)
                return provider.GenerateSerializeExpression(this, type, dataAccess, stackFrame);

            // Check if we are dealing with a registered type
            if (TryGetRecordSerializer(type, out var recordPair))
            {
                throw new NotImplementedException();
            }
            // Check if we are dealing with an IList
            else if (type.ImplementsGenericInterface(typeof(ICollection<>)) && false)
            {
                var genericTypes = type.GetGenericArguments();
                var genericType = genericTypes[0];

                // Write the element count and then serialize each element
                var ass = Expression.Variable(typeof(ICollection<>).MakeGenericType(genericTypes));
                var assassign = Expression.Assign(ass, dataAccess);
                
                var writeElementCount = Expression.Call(stackFrame.GetParameter("buffer"),
                    typeof(SpanBufferWriter).GetMethod("WriteUInt16"),
                    Expression.Convert(Expression.PropertyOrField(assassign, "Count"), typeof(ushort)));
                
                // now serialize each element
                var exitLabel = Expression.Label();
                var counter = Expression.Variable(typeof(int));
                var assignCounter = Expression.Assign(counter, Expression.Constant(0));
                var serializationLoop = Expression.Loop(
                    Expression.IfThenElse(
                        Expression.LessThan(counter, Expression.PropertyOrField(assassign, "Count")),
                        Expression.Block(new []
                        {
                            GenerateTypeSerializer(genericType, Expression.MakeIndex(dataAccess, type.GetProperty("Item"), new []{ counter }), stackFrame),
                            Expression.PostIncrementAssign(counter)
                        }),
                        Expression.Break(exitLabel))
                );
                
                return Expression.Block(new[] { ass, counter }, new Expression[]
                {
                    assassign,
                    writeElementCount,
                    assignCounter,
                    serializationLoop,
                    Expression.Label(exitLabel)
                });
            }
            else
            {
                throw new Exception($"Couldn't generate serializer for type: {type.Name}");
            }
        }

        public Expression GenerateEnumerableSerialization(Type type, Expression dataAccess, StackFrame stackFrame)
        {
            var enumerableInterface = 
                type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>) 
                    ? type 
                    : type.GetGenericInterface(typeof(IEnumerable<>));
            var generics = enumerableInterface.GetGenericArguments();
            var genericType = generics[0];
            
            var enumerableType = typeof(IEnumerable<>)
                .MakeGenericType(generics);

            var enumeratorType = typeof(IEnumerator<>)
                .MakeGenericType(generics);
            
            var blockFrame = new StackFrame();
            var enumerable = blockFrame.GetOrCreateVariable(enumerableType);
            var enumerator = blockFrame.GetOrCreateVariable(enumeratorType);
            
            var assignEnumerable = Expression.Assign(enumerable, 
                Expression.Convert(dataAccess, enumerableType));
            var assignEnumerator = Expression.Assign(enumerator,
                Expression.Call(enumerable, enumerableType.GetMethod("GetEnumerator")));
            
            var countBookmark = blockFrame.GetOrCreateVariable(typeof(SpanBufferWriter.Bookmark));
            var assignBookmark = Expression.Assign(countBookmark, 
                Expression.Call(stackFrame.GetParameter("buffer"), 
                    typeof(SpanBufferWriter).GetMethod("ReserveBookmark"), 
                    Expression.Constant(sizeof(ushort))));

            var written = blockFrame.GetOrCreateVariable(typeof(ushort));
            
            var loopExit = Expression.Label();
            var writeLoop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.Call(enumerator, typeof(IEnumerator).GetMethod("MoveNext")),
                    Expression.Block(new []
                    {
                        GenerateTypeSerializer(genericType, Expression.PropertyOrField(enumerator, "Current"), stackFrame),
                        Expression.PostIncrementAssign(written)
                    }),
                    Expression.Break(loopExit))
            );

            var writeBookmarkMethod = typeof(BinarySerializerUtil).GetMethod("WriteUInt16Bookmark");
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
        }

        private DeserializeRecordDelegate GenerateDeserializeDelegate(Type type)
        {
            var model = _constructionModels[type];
            var stackFrame = new StackFrame();
            stackFrame.CreateParameter(BufferReaderType, "buffer");
            
            var blockExpression = model.Constructor != null
                ? GenerateConstructorDeserializer(model, stackFrame)
                : GenerateOpenDeserializer(model, stackFrame);
            
            var lambda = Expression.Lambda<DeserializeRecordDelegate>(
                blockExpression,
                stackFrame.Parameters
            );
            return lambda.Compile();
        }

        private BlockExpression GenerateConstructorDeserializer(RecordConstructionModel model, StackFrame stackFrame)
        {
            var returnTarget = Expression.Label(model.type);
            var constructorCall = Expression.New(model.Constructor,
                model.Properties.Select(p => GenerateTypeDeserializer(p.PropertyType, stackFrame)),
                model.Properties);
            var returnExpression = Expression.Return(returnTarget, constructorCall, model.type);
            var returnLabel = Expression.Label(returnTarget, constructorCall);
            return Expression.Block(returnExpression, returnLabel);
        }

        private BlockExpression GenerateOpenDeserializer(RecordConstructionModel model, StackFrame stackFrame)
        {
            var returnTarget = Expression.Label(model.type);
            var memberInit = Expression.MemberInit(
                Expression.New(model.type.GetConstructor(new Type[] { })),
                model.Properties.Select(p =>
                    Expression.Bind(p, GenerateTypeDeserializer(p.PropertyType, stackFrame))));
            var returnExpression = Expression.Return(returnTarget, memberInit, model.type);
            var returnLabel = Expression.Label(returnTarget, memberInit);
            return Expression.Block(returnExpression, returnLabel);
        }
        
        public Expression GenerateTypeDeserializer(Type type, StackFrame stackFrame)
        {
            // Check if we have a provider willing to deserialize the type
            var provider = _generatorProvider.FirstOrDefault(model => model.IsInterested(type));
            if (provider != null)
                return provider.GenerateDeserializeExpression(this, type, stackFrame);
            
            throw new Exception($"Couldn't generate serializer for type: {type.Name}");
            
            // Check if we are dealing with a serializable record
            if (TryGetRecordSerializer(type, out var recordPair))
            {
                throw new NotImplementedException();
            }
        }

        public Expression GenerateEnumerableDeserializer(Type type, StackFrame stackFrame, 
            Type genericBackingType, GenerateAddElementExpressionDelegate addElementExpressionDelegate)
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
            var deserialized = blockFrame.GetOrCreateVariable(constructingCollectionType);

            // Read the element count
            var elementCount = Expression.Variable(typeof(int));
            var readElementCount = Expression.Assign(elementCount, 
                Expression.Convert(Expression.Call(stackFrame.GetParameter("buffer"), 
                    typeof(SpanBufferReader).GetMethod("ReadUInt16")), typeof(int)));

            // now deserialize each element
            var constructed = Expression.Assign(deserialized, 
                collectionConstructor.GetParameters().Length == 1 
                    ? Expression.New(collectionConstructor, elementCount) 
                    : Expression.New(collectionConstructor));

            var exitLabel = Expression.Label(constructingCollectionType);
            var counter = Expression.Variable(typeof(int));
            var assignCounter = Expression.Assign(counter, Expression.Constant(0));
            var deserializationLoop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(counter, elementCount),
                    Expression.Block(new []
                    {
                        addElementExpressionDelegate(deserialized, genericType, 
                            () => GenerateTypeDeserializer(genericType, stackFrame)),
                        Expression.PostIncrementAssign(counter)
                    }),
                    Expression.Break(exitLabel, deserialized))
            );
            
            return Expression.Block(new[] { elementCount, counter, deserialized }, new Expression[]
            {
                readElementCount,
                constructed,
                assignCounter,
                deserializationLoop,
                Expression.Label(exitLabel, Expression.Constant(null, constructingCollectionType))
            });
        }
        

        public void Serialize<T>(T obj, ref SpanBufferWriter buffer)
        {
            if (!_serializers.TryGetValue(typeof(T), out var serializer))
                throw new Exception($"Don't know how to serialize type {nameof(T)}");

            serializer.Serialize(obj, ref buffer);
        }

        public void Serialize<T, TState>(T obj, TState state, ReadOnlySpanAction<byte, TState> callback, int stackSize = 512)
        {
            if (!_serializers.TryGetValue(typeof(T), out var serializer))
                throw new Exception($"Don't know how to serialize type {nameof(T)}");

            var buffer = new SpanBufferWriter(stackalloc byte[stackSize]);
            serializer.Serialize(obj, ref buffer);
            callback(buffer.Data, state);
        }

        public void Serialize<T>(T obj, StatelessSerializationCallback callback, int stackSize = 512)
        {
            if (!_serializers.TryGetValue(typeof(T), out var serializer))
                throw new Exception($"Don't know how to serialize type {typeof(T).Name}");

            var buffer = new SpanBufferWriter(stackalloc byte[stackSize]);
            serializer.Serialize(obj, ref buffer);
            callback(buffer.Data);
        }
        
        public T Deserialize<T>(ReadOnlySpan<byte> buffer)
        {
            if (!_serializers.TryGetValue(typeof(T), out var serializer))
                throw new Exception($"Don't know how to deserialize type {nameof(T)}");

            var bufferReader = new SpanBufferReader(buffer);
            return (T)serializer.Deserialize(ref bufferReader);
        }
        
        public T Deserialize<T>(ref SpanBufferReader bufferReader)
        {
            if (!_serializers.TryGetValue(typeof(T), out var serializer))
                throw new Exception($"Don't know how to deserialize type {nameof(T)}");

            return (T)serializer.Deserialize(ref bufferReader);
        }
    }
}
