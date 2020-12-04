using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using BinaryRecords.Delegates;
using BinaryRecords.Models;
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
                stackFrame.CreateVariable(type),
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
            var provider = _generatorProvider.FirstOrDefault(provider => provider.IsInterested(type));
            if (provider != null)
                return provider.GenerateSerializeExpression(this, type, dataAccess, stackFrame);

            // If we don't have a provider for it, it is probably a type we construct
            // Check if we are dealing with a record type
            if (TryGetRecordSerializer(type, out var recordPair))
            {
                var serializerDelegate = recordPair.Serialize;
                return Expression.Invoke(Expression.Constant(serializerDelegate),
                    dataAccess,
                    stackFrame.GetParameter("buffer"));
            } 
            
            // We don't know what we are dealing with...
            throw new Exception($"Couldn't generate serializer for type: {type.Name}");
        }

        private DeserializeRecordDelegate GenerateDeserializeDelegate(Type type)
        {
            var model = _constructionModels[type];
            var stackFrame = new StackFrame();
            stackFrame.CreateParameter(BufferReaderType, "buffer");
            
            var blockExpression = model.Constructor != null
                ? GenerateConstructorDeserializer(model, stackFrame)
                : GenerateMemberInitDeserializer(model, stackFrame);
            
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

        private BlockExpression GenerateMemberInitDeserializer(RecordConstructionModel model, StackFrame stackFrame)
        {
            var returnTarget = Expression.Label(model.type);
            var memberInit = Expression.MemberInit(
                Expression.New(model.type.GetConstructor(Array.Empty<Type>())),
                model.Properties.Select(p =>
                    Expression.Bind(p, GenerateTypeDeserializer(p.PropertyType, stackFrame))));
            var returnExpression = Expression.Return(returnTarget, memberInit, model.type);
            var returnLabel = Expression.Label(returnTarget, memberInit);
            return Expression.Block(returnExpression, returnLabel);
        }
        
        public Expression GenerateTypeDeserializer(Type type, StackFrame stackFrame)
        {
            // Check if we have a provider willing to deserialize the type
            var provider = _generatorProvider.FirstOrDefault(provider => provider.IsInterested(type));
            if (provider != null)
                return provider.GenerateDeserializeExpression(this, type, stackFrame);
            
            // If we don't have a provider for it, it is probably a type we construct
            // Check if we are dealing with a record type
            if (TryGetRecordSerializer(type, out var recordPair))
            {
                return Expression.Convert(
                    Expression.Invoke(
                        Expression.Constant(recordPair.Deserialize), 
                        stackFrame.GetParameter("buffer")),
                    type);
            }
            
            throw new Exception($"Couldn't generate deserializer for type: {type.Name}");
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
