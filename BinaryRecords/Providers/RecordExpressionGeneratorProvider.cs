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

namespace BinaryRecords.Providers
{
    public static class RecordExpressionGeneratorProvider
    {
        public static IReadOnlyList<ExpressionGeneratorProvider> Builtin = new[] {CreateBuiltinProvider()};

        private static readonly Dictionary<Type, RecordConstructionRecord> _recordConstructionRecords = new();
        
        private record RecordConstructionRecord(
            Type Type, 
            IReadOnlyList<(uint Key, PropertyInfo)> Properties,
            ConstructorInfo ConstructorInfo,
            bool Versioned,
            ConstructableTypeRecord ConstructableTypeRecord)
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
            // Type alias are a requirement for cross-language types, if we aren't given one, we get stuck in .NET
            var aliasAttribute = type.GetCustomAttribute<AliasAttribute>();
            var alias = aliasAttribute?.Alias ?? type.FullName!;
            
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
                        throw new Exception($"Inconsistent type versioning on record: {type.FullName}");
                    recordProperties.Add((0, property));
                }
                else if (keyAttributes.Length == 1)
                {
                    if (!versioned)
                        throw new Exception($"Inconsistent type versioning on record: {type.FullName}");
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
            
            return new ConstructableTypeRecord(alias, typeRecords, versioned);
        }

        private static Expression GenerateSerializeExpression(
            ITypingLibrary typingLibrary, 
            Type type, 
            Expression dataAccess, 
            Expression bufferAccess)
        {
            throw new NotImplementedException();
        }

        private static Expression GenerateDeserializeExpression(
            ITypingLibrary typingLibrary,
            Type type,
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

        private static (PropertyInfo[] Blittables, PropertyInfo[] NonBlittables) GetRecordPropertyLayout(
            RecordConstructionRecord constructionRecord, 
            ITypingLibrary typingLibrary)
        {
            var blittables = constructionRecord.Properties.Where(p => typingLibrary.IsTypeBlittable(p.Item2.PropertyType)).ToArray();
            var nonBlittables = constructionRecord.Properties.Except(blittables).ToArray();
            return (blittables.Select(t => t.Item2).ToArray(), nonBlittables.Select(t => t.Item2).ToArray());
        }

        private static bool IsBlittableOptimizationCandidate(
            RecordConstructionRecord model, 
            ITypingLibrary typingLibrary) =>
            // Not too sure what a good starting point for the optimization is, benchmarks results
            // have shown its reliably effective after 2 fields
            model.Properties.Count(p => typingLibrary.IsTypeBlittable(p.Item2.PropertyType)) > 2;
        
        private static Expression GenerateConfidentDeserializeExpression(
            ITypingLibrary typingLibrary,
            Type type,
            Expression bufferAccess)
        {
            throw new NotImplementedException();
        }
        
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