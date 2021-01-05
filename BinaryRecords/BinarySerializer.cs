using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using BinaryRecords.Delegates;
using BinaryRecords.Expressions;
using BinaryRecords.Interfaces;
using BinaryRecords.Models;
using BinaryRecords.Providers;
using Krypton.Buffers;

namespace BinaryRecords
{
    public class BinarySerializer : ITypeLibrary
    {
        private delegate void SerializeRecordDelegate(Object obj, ref SpanBufferWriter buffer);

        private delegate object DeserializeRecordDelegate(ref SpanBufferReader bufferReader);

        public static readonly Type BufferWriterType =
            Type.GetType($"{typeof(SpanBufferWriter).FullName}&,Krypton.Buffers");

        public static readonly Type BufferReaderType =
            Type.GetType($"{typeof(SpanBufferReader).FullName}&,Krypton.Buffers");
        
        private record RecordSerializationPair(SerializeRecordDelegate Serialize, DeserializeRecordDelegate Deserialize);
        
        private Dictionary<Type, RecordSerializationPair> _serializers = new();

        private Dictionary<Type, ExpressionGeneratorProvider> _typeProviderCache = new();
        
        private Dictionary<Type, RecordConstructionModel> _constructionModels;

        private List<ExpressionGeneratorProvider> _generatorProviders;

        internal BinarySerializer(Dictionary<Type, RecordConstructionModel> constructionModels, 
            List<ExpressionGeneratorProvider> generatorProviders)
        {
            _constructionModels = new(constructionModels);
            _generatorProviders = new(generatorProviders);
            
            foreach (var type in _constructionModels.Keys)
                TryGetRecordSerializer(type, out _);
        }
        
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
            var blockBuilder = new ExpressionBlockBuilder();
            
            // Declare parameters
            var objParameter = Expression.Parameter(typeof(object), "obj");
            var bufferParameter = Expression.Parameter(BufferWriterType, "buffer");

            // Cast record type
            var recordInstance = blockBuilder.CreateVariable(type);
            blockBuilder += Expression.Assign(
                recordInstance,
                Expression.Convert(objParameter, type)
            );

            // Generate a serializer for each record property
            blockBuilder += _constructionModels[type].Properties
                .Select(property => 
                    GenerateTypeSerializer(property.PropertyType, 
                        Expression.Property(recordInstance, property), 
                        bufferParameter));

            // Compile and return
            var lambda = Expression.Lambda<SerializeRecordDelegate>(
                blockBuilder, 
                objParameter, bufferParameter
            );
            return lambda.Compile();
        }

        public Expression GenerateTypeSerializer(Type type, Expression dataAccess, Expression bufferAccess)
        {
            // Check if any providers are interested in the type
            var provider = GetProviderForType(type);
            if (provider != null) 
                return provider.GenerateSerializeExpression(this, type, dataAccess, bufferAccess);

            // If we don't have a provider for it, it is probably a type we construct
            // Check if we are dealing with a record type
            if (TryGetRecordSerializer(type, out var recordPair))
            {
                var serializerDelegate = recordPair.Serialize;
                return Expression.Invoke(Expression.Constant(serializerDelegate),
                    dataAccess,
                    bufferAccess);
            } 
            
            // We don't know what we are dealing with...
            throw new Exception($"Couldn't generate serializer for type: {type.Name}");
        }

        private DeserializeRecordDelegate GenerateDeserializeDelegate(Type type)
        {
            var model = _constructionModels[type];
            var bufferAccess = Expression.Parameter(BufferReaderType, "buffer");

            var blockExpression = model.Constructor != null
                ? GenerateConstructorDeserializer(model, bufferAccess)
                : GenerateMemberInitDeserializer(model, bufferAccess);
            
            var lambda = Expression.Lambda<DeserializeRecordDelegate>(
                blockExpression,
                bufferAccess
            );
            return lambda.Compile();
        }

        private BlockExpression GenerateConstructorDeserializer(RecordConstructionModel model, Expression bufferAccess)
        {
            var returnTarget = Expression.Label(model.type);
            var constructorCall = Expression.New(model.Constructor,
                model.Properties.Select(p => GenerateTypeDeserializer(p.PropertyType, bufferAccess)),
                model.Properties);
            var returnExpression = Expression.Return(returnTarget, constructorCall, model.type);
            var returnLabel = Expression.Label(returnTarget, constructorCall);
            return Expression.Block(returnExpression, returnLabel);
        }

        private BlockExpression GenerateMemberInitDeserializer(RecordConstructionModel model, Expression bufferAccess)
        {
            var returnTarget = Expression.Label(model.type);
            var memberInit = Expression.MemberInit(
                Expression.New(model.type.GetConstructor(Array.Empty<Type>())),
                model.Properties.Select(p =>
                    Expression.Bind(p, GenerateTypeDeserializer(p.PropertyType, bufferAccess))));
            var returnExpression = Expression.Return(returnTarget, memberInit, model.type);
            var returnLabel = Expression.Label(returnTarget, memberInit);
            return Expression.Block(returnExpression, returnLabel);
        }
        
        public Expression GenerateTypeDeserializer(Type type, Expression bufferAccess)
        {
            // Check if we have a provider willing to deserialize the type
            var provider = GetProviderForType(type);
            if (provider != null)
                return provider.GenerateDeserializeExpression(this, type, bufferAccess);
            
            // If we don't have a provider for it, it is probably a type we construct
            // Check if we are dealing with a record type
            if (TryGetRecordSerializer(type, out var recordPair))
            {
                return Expression.Convert(
                    Expression.Invoke(
                        Expression.Constant(recordPair.Deserialize), 
                        bufferAccess),
                    type);
            }
            
            throw new Exception($"Couldn't generate deserializer for type: {type.Name}");
        }

        public ExpressionGeneratorProvider GetProviderForType(Type type)
        {
            if (_typeProviderCache.TryGetValue(type, out var provider)) return provider;
            return _typeProviderCache[type] = _generatorProviders.GetInterestedProvider(type, this);
        }
        
        public void Serialize(Type objType, object obj, ref SpanBufferWriter buffer)
        {
            if (!_serializers.TryGetValue(objType, out var serializer))
                throw new Exception($"Don't know how to serialize type {objType.Name}");

            serializer.Serialize(obj, ref buffer);
        }

        public void Serialize<T>(T obj, ref SpanBufferWriter buffer) => Serialize(typeof(T), obj, ref buffer);

        public void Serialize<TState>(Type objType, object obj, TState state, ReadOnlySpanAction<byte, TState> callback, int stackSize = 512)
        {
            if (!_serializers.TryGetValue(objType, out var serializer))
                throw new Exception($"Don't know how to serialize type {objType.Name}");

            var buffer = new SpanBufferWriter(stackalloc byte[stackSize]);
            serializer.Serialize(obj, ref buffer);
            callback(buffer.Data, state);
        }

        public void Serialize<T, TState>(T obj, TState state, ReadOnlySpanAction<byte, TState> callback, int stackSize = 512) =>
            Serialize(typeof(T), obj, state, callback, stackSize);

        public void Serialize(Type objType, object obj, StatelessSerializationCallback callback, int stackSize = 512)
        {
            if (!_serializers.TryGetValue(objType, out var serializer))
                throw new Exception($"Don't know how to serialize type {objType.Name}");

            var buffer = new SpanBufferWriter(stackalloc byte[stackSize]);
            serializer.Serialize(obj, ref buffer);
            callback(buffer.Data);
        }

        public void Serialize<T>(T obj, StatelessSerializationCallback callback, int stackSize = 512) => 
            Serialize(typeof(T), obj, callback, stackSize);

        public int Serialize(Type objType, object obj, Memory<byte> memory)
        {
            var buffer = new SpanBufferWriter(memory.Span, resize: false);
            Serialize(obj, ref buffer);
            return buffer.Size;
        }

        public int Serialize<T>(T obj, Memory<byte> memory) => Serialize(typeof(T), obj, memory);

        public byte[] Serialize(Type objType, object obj)
        {
            var buffer = new SpanBufferWriter(stackalloc byte[512]);
            Serialize(obj, ref buffer);
            return buffer.Data.ToArray();
        }

        public byte[] Serialize<T>(T obj) => Serialize(typeof(T), obj);
        
        public object Deserialize(Type type, ReadOnlySpan<byte> buffer)
        {
            if (!_serializers.TryGetValue(type, out var serializer))
                throw new Exception($"Don't know how to deserialize type {type.Name}");

            var bufferReader = new SpanBufferReader(buffer);
            return serializer.Deserialize(ref bufferReader);
        }

        public T Deserialize<T>(ReadOnlySpan<byte> buffer) => (T) Deserialize(typeof(T), buffer);

        public object Deserialize(Type type, ref SpanBufferReader bufferReader)
        {
            if (!_serializers.TryGetValue(type, out var serializer))
                throw new Exception($"Don't know how to deserialize type {type.Name}");

            return serializer.Deserialize(ref bufferReader);
        }

        public T Deserialize<T>(ref SpanBufferReader bufferReader) => (T) Deserialize(typeof(T), ref bufferReader);
        
        public bool IsTypeSerializable(Type type)
        {
            return GetProviderForType(type) != null || _constructionModels.ContainsKey(type);
        }

        public IList<Type> GetConstructableTypes()
        {
            return _constructionModels.Keys.ToList();
        }
    }
}
