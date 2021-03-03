using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BinaryRecords.Abstractions;
using BinaryRecords.Delegates;
using BinaryRecords.Expressions;
using BinaryRecords.Extensions;
using BinaryRecords.Models;
using BinaryRecords.Providers;
using Krypton.Buffers;

namespace BinaryRecords
{
    // TODO: A lot more code separation can be done here
    public class TypeSerializer
    {
        private static readonly Type BufferWriterType = typeof(SpanBufferWriter).MakeByRefType();

        private static readonly Type BufferReaderType = typeof(SpanBufferReader).MakeByRefType();
        
        private readonly Dictionary<Type, RecordSerializationInvocationModel> _invocationModels = new();
        
        private readonly ITypingLibrary _typingLibrary;
        
        internal TypeSerializer(ITypingLibrary typingLibrary)
        {
            _typingLibrary = typingLibrary;
        }
        
        private bool TryGetRecordInvocationModel(
            Type type, 
            [MaybeNullWhen(false)] out RecordSerializationInvocationModel serializer)
        {
            // Check if we can already handle this type
            if (_invocationModels.TryGetValue(type, out serializer))
                return true;

            // Try to get the construction model from the type library
            if (!_typingLibrary.TryGetRecordConstructionModel(type, out _))
                return false;
            
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
            
            var returnTarget = Expression.Label();

            // Null check
            blockBuilder += Expression.IfThen(
                Expression.Equal(objParameter, Expression.Constant(null)),
                Expression.Block(
                        Expression.Call(
                            bufferParameter, 
                            typeof(SpanBufferWriter).GetMethod("WriteUInt8")!, 
                            Expression.Constant((byte)0)
                            ),
                        Expression.Return(returnTarget)
                        )
                );
            
            blockBuilder += Expression.Call(
                bufferParameter, 
                typeof(SpanBufferWriter).GetMethod("WriteUInt8")!, 
                Expression.Constant((byte)1)
                );

            // Cast record type
            var recordInstance = blockBuilder.CreateVariable(type);
            blockBuilder += Expression.Assign(
                recordInstance,
                Expression.Convert(objParameter, type)
                );

            var model = _typingLibrary.GetRecordConstructionModel(type);
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
            
            blockBuilder += Expression.Label(returnTarget);
            
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
            var provider = _typingLibrary.GetInterestedGeneratorProvider(type);
            if (provider != null)
            {
                try
                {
                    return provider.GenerateSerializeExpression(this, type, dataAccess, bufferAccess);
                }
                catch (Exception)
                {
                    Console.WriteLine($"Failed to generate serialize expression for type: {type.FullName}");
                    throw;
                }
            }

            // If we don't have a provider for it, it is probably a type we construct
            // Check if we are dealing with a record type
            if (TryGetRecordInvocationModel(type, out var invocationModel))
                return Expression.Invoke(Expression.Constant(invocationModel.Serialize), dataAccess, bufferAccess);

            // We don't know what we are dealing with...
            throw new Exception($"Couldn't generate serializer for type: {type.Name}");
        }

        private DeserializeRecordDelegate GenerateDeserializeDelegate(Type type)
        {
            var model = _typingLibrary.GetRecordConstructionModel(type);
            var bufferAccess = Expression.Parameter(BufferReaderType, "buffer");

            var blockBuilder = new ExpressionBlockBuilder();
            var returnTarget = Expression.Label(typeof(object));
            
            // Null check
            blockBuilder += Expression.IfThen(
                Expression.Equal(
                    Expression.Call(
                        bufferAccess, 
                        typeof(SpanBufferReader).GetMethod("ReadUInt8")!
                        ), 
                    Expression.Constant((byte)0)
                    ),
                Expression.Return(returnTarget, Expression.Constant(null, typeof(object)))
            );
            
#if NET5_0
            var blockExpression = BitConverter.IsLittleEndian && IsBlittableOptimizationCandidate(model)
                ? GenerateFastRecordDeserializer(model, bufferAccess)
                : GenerateStandardRecordDeserializer(model, bufferAccess);
#else
            var blockExpression = GenerateStandardRecordDeserializer(model, bufferAccess);
#endif
            blockBuilder += Expression.Label(returnTarget, blockExpression);

            var lambda = Expression.Lambda<DeserializeRecordDelegate>(
                blockBuilder,
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
        private BlockExpression GenerateFastRecordDeserializer(
            RecordConstructionModel model,
            Expression bufferAccess)
        {
            var blockBuilder = new ExpressionBlockBuilder();

            var (blittables, nonBlittables) = GetRecordPropertyLayout(model);
            var blittableTypes = blittables.Select(p => p.PropertyType).ToArray();
            
            var blockType = BlittableBlockTypeProvider.GetBlittableBlock(blittableTypes);
            var readBlockMethod = typeof(BufferExtensions).GetMethod("ReadBlittableBytes")!.MakeGenericMethod(blockType);

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
            var provider = _typingLibrary.GetInterestedGeneratorProvider(type);
            if (provider != null)
            {
                try
                {
                    return provider.GenerateDeserializeExpression(this, type, bufferAccess);
                }
                catch (Exception)
                {
                    Console.WriteLine($"Failed to generate deserialize expression for type: {type.FullName}");
                    throw;
                }
            }

            // If we don't have a provider for it, it is probably a type we construct
            // Check if we are dealing with a record type
            if (TryGetRecordInvocationModel(type, out var invocationModel))
                return Expression.Convert(Expression.Invoke(Expression.Constant(invocationModel.Deserialize), bufferAccess), type);

            throw new Exception($"Couldn't generate deserializer for type: {type.Name}");
        }

        private (PropertyInfo[] Blittables, PropertyInfo[] NonBlittables) GetRecordPropertyLayout(RecordConstructionModel model)
        {
            var blittables = model.Properties.Where(p => _typingLibrary.IsTypeBlittable(p.PropertyType)).ToArray();
            var nonBlittables = model.Properties.Except(blittables).ToArray();
            return (blittables, nonBlittables);
        }

        private bool IsBlittableOptimizationCandidate(RecordConstructionModel model) =>
            // Not too sure what a good starting point for the optimization is, benchmarks results
            // have shown its reliably effective after 2 fields
            model.Properties.Count(p => _typingLibrary.IsTypeBlittable(p.PropertyType)) > 2;

        public bool IsTypeBlittable(Type type) =>
            _typingLibrary.IsTypeBlittable(type);

        public ExpressionGeneratorProvider? GetInterestedGeneratorProvider(Type type) =>
            _typingLibrary.GetInterestedGeneratorProvider(type);

        public void Serialize<T>(T obj, ref SpanBufferWriter buffer)
        {
            if (!TryGetRecordInvocationModel(typeof(T), out var serializer))
                throw new Exception($"Don't know how to serialize type {typeof(T).Name}");

            serializer.Serialize(obj, ref buffer);
        }

        public void Serialize<T, TState>(T obj, TState state, ReadOnlySpanAction<byte, TState> callback, int stackSize = 512)
        {
            if (!TryGetRecordInvocationModel(typeof(T), out var serializer))
                throw new Exception($"Don't know how to serialize type {typeof(T).Name}");

            var buffer = new SpanBufferWriter(stackalloc byte[stackSize]);
            serializer.Serialize(obj, ref buffer);
            callback(buffer.Data, state);
        }

        public void Serialize(Type type, object obj, StatelessSerializationCallback callback, int stackSize = 512)
        {
            if (!TryGetRecordInvocationModel(type, out var serializer))
                throw new Exception($"Don't know how to serialize type {type.Name}");

            var buffer = new SpanBufferWriter(stackalloc byte[stackSize]);
            serializer.Serialize(obj, ref buffer);
            callback(buffer.Data);
        }

        public void Serialize<T>(T obj, StatelessSerializationCallback callback, int stackSize = 512) => 
            Serialize(typeof(T), obj, callback, stackSize);

        public int Serialize(Type objType, object obj, Memory<byte> memory)
        {
            if (!TryGetRecordInvocationModel(objType, out var serializer))
                throw new Exception($"Don't know how to serialize type {objType.Name}");

            var buffer = new SpanBufferWriter(memory.Span, resize: false);
            try
            {
                serializer.Serialize(obj, ref buffer);
            }
            catch(OutOfSpaceException)
            {
                return -1;
            }
            return buffer.Size;
        }

        public int Serialize<T>(T obj, Memory<byte> memory) => Serialize(typeof(T), obj, memory);

        public byte[] Serialize(Type objType, object obj)
        {
            if (!TryGetRecordInvocationModel(objType, out var serializer))
                throw new Exception($"Don't know how to serialize type {objType.Name}");

            var buffer = new SpanBufferWriter(stackalloc byte[512]);
            serializer.Serialize(obj, ref buffer);
            return buffer.Data.ToArray();
        }

        public byte[] Serialize<T>(T obj) => Serialize(typeof(T), obj);
        
        public object Deserialize(Type type, ReadOnlySpan<byte> buffer)
        {
            if (!TryGetRecordInvocationModel(type, out var serializer))
                throw new Exception($"Don't know how to deserialize type {type.Name}");

            var bufferReader = new SpanBufferReader(buffer);
            return serializer.Deserialize(ref bufferReader);
        }

        public T Deserialize<T>(ReadOnlySpan<byte> buffer) => 
            (T) Deserialize(typeof(T), buffer);

        public object Deserialize(Type type, ref SpanBufferReader bufferReader)
        {
            if (!TryGetRecordInvocationModel(type, out var serializer))
                throw new Exception($"Don't know how to deserialize type {type.Name}");

            return serializer.Deserialize(ref bufferReader);
        }

        public T Deserialize<T>(ref SpanBufferReader bufferReader) => 
            (T) Deserialize(typeof(T), ref bufferReader);
    }
}
