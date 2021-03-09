using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BinaryRecords.Abstractions;
using BinaryRecords.Attributes;
using BinaryRecords.Extensions;
using BinaryRecords.Records;
using BinaryRecords.Util;
using Krypton.Buffers;

namespace BinaryRecords.Providers
{
    public static class RecordExpressionGeneratorProvider
    {
        public static readonly IReadOnlyList<ExpressionGeneratorProvider> Builtin = new[] {CreateBuiltinProvider()};

        private static readonly Dictionary<Type, RecordConstructionRecord> RecordConstructionRecords = new();
        
        private record RecordConstructionRecord(
            IReadOnlyList<(uint Key, PropertyInfo)> Properties,
            ConstructorInfo ConstructorInfo)
        {
            public bool UsesMemberInit => ConstructorInfo.GetParameters().Length == 0;
        }
        
        private static bool IsInterested(Type type, ITypingLibrary typingLibrary)
        {
            if (!type.IsRecord())
                return false;
            // Make sure all our properties are serializable
            var properties = type.GetProperties();
            foreach (var property in properties)
            {
                if (!property.HasPublicSetAndGet())
                    continue;
                if (!typingLibrary.IsTypeSerializable(property.PropertyType))
                    return false;
            }
            return true;
        }

        private static ConstructableTypeRecord GenerateConstructableTypeRecord(
            Type type, 
            ITypingLibrary typingLibrary)
        {
            var versioned = false;
            
            var heldKeys = new HashSet<uint>();
            var recordProperties = new List<(uint, PropertyInfo)>();
            var typeRecords = new List<(uint, TypeRecord)>();
            
            // We need to check if we use memberinit before hand so we know where to check for keys
            var constructor = type.GetConstructors()[0];
            
            // TODO: Make the memberinit stuff work with inheritance, right now I don't think this current code will do the trick
            var usesMemberInit = constructor.GetParameters().Length == 0;
            var parameterIndex = 0;
            
            var properties = type.GetProperties();
            foreach (var property in properties)
            {
                if (!property.HasPublicSetAndGet())
                    continue;
                
                uint keyId = 0;
                
                // We check for key differently depending if we use memberinit
                KeyAttribute[] keyAttributes;
                if (usesMemberInit)
                {
                    keyAttributes = property.GetCustomAttributes<KeyAttribute>().ToArray();
                }
                else
                {
                    var constructorParameter = constructor.GetParameters()[parameterIndex];
                    keyAttributes = constructorParameter.GetCustomAttributes<KeyAttribute>().ToArray();
                }
                
                // First parameter determines versioning.
                if (parameterIndex == 0)
                    versioned = keyAttributes.Length > 0;

                if (keyAttributes.Length == 0)
                {
                    if (versioned)
                        ThrowHelpers.ThrowInconsistentVersioning(type);
                    recordProperties.Add((0, property));
                }
                else if (keyAttributes.Length == 1)
                {
                    if (!versioned)
                        ThrowHelpers.ThrowInconsistentVersioning(type);
                    versioned = true;
                    keyId = keyAttributes[0].Index;
                    if (!heldKeys.Add(keyId))
                        throw new Exception($"Duplicate key value used on property: {property.PropertyType.FullName}, key: {keyId}");
                    recordProperties.Add((keyId, property));
                }
                else
                {
                    throw new Exception($"More than one key attribute on property: {property.PropertyType.FullName}");
                }

                var propertyTypeRecord = typingLibrary.GetTypeRecord(property.PropertyType);
                typeRecords.Add((keyId, propertyTypeRecord));

                parameterIndex++;
            }
            
            Debug.Assert(usesMemberInit || typeRecords.Count == constructor.GetParameters().Length);
            RecordConstructionRecords[type] = new RecordConstructionRecord(recordProperties, constructor);
            return new ConstructableTypeRecord(typeRecords, versioned);
        }

        private static Expression GenerateSerializeExpression(
            ITypingLibrary typingLibrary, 
            Type type, 
            Expression dataAccess, 
            Expression bufferAccess,
            AutoVersioning? autoVersioning)
        {
            var typeRecord = typingLibrary.GetTypeRecord(type) as ConstructableTypeRecord;
            if (typeRecord == null) throw new Exception();
            var typeGuid = TypeRecordGuidProvider.ComputeGuid(typeRecord);
            
            var blockBuilder = new ExpressionBlockBuilder();
            autoVersioning?.MarkVersioningStart(blockBuilder, bufferAccess);

            Expression? rentedHeaderArray = null;
            Expression? headerWriter = null;
            Expression? headerBookmark = null;
            int headerSize = 0;
            if (typeRecord.Versioned)
            {
                // Calculate header size
                headerSize = 16 + sizeof(uint) + (typeRecord.Members.Count * sizeof(uint) * 2);
                
                // Since Expressions don't support stackalloc, we need to pool an array here. Oh well.
                // Not the end of the world
                rentedHeaderArray = blockBuilder.CreateVariable<byte[]>();
                blockBuilder += Expression.Assign(rentedHeaderArray, ByteArrayPoolExpressions.Rent(headerSize));

                // Build the header writer
                headerWriter = blockBuilder.CreateVariable(typeof(SpanBufferWriter));
                blockBuilder += Expression.Assign(
                    headerWriter, 
                    Expression.Call(
                        SpanBufferWriterUtil.FixedFromArrayMethod, 
                        rentedHeaderArray,
                        Expression.Constant(headerSize)));
                
                // Write our type guid
                blockBuilder += BufferWriterExpressions.WriteGuid(
                    headerWriter, 
                    Expression.Constant(typeGuid));
                
                // Write the key count
                blockBuilder += BufferWriterExpressions.WriteUInt32(
                    headerWriter,
                    Expression.Constant((uint) typeRecord.Members.Count));
                
                // Reserve a bookmark on the buffer for our header
                headerBookmark = blockBuilder.CreateVariable<SpanBufferWriter.Bookmark>();
                blockBuilder += Expression.Assign(
                    headerBookmark,
                    BufferWriterExpressions.ReserveBookmark(bufferAccess, headerSize));
            }

            var endLabel = Expression.Label();

            // Null check
            blockBuilder += Expression.IfThen(
                Expression.Equal(dataAccess, Expression.Constant(null)),
                Expression.Block(
                        Expression.Call(
                            bufferAccess, 
                            typeof(SpanBufferWriter).GetMethod("WriteUInt8")!, 
                            Expression.Constant((byte)0)
                            ),
                        Expression.Return(endLabel)
                        )
                );
            
            blockBuilder += Expression.Call(
                bufferAccess, 
                typeof(SpanBufferWriter).GetMethod("WriteUInt8")!, 
                Expression.Constant((byte)1)
                );

            var recordConstruction = RecordConstructionRecords[type];
            var (blittables, nonBlittables) = GetRecordPropertyLayout(typingLibrary, recordConstruction);
            
            // Serialize each blittable property
            blockBuilder = blittables.Aggregate(blockBuilder, 
                (current, property) => 
                    current + typingLibrary.GenerateSerializeExpression(
                        property.Property.PropertyType, 
                        Expression.Property(dataAccess, property.Property), 
                        bufferAccess,
                        typeRecord.Versioned ? new AutoVersioning(property.Key, headerWriter!) : null));

            // Now we serialize each non-blittable property
            blockBuilder = nonBlittables.Aggregate(blockBuilder, 
                (current, property) => 
                    current + typingLibrary.GenerateSerializeExpression(
                        property.Property.PropertyType, 
                        Expression.Property(dataAccess, property.Property), 
                        bufferAccess,
                        typeRecord.Versioned ? new AutoVersioning(property.Key, headerWriter!) : null));
            
            blockBuilder += Expression.Label(endLabel);
            autoVersioning?.MarkVersioningEnd(blockBuilder, bufferAccess);
            
            // If we are versioned we need to write our header bookmark and clean up our rented array
            if (typeRecord.Versioned)
            {
                blockBuilder += Expression.Call(
                    BufferExtensions.WriteSizedArrayBookmarkMethod,
                    bufferAccess,
                    headerBookmark!,
                    rentedHeaderArray!,
                    Expression.Constant(headerSize));
                blockBuilder += ByteArrayPoolExpressions.Return(rentedHeaderArray!);
            }

            return blockBuilder;
        }
        
        private static Expression GenerateDeserializeExpression(
            ITypingLibrary typingLibrary,
            Type type,
            Expression bufferAccess)
        {
            var typeRecord = typingLibrary.GetTypeRecord(type) as ConstructableTypeRecord;
            if (typeRecord == null) throw new Exception();
            var constructionRecord = RecordConstructionRecords[type];
            throw new NotImplementedException();
        }

        private static Expression GenerateStandardDeserializeExpression(
            ITypingLibrary typingLibrary,
            RecordConstructionRecord constructionRecord,
            Expression bufferAccess)
        {
            throw new NotImplementedException();
        }
        
        private static Expression GenerateFastDeserializeExpression(
            ITypingLibrary typingLibrary,
            RecordConstructionRecord constructionRecord,
            Expression bufferAccess)
        {
            throw new NotImplementedException();
        }

        private static Expression GenerateNonConfidentDeserializeExpression(
            ITypingLibrary typingLibrary,
            RecordConstructionRecord constructionRecord,
            Expression bufferAccess)
        {
            throw new NotImplementedException();
        }
        
        /*
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

        private static BlockExpression GenerateStandardRecordDeserializer(RecordConstructionModel model,
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
        private static BlockExpression GenerateFastConfidentRecordDeserializer(
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
*/

        private static ((uint Key, PropertyInfo Property)[] Blittables, (uint Key, PropertyInfo Property)[] NonBlittables) GetRecordPropertyLayout(
            ITypingLibrary typingLibrary,
            RecordConstructionRecord constructionRecord)
        {
            var blittables = constructionRecord.Properties.Where(
                p => typingLibrary.IsTypeBlittable(p.Item2.PropertyType)).ToArray();
            var nonBlittables = constructionRecord.Properties.Except(blittables).ToArray();
            return (blittables, nonBlittables);
        }

        private static bool IsBlittableOptimizationCandidate(
            ITypingLibrary typingLibrary,
            RecordConstructionRecord model) =>
            // Not too sure what a good starting point for the optimization is, benchmarks results
            // have shown its reliably effective after 2 fields
            model.Properties.Count(p => typingLibrary.IsTypeBlittable(p.Item2.PropertyType)) > 2;

        private static ExpressionGeneratorProvider CreateBuiltinProvider()
        {
            return new(
                Name: "RecordProvider",
                Priority: ProviderPriority.Normal,
                IsInterested: IsInterested,
                GenerateSerializeExpression: GenerateSerializeExpression,
                GenerateDeserializeExpression: GenerateDeserializeExpression,
                GenerateTypeRecord: GenerateConstructableTypeRecord
            );
        }
    }
}