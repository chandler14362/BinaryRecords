using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BinaryRecords.Delegates;
using BinaryRecords.Expressions;
using BinaryRecords.Extensions;
using BinaryRecords.Interfaces;
using BinaryRecords.Models;
using BinaryRecords.Providers;
using Krypton.Buffers;

namespace BinaryRecords
{
    public class BinarySerializer : ITypeLibrary
    {
        public static readonly Type BufferWriterType = typeof(SpanBufferWriter).MakeByRefType();

        public static readonly Type BufferReaderType = typeof(SpanBufferReader).MakeByRefType();
        
        private readonly Dictionary<Type, RecordSerializationInvocationModel> _invocationModels = new();

        private readonly Dictionary<Type, ExpressionGeneratorProvider> _typeProviderCache = new();
        
        private readonly Dictionary<Type, RecordConstructionModel> _constructionModels;

        private readonly List<ExpressionGeneratorProvider> _generatorProviders;

        internal BinarySerializer(Dictionary<Type, RecordConstructionModel> constructionModels, 
            List<ExpressionGeneratorProvider> generatorProviders)
        {
            _constructionModels = new(constructionModels);
            _generatorProviders = new(generatorProviders);
            
            foreach (var type in _constructionModels.Keys)
                TryGetRecordInvocationModel(type, out _);
        }
        
        private bool TryGetRecordInvocationModel(Type type, out RecordSerializationInvocationModel serializer)
        {
            // Ensure the type is one of our record types
            if (!_constructionModels.ContainsKey(type))
            {
                serializer = null;
                return false;
            }
            
            // Check if we already handle this type
            if (_invocationModels.TryGetValue(type, out serializer))
                return true;

            // Create new serializers
            _invocationModels[type] = serializer = 
                new (GenerateSerializeDelegate(type), GenerateDeserializeDelegate(type));
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

            var model = _constructionModels[type];

            var (blittables, nonBlittables) = GetRecordPropertyLayout(model);
            
            blockBuilder = blittables.Aggregate(blockBuilder, 
                (current, property) => 
                    current + GenerateTypeSerializer(
                        property.PropertyType, 
                        Expression.Property(recordInstance, property), 
                        bufferParameter));

            // Now we serialize each non-blittable property
            blockBuilder = nonBlittables.Aggregate(blockBuilder, 
                (current, property) => 
                    current + GenerateTypeSerializer(
                        property.PropertyType, 
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
            if (TryGetRecordInvocationModel(type, out var invocationModel))
                return Expression.Invoke(Expression.Constant(invocationModel.Serialize), dataAccess, bufferAccess);

            // We don't know what we are dealing with...
            throw new Exception($"Couldn't generate serializer for type: {type.Name}");
        }

        private DeserializeRecordDelegate GenerateDeserializeDelegate(Type type)
        {
            var model = _constructionModels[type];
            var bufferAccess = Expression.Parameter(BufferReaderType, "buffer");

#if NET5_0
            var blockExpression = BitConverter.IsLittleEndian && RecordHasBlittableProperties(model)
                ? GenerateFastRecordDeserializer(model, bufferAccess)
                : GenerateStandardRecordDeserializer(model, bufferAccess);
#else
            var blockExpression = GenerateStandardRecordDeserializer(model, bufferAccess);
#endif

            var lambda = Expression.Lambda<DeserializeRecordDelegate>(
                blockExpression,
                bufferAccess
            );
            return lambda.Compile();
        }

        private BlockExpression GenerateStandardRecordDeserializer(RecordConstructionModel model,
            Expression bufferAccess)
        {
            var propertyLookup = new Dictionary<PropertyInfo, ParameterExpression>();
            var blockBuilder = new ExpressionBlockBuilder();

            var (blittable, nonBlittable) = GetRecordPropertyLayout(model);
            foreach (var property in blittable.Concat(nonBlittable))
            {
                var variable = propertyLookup[property] 
                    = blockBuilder.CreateVariable(property.PropertyType);
                blockBuilder += Expression.Assign(variable, 
                    GenerateTypeDeserializer(property.PropertyType, bufferAccess));
            }
        
            var returnTarget = Expression.Label(model.type);
            Expression result = !model.UsesMemberInit
                ? Expression.New(model.Constructor, 
                    model.Properties.Select(p => propertyLookup[p]), model.Properties) 
                : Expression.MemberInit(Expression.New(model.Constructor),
                    model.Properties.Select(p => Expression.Bind(p, propertyLookup[p])));
            blockBuilder += Expression.Label(returnTarget, result);
            return blockBuilder;
        }

#if NET5_0
        private BlockExpression GenerateFastRecordDeserializer(RecordConstructionModel model,
            Expression bufferAccess)
        {
            var blockBuilder = new ExpressionBlockBuilder();

            var (blittables, nonBlittables) = GetRecordPropertyLayout(model);
            var blittableTypes = blittables.Select(p => p.PropertyType).ToArray();
            
            var blockType = BlittableBlockTypeProvider.GetBlittableBlock(blittableTypes);
            var readBlockMethod = typeof(BufferExtensions).GetMethod("ReadBlittableBytes").MakeGenericMethod(blockType);

            var constructRecordMethod =
                FastRecordInstantiationMethodProvider.GetFastRecordInstantiationMethod(model, blockType, nonBlittables);

            // Deserialize non blittable properties in to arguments list
            var arguments = new List<Expression> {Expression.Call(readBlockMethod, bufferAccess)};
            var nonBlittableTypes = nonBlittables.Select(p => p.PropertyType);
            arguments.AddRange(nonBlittableTypes.Select(t => GenerateTypeDeserializer(t, bufferAccess)));
           
            var returnTarget = Expression.Label(model.type);
            return blockBuilder += Expression.Label(returnTarget, Expression.Call(constructRecordMethod, arguments));
        }
#endif
        
        public Expression GenerateTypeDeserializer(Type type, Expression bufferAccess)
        {
            // Check if we have a provider willing to deserialize the type
            var provider = GetProviderForType(type);
            if (provider != null)
                return provider.GenerateDeserializeExpression(this, type, bufferAccess);
            
            // If we don't have a provider for it, it is probably a type we construct
            // Check if we are dealing with a record type
            if (TryGetRecordInvocationModel(type, out var invocationModel))
                return Expression.Convert(Expression.Invoke(Expression.Constant(invocationModel.Deserialize), bufferAccess), type);

            throw new Exception($"Couldn't generate deserializer for type: {type.Name}");
        }

        public ExpressionGeneratorProvider GetProviderForType(Type type)
        {
            if (_typeProviderCache.TryGetValue(type, out var provider)) return provider;

            if (!_generatorProviders.TryGetInterestedProvider(type, this, out provider))
                throw new Exception($"No provider's interested in type: {type.FullName}");

            return _typeProviderCache[type] = provider;
        }

        public void Serialize<T>(T obj, ref SpanBufferWriter buffer) where T: class
        {
            if (!_invocationModels.TryGetValue(typeof(T), out var serializer))
                throw new Exception($"Don't know how to serialize type {typeof(T).Name}");

            serializer.Serialize(obj, ref buffer);
        }

        public void Serialize<T, TState>(T obj, TState state, ReadOnlySpanAction<byte, TState> callback, 
            int stackSize = 512) where T: class
        {
            if (!_invocationModels.TryGetValue(typeof(T), out var serializer))
                throw new Exception($"Don't know how to serialize type {typeof(T).Name}");

            var buffer = new SpanBufferWriter(stackalloc byte[stackSize]);
            serializer.Serialize(obj, ref buffer);
            callback(buffer.Data, state);
        }

        public void Serialize<T>(T obj, StatelessSerializationCallback callback, int stackSize = 512) where T: class
        {
            if (!_invocationModels.TryGetValue(typeof(T), out var serializer))
                throw new Exception($"Don't know how to serialize type {typeof(T).Name}");

            var buffer = new SpanBufferWriter(stackalloc byte[stackSize]);
            serializer.Serialize(obj, ref buffer);
            callback(buffer.Data);
        }

        public int Serialize<T>(T obj, Memory<byte> memory) where T: class
        {
            var buffer = new SpanBufferWriter(memory.Span, resize: false);
            Serialize(obj, ref buffer);
            return buffer.Size;
        }

        public byte[] Serialize<T>(T obj) where T: class
        {
            var buffer = new SpanBufferWriter(stackalloc byte[512]);
            Serialize(obj, ref buffer);
            return buffer.Data.ToArray();
        }
        
        public object Deserialize(Type type, ReadOnlySpan<byte> buffer)
        {
            if (!_invocationModels.TryGetValue(type, out var serializer))
                throw new Exception($"Don't know how to deserialize type {type.Name}");

            var bufferReader = new SpanBufferReader(buffer);
            return serializer.Deserialize(ref bufferReader);
        }

        public T Deserialize<T>(ReadOnlySpan<byte> buffer) where T: class => (T) Deserialize(typeof(T), buffer);

        public object Deserialize(Type type, ref SpanBufferReader bufferReader)
        {
            if (!_invocationModels.TryGetValue(type, out var serializer))
                throw new Exception($"Don't know how to deserialize type {type.Name}");

            return serializer.Deserialize(ref bufferReader);
        }

        public T Deserialize<T>(ref SpanBufferReader bufferReader) where T: class => 
            (T) Deserialize(typeof(T), ref bufferReader);
        
        public bool IsTypeSerializable(Type type) => 
            GetProviderForType(type) != null || _constructionModels.ContainsKey(type);

        public bool IsTypeBlittable(Type type) => 
            _generatorProviders.TryGetInterestedProvider(type, this, out var provider) && 
            PrimitiveExpressionGeneratorProviders.IsBlittable(provider);

        private (PropertyInfo[] Blittables, PropertyInfo[] NonBlittables) GetRecordPropertyLayout(RecordConstructionModel model)
        {
            var blittables = model.Properties.Where(p => IsTypeBlittable(p.PropertyType)).ToArray();
            var nonBlittables = model.Properties.Except(blittables).ToArray();
            return (blittables, nonBlittables);
        }
        
        private bool RecordHasBlittableProperties(RecordConstructionModel model) => 
            model.Properties.Any(p => IsTypeBlittable(p.PropertyType));
        
        public RecordConstructionModel? GetRecordConstructionModel(Type type) => 
            _constructionModels.TryGetValue(type, out var model) ? model : null;
        
        public IList<RecordConstructionModel> GetRecordConstructionModels() => _constructionModels.Values.ToList();
    }
}
