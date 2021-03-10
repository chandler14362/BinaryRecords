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
                // Calculate header size.
                headerSize = 16 + sizeof(uint) + (typeRecord.Members.Count * sizeof(uint) * 2);
                
                // Since Expressions don't support stackalloc, we need to pool an array here. Oh well.
                // Not the end of the world
                rentedHeaderArray = blockBuilder.CreateVariable<byte[]>();
                blockBuilder += Expression.Assign(
                    rentedHeaderArray, 
                    ArrayPoolExpressions<byte>.Rent(Expression.Constant(headerSize)));

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
                        BufferWriterExpressions.WriteBool(bufferAccess, Expression.Constant(false)),
                        Expression.Return(endLabel)
                    )
                );

            blockBuilder += BufferWriterExpressions.WriteBool(bufferAccess, Expression.Constant(true));

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
                blockBuilder += ArrayPoolExpressions<byte>.Return(rentedHeaderArray!);
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
            return typeRecord.Versioned
                ? GenerateVersionedDeserializedExpression(typingLibrary, typeRecord, constructionRecord, bufferAccess)
                : GenerateSequentialDeserializeExpression(typingLibrary, constructionRecord, bufferAccess);
        }

        private static Expression GenerateVersionedDeserializedExpression(
            ITypingLibrary typingLibrary,
            ConstructableTypeRecord typeRecord,
            RecordConstructionRecord constructionRecord,
            Expression bufferAccess)
        {
            var typeGuid = TypeRecordGuidProvider.ComputeGuid(typeRecord);
            var blockBuilder = new ExpressionBlockBuilder();
                
            // Read in the guid
            var serializedGuid = blockBuilder.CreateVariable<Guid>();
            blockBuilder += Expression.Assign(
                serializedGuid,
                BufferReaderExpressions.ReadGuid(bufferAccess));

            var returnLabel = Expression.Label(constructionRecord.Type);

            // TODO: It's probably worth generating methods to handle the two different code paths here
            // Check confidence 
            blockBuilder += Expression.IfThenElse(
                Expression.Equal(serializedGuid, Expression.Constant(typeGuid)),
                Expression.Return(
                    returnLabel, 
                    GenerateConfidentDeserializeExpression(typingLibrary, constructionRecord, bufferAccess)),
                Expression.Return(
                    returnLabel,
                    GenerateNonConfidentDeserializeExpression(typingLibrary, constructionRecord, bufferAccess))
                );
            return blockBuilder += Expression.Label(returnLabel, Expression.Constant(null, constructionRecord.Type));
        }

        private static Expression GenerateConfidentDeserializeExpression(
            ITypingLibrary typingLibrary,
            RecordConstructionRecord constructionRecord,
            Expression bufferAccess)
        {
            var blockBuilder = new ExpressionBlockBuilder();
            // Calculate the size of the header without the guid so we can skip
            var remainingHeaderSize = sizeof(uint) + (constructionRecord.Properties.Count * sizeof(uint) * 2);
            blockBuilder += BufferReaderExpressions.SkipBytes(bufferAccess, Expression.Constant(remainingHeaderSize));
            return blockBuilder += GenerateSequentialDeserializeExpression(typingLibrary, constructionRecord, bufferAccess);
        }
        
        private static Expression GenerateNonConfidentDeserializeExpression(
            ITypingLibrary typingLibrary,
            RecordConstructionRecord constructionRecord,
            Expression bufferAccess)
        {
            var blockBuilder = new ExpressionBlockBuilder();

            // Create every possible property with default values
            var propertyVariables = constructionRecord.Properties.Select(
                p => blockBuilder.CreateVariable(p.Property.PropertyType, p.Property.Name)).ToArray();
            for (var i = 0; i < constructionRecord.Properties.Count; i++)
                blockBuilder += Expression.Assign(
                    propertyVariables[i],
                    Expression.Default(constructionRecord.Properties[i].Property.PropertyType));

            // Read in the key count
            var keyCount = blockBuilder.CreateVariable<uint>();
            blockBuilder += Expression.Assign(
                keyCount, 
                BufferReaderExpressions.ReadUInt32(bufferAccess));

            var keysAndSizesCount = blockBuilder.CreateVariable<int>();
            blockBuilder += Expression.Assign(
                keysAndSizesCount, 
                Expression.Convert(
                    Expression.Multiply(keyCount, Expression.Constant((uint)2)),
                    typeof(int))
                );
            
            // TODO: I really want to use stackalloc for this, but right now I have to use ArrayPool again.
            //       It might be worth rewriting this portion of the code using ILEmit but for now this works.
            // I want to do this for if some sort of streaming or sequencing is ever implemented in to the buffers
            var keysAndSizes = blockBuilder.CreateVariable<uint[]>();
            blockBuilder += Expression.Assign(
                keysAndSizes,
                ArrayPoolExpressions<uint>.Rent(keysAndSizesCount)
                );
            
            // Now read in the keysAndSizes and copy them over
            blockBuilder += Expression.Call(
                BufferExtensions.ReadAndCopyUInt32SliceMethod,
                bufferAccess,
                keysAndSizes,
                keysAndSizesCount);

            var endLabel = Expression.Label(constructionRecord.Type);
            
            // Null check
            blockBuilder += Expression.IfThen(
                Expression.Equal(
                    BufferReaderExpressions.ReadBool(bufferAccess),
                    Expression.Constant(false)
                ),
                Expression.Return(endLabel, Expression.Constant(null, constructionRecord.Type))
            );

            // Now we can loop through each key we hold
            var loopExit = Expression.Label();
            var keysAndSizesIndex = blockBuilder.CreateVariable<int>("keysAndSizesIndex");
            blockBuilder += Expression.Assign(keysAndSizesIndex, Expression.Default(typeof(int)));
            blockBuilder += Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(keysAndSizesIndex, keysAndSizesCount),
                    Expression.Block(
                        GenerateNonConfidentDataSwitchExpression(
                            typingLibrary, 
                            keysAndSizes,
                            keysAndSizesIndex,
                            propertyVariables, 
                            constructionRecord,
                            bufferAccess),
                        Expression.AddAssign(keysAndSizesIndex, Expression.Constant(2))),
                    Expression.Break(loopExit)
                    ),
                loopExit
            );
            
            // Now we can construct the type using all of the variables we have prepped
            var constructedRecord = blockBuilder.CreateVariable(constructionRecord.Type);
            Expression recordConstructor = !constructionRecord.UsesMemberInit
                ? Expression.New(
                    constructionRecord.ConstructorInfo, 
                    constructionRecord.Properties.Select((_, i) => propertyVariables[i]), 
                    constructionRecord.Properties.Select(p => p.Property)) 
                : Expression.MemberInit(
                    Expression.New(constructionRecord.ConstructorInfo),
                    constructionRecord.Properties.Select((p, i) => 
                        Expression.Bind(p.Property, propertyVariables[i])));
            blockBuilder += Expression.Assign(constructedRecord, recordConstructor);

            // Free our pooled array
            blockBuilder += ArrayPoolExpressions<uint>.Return(keysAndSizes);
            return blockBuilder += Expression.Label(endLabel, constructedRecord);
        }

        private static Expression GenerateNonConfidentDataSwitchExpression(
            ITypingLibrary typingLibrary,
            Expression keysAndSizes,
            Expression keysAndSizesIndex,
            ParameterExpression[] variables,
            RecordConstructionRecord constructionRecord,
            Expression bufferAccess)
        {
            var blockBuilder = new ExpressionBlockBuilder();
            var switchBreak = Expression.Label();
            
            // Load current key
            var currentKey = blockBuilder.CreateVariable<uint>();
            blockBuilder += Expression.Assign(
                currentKey, 
                Expression.ArrayIndex(keysAndSizes, keysAndSizesIndex));
            
            // Load current size
            var currentSize = blockBuilder.CreateVariable<uint>();
            blockBuilder += Expression.Assign(
                currentSize,
                Expression.ArrayIndex(
                    keysAndSizes, 
                    Expression.Add(keysAndSizesIndex, Expression.Constant(1))));
            
            // Now generate an if then for each possible key we can handle
            for (var i = 0; i < constructionRecord.Properties.Count; i++)
            {
                var applicableVariable = variables[i];
                var (neededKey, property) = constructionRecord.Properties[i];
                blockBuilder += Expression.IfThen(
                    Expression.Equal(currentKey, Expression.Constant(neededKey)),
                        Expression.Block(
                            Expression.Assign(
                                applicableVariable, 
                                typingLibrary.GenerateDeserializeExpression(property.PropertyType, bufferAccess)),
                            Expression.Goto(switchBreak)));
            }
            
            // If we got here it means it's a key we can't handle. Do a data skip
            blockBuilder += BufferReaderExpressions.SkipBytes(bufferAccess, Expression.Convert(currentSize, typeof(int)));
            return blockBuilder += Expression.Label(switchBreak); 
        }
        
        private static Expression GenerateSequentialDeserializeExpression(
            ITypingLibrary typingLibrary,
            RecordConstructionRecord constructionRecord,
            Expression bufferAccess)
        {
            var blockBuilder = new ExpressionBlockBuilder();
            var endLabel = Expression.Label(constructionRecord.Type);
            
            // Null check
            blockBuilder += Expression.IfThen(
                Expression.Equal(
                    BufferReaderExpressions.ReadBool(bufferAccess),
                    Expression.Constant(false)
                ),
                Expression.Return(endLabel, Expression.Constant(null, constructionRecord.Type))
            );
            
#if NET5_0
            var blockExpression = BitConverter.IsLittleEndian && IsBlittableOptimizationCandidate(typingLibrary, constructionRecord)
                ? GenerateFastRecordDeserializer(typingLibrary, constructionRecord, bufferAccess)
                : GenerateStandardRecordDeserializer(typingLibrary, constructionRecord, bufferAccess);
#else
            var blockExpression = GenerateStandardRecordDeserializer(typingLibrary, constructionRecord, bufferAccess);
#endif
            blockBuilder += Expression.Label(endLabel, blockExpression);
            return blockBuilder;
        }

        private static BlockExpression GenerateStandardRecordDeserializer(
            ITypingLibrary typingLibrary,
            RecordConstructionRecord constructionRecord,
            Expression bufferAccess)
        {
            var propertyLookup = new Dictionary<PropertyInfo, ParameterExpression>();
            var blockBuilder = new ExpressionBlockBuilder();

            var (blittable, nonBlittable) = GetRecordPropertyLayout(typingLibrary, constructionRecord);
            foreach (var (_, property) in blittable.Concat(nonBlittable))
            {
                var variable = propertyLookup[property] 
                    = blockBuilder.CreateVariable(property.PropertyType);
                blockBuilder += Expression.Assign(variable, 
                    typingLibrary.GenerateDeserializeExpression(property.PropertyType, bufferAccess));
            }
            
            return blockBuilder += !constructionRecord.UsesMemberInit
                ? Expression.New(
                    constructionRecord.ConstructorInfo, 
                    constructionRecord.Properties.Select(p => propertyLookup[p.Property]), 
                    constructionRecord.Properties.Select(p => p.Property)) 
                : Expression.MemberInit(
                    Expression.New(constructionRecord.ConstructorInfo),
                    constructionRecord.Properties.Select(p => Expression.Bind(p.Property, propertyLookup[p.Property])));
        }
        
#if NET5_0
        private static BlockExpression GenerateFastRecordDeserializer(
            ITypingLibrary typingLibrary,
            RecordConstructionRecord constructionRecord,
            Expression bufferAccess)
        {
            var blockBuilder = new ExpressionBlockBuilder();

            var (blittables, nonBlittables) = GetRecordPropertyLayout(typingLibrary, constructionRecord);
            var blittableTypes = blittables.Select(p => p.Property.PropertyType).ToArray();
            var nonBlittableProperties = nonBlittables.Select(p => p.Property).ToArray();
            
            var blockType = BlittableBlockTypeProvider.GetBlittableBlock(blittableTypes);
            var readBlockMethod = typeof(BufferExtensions).GetMethod("ReadBlittableBytes")!.MakeGenericMethod(blockType);

            var constructRecordMethod =
                FastRecordInstantiationMethodProvider.GetFastRecordInstantiationMethod(constructionRecord, blockType, nonBlittableProperties);

            // Deserialize non blittable properties in to arguments list
            var arguments = new List<Expression> {Expression.Call(readBlockMethod, bufferAccess)};

            // TODO: Call the MethodInfo backing our generated Delegates from within the IL so we can stop abusing the stackframe here.
            arguments.AddRange(nonBlittableProperties.Select(
                p => typingLibrary.GenerateDeserializeExpression(p.PropertyType, bufferAccess)));
            
            return blockBuilder += Expression.Call(constructRecordMethod, arguments);
        }
#endif
        
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