using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BinaryRecords.Abstractions;
using BinaryRecords.Enums;
using BinaryRecords.Expressions;
using BinaryRecords.Extensions;
using BinaryRecords.Records;
using BinaryRecords.Util;

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
            ITypingLibrary typingLibrary,
            Type type)
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
            Expression buffer,
            Expression data,
            VersionWriter? versioning = null)
        {
            var typeRecord = typingLibrary.GetTypeRecord(type) as ConstructableTypeRecord;
            if (typeRecord == null) throw new Exception();
            var typeGuid = ConstructableVersionGuidProvider.ComputeGuid(typeRecord, typingLibrary.BitSize);
            
            var blockBuilder = new ExpressionBlockBuilder();
            versioning?.Start(blockBuilder, buffer, typingLibrary.BitSize);

            Expression? rentedHeaderArray = null;
            Expression? headerWriter = null;
            Expression? headerBookmark = null;
            int headerSize = 0;
            if (typeRecord.Versioned)
            {
                if (typingLibrary.BitSize != BitSize.B32)
                    throw new NotImplementedException();
                
                // Calculate header size.
                headerSize = 
                    // compatibility plus padding
                    sizeof(uint) + 4 + 
                    // version hash
                    16 + 
                    // fieldcount plus padding
                    sizeof(uint) + 4 + 
                    // keys and sizes
                    (typeRecord.Members.Count * sizeof(uint) * 2);
                
                // Since Expressions don't support stackalloc, we need to pool an array here. Oh well.
                // Not the end of the world
                rentedHeaderArray = blockBuilder.CreateVariable<byte[]>();
                blockBuilder += Expression.Assign(
                    rentedHeaderArray, 
                    ArrayPoolExpressions<byte>.Rent(Expression.Constant(headerSize)));

                // Build the header writer
                headerWriter = blockBuilder.CreateVariable(typeof(BinaryBufferWriter));
                blockBuilder += Expression.Assign(
                    headerWriter, 
                    Expression.Call(
                        BinaryBufferWriterUtil.FixedFromArrayMethod, 
                        rentedHeaderArray,
                        Expression.Constant(headerSize)));
                
                // Write our compatibility 
                blockBuilder += BufferWriterExpressions.WriteUInt32(
                        headerWriter,
                        Expression.Constant((uint) typingLibrary.BitSize));
                blockBuilder += BufferWriterExpressions.PadBytes(headerWriter, Expression.Constant(4));
                
                // Write our type guid
                blockBuilder += BufferWriterExpressions.WriteGuid(
                    headerWriter, 
                    Expression.Constant(typeGuid));
                
                // Write the key count
                blockBuilder += BufferWriterExpressions.WriteUInt32(
                    headerWriter,
                    Expression.Constant((uint) typeRecord.Members.Count));
                blockBuilder += BufferWriterExpressions.PadBytes(headerWriter, Expression.Constant(4));
                
                // Reserve a bookmark on the buffer for our header
                headerBookmark = blockBuilder.CreateVariable<BinaryBufferWriter.Bookmark>();
                blockBuilder += Expression.Assign(
                    headerBookmark,
                    BufferWriterExpressions.ReserveBookmark(buffer, Expression.Constant(headerSize)));
            }

            var endLabel = Expression.Label();

            // Null check
            blockBuilder += Expression.IfThen(
                Expression.Equal(data, Expression.Constant(null)),
                Expression.Block(
                        BufferWriterExpressions.WriteBool(buffer, Expression.Constant(false)),
                        Expression.Return(endLabel)
                    )
                );

            blockBuilder += BufferWriterExpressions.WriteBool(buffer, Expression.Constant(true));

            var recordConstruction = RecordConstructionRecords[type];
            GetRecordPropertyLayout(
                typingLibrary, 
                recordConstruction, 
                out var blittables, 
                out var nonBlittables);
            
            // Serialize each blittable property
            blockBuilder = blittables.Aggregate(blockBuilder, 
                (current, property) => 
                    current + typingLibrary.GenerateSerializeExpression(
                        property.Property.PropertyType,
                        buffer,
                        Expression.Property(data, property.Property),
                        typeRecord.Versioned ? new VersionWriter(property.Key, headerWriter!) : null));

            // Now we serialize each non-blittable property
            blockBuilder = nonBlittables.Aggregate(blockBuilder, 
                (current, property) => 
                    current + typingLibrary.GenerateSerializeExpression(
                        property.Property.PropertyType, 
                        buffer,
                        Expression.Property(data, property.Property),
                        typeRecord.Versioned ? new VersionWriter(property.Key, headerWriter!) : null));
            
            blockBuilder += Expression.Label(endLabel);
            versioning?.Stop(blockBuilder, buffer, typingLibrary.BitSize);
            
            // If we are versioned we need to write our header bookmark and clean up our rented array
            if (typeRecord.Versioned)
            {
                blockBuilder += Expression.Call(
                    BufferExtensions.WriteSizedArrayBookmarkMethod,
                    buffer,
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
            Expression buffer)
        {
            var typeRecord = typingLibrary.GetTypeRecord(type) as ConstructableTypeRecord;
            if (typeRecord == null) throw new Exception();
            var constructionRecord = RecordConstructionRecords[type];
            return typeRecord.Versioned
                ? GenerateVersionedDeserializedExpression(typingLibrary, buffer, typeRecord, constructionRecord)
                : GenerateSequentialDeserializeExpression(typingLibrary, buffer, constructionRecord);
        }

        private static Expression GenerateVersionedDeserializedExpression(
            ITypingLibrary typingLibrary,
            Expression buffer,
            ConstructableTypeRecord typeRecord,
            RecordConstructionRecord constructionRecord)
        {
            var typeGuid = ConstructableVersionGuidProvider.ComputeGuid(typeRecord, typingLibrary.BitSize);
            var blockBuilder = new ExpressionBlockBuilder();
            
            // TODO: Actually check against the compatibility, for now skip
            blockBuilder += BufferReaderExpressions.SkipBytes(buffer, Expression.Constant(8));
            
            // Read in the guid
            var serializedGuid = blockBuilder.CreateVariable<Guid>();
            blockBuilder += Expression.Assign(
                serializedGuid,
                BufferReaderExpressions.ReadGuid(buffer));

            var returnLabel = Expression.Label(constructionRecord.Type);

            // TODO: It's probably worth generating methods to handle the two different code paths here
            // Check confidence 
            blockBuilder += Expression.IfThenElse(
                Expression.Equal(serializedGuid, Expression.Constant(typeGuid)),
                Expression.Return(
                    returnLabel, 
                    GenerateConfidentDeserializeExpression(typingLibrary, buffer, constructionRecord)),
                Expression.Return(
                    returnLabel,
                    GenerateNonConfidentDeserializeExpression(typingLibrary, buffer, constructionRecord))
                );
            return blockBuilder += Expression.Label(returnLabel, Expression.Constant(null, constructionRecord.Type));
        }

        private static Expression GenerateConfidentDeserializeExpression(
            ITypingLibrary typingLibrary,
            Expression buffer,
            RecordConstructionRecord constructionRecord)
        {
            var blockBuilder = new ExpressionBlockBuilder();
            // Calculate the size of the header without the guid so we can skip
            var remainingHeaderSize = sizeof(uint) + 4 + (constructionRecord.Properties.Count * sizeof(uint) * 2);
            blockBuilder += BufferReaderExpressions.SkipBytes(buffer, Expression.Constant(remainingHeaderSize));
            return blockBuilder += GenerateSequentialDeserializeExpression(typingLibrary, buffer, constructionRecord);
        }
        
        private static Expression GenerateNonConfidentDeserializeExpression(
            ITypingLibrary typingLibrary,
            Expression buffer,
            RecordConstructionRecord constructionRecord)
        {
            var blockBuilder = new ExpressionBlockBuilder();

            // Create every possible property with default values
            var propertyVariables = constructionRecord.Properties.Select(
                p => blockBuilder.CreateVariable(p.Property.PropertyType, p.Property.Name)).ToArray();
            for (var i = 0; i < constructionRecord.Properties.Count; i++)
                blockBuilder += Expression.Assign(
                    propertyVariables[i],
                    Expression.Default(constructionRecord.Properties[i].Property.PropertyType));

            // Read in the key count, this can be optimized in to a single call modifying our buffer code
            var keyCount = blockBuilder.CreateVariable<uint>();
            blockBuilder += Expression.Assign(
                keyCount, 
                BufferReaderExpressions.ReadUInt32(buffer));
            blockBuilder += BufferReaderExpressions.SkipBytes(buffer, Expression.Constant(4));

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
            // It also allows for better memory access
            var keysAndSizes = blockBuilder.CreateVariable<uint[]>();
            blockBuilder += Expression.Assign(
                keysAndSizes,
                ArrayPoolExpressions<uint>.Rent(keysAndSizesCount)
                );
            
            // Now read in the keysAndSizes and copy them over
            blockBuilder += Expression.Call(
                BufferExtensions.ReadAndCopyUInt32SliceMethod,
                buffer,
                keysAndSizes,
                keysAndSizesCount);

            var endLabel = Expression.Label(constructionRecord.Type);
            
            // Null check
            blockBuilder += Expression.IfThen(
                Expression.Equal(
                    BufferReaderExpressions.ReadBool(buffer),
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
                            buffer,
                            keysAndSizes,
                            keysAndSizesIndex,
                            propertyVariables, 
                            constructionRecord),
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
            Expression buffer,
            Expression keysAndSizes,
            Expression keysAndSizesIndex,
            ParameterExpression[] variables,
            RecordConstructionRecord constructionRecord)
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
                                typingLibrary.GenerateDeserializeExpression(property.PropertyType, buffer)),
                            Expression.Goto(switchBreak)));
            }
            
            // If we got here it means it's a key we can't handle. Do a data skip
            blockBuilder += BufferReaderExpressions.SkipBytes(buffer, Expression.Convert(currentSize, typeof(int)));
            return blockBuilder += Expression.Label(switchBreak); 
        }
        
        private static Expression GenerateSequentialDeserializeExpression(
            ITypingLibrary typingLibrary,
            Expression buffer,
            RecordConstructionRecord constructionRecord)
        {
            var blockBuilder = new ExpressionBlockBuilder();
            var endLabel = Expression.Label(constructionRecord.Type);
            
            // Null check
            blockBuilder += Expression.IfThen(
                Expression.Equal(
                    BufferReaderExpressions.ReadBool(buffer),
                    Expression.Constant(false)
                ),
                Expression.Return(endLabel, Expression.Constant(null, constructionRecord.Type))
            );
            
#if NET5_0
            var blockExpression = BitConverter.IsLittleEndian && IsBlittableOptimizationCandidate(typingLibrary, constructionRecord)
                ? GenerateFastRecordDeserializer(typingLibrary, buffer, constructionRecord)
                : GenerateStandardRecordDeserializer(typingLibrary, buffer, constructionRecord);
#else
            var blockExpression = GenerateStandardRecordDeserializer(typingLibrary, buffer, constructionRecord);
#endif
            blockBuilder += Expression.Label(endLabel, blockExpression);
            return blockBuilder;
        }

        private static BlockExpression GenerateStandardRecordDeserializer(
            ITypingLibrary typingLibrary,
            Expression buffer,
            RecordConstructionRecord constructionRecord)
        {
            var propertyLookup = new Dictionary<PropertyInfo, ParameterExpression>();
            var blockBuilder = new ExpressionBlockBuilder();

            GetRecordPropertyLayout(
                typingLibrary, 
                constructionRecord, 
                out var blittable, 
                out var nonBlittable);
            foreach (var (_, property) in blittable.Concat(nonBlittable))
            {
                var variable = propertyLookup[property] 
                    = blockBuilder.CreateVariable(property.PropertyType);
                blockBuilder += Expression.Assign(variable, 
                    typingLibrary.GenerateDeserializeExpression(property.PropertyType, buffer));
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
            Expression buffer,
            RecordConstructionRecord constructionRecord)
        {
            var blockBuilder = new ExpressionBlockBuilder();

            GetRecordPropertyLayout(
                typingLibrary, 
                constructionRecord, 
                out var blittables, 
                out var nonBlittables);
            var blittableTypes = blittables.Select(p => p.Property.PropertyType).ToArray();
            var nonBlittableProperties = nonBlittables.Select(p => p.Property).ToArray();
            
            var blockType = BlittableBlockTypeProvider.GetBlittableBlock(blittableTypes);
            var readBlockMethod = typeof(BufferExtensions).GetMethod("ReadBlittableBytes")!.MakeGenericMethod(blockType);

            var constructRecordMethod =
                FastRecordInstantiationMethodProvider.GetFastRecordInstantiationMethod(constructionRecord, blockType, nonBlittableProperties);

            // Deserialize non blittable properties in to arguments list
            var arguments = new List<Expression> {Expression.Call(readBlockMethod, buffer)};

            // TODO: Call the MethodInfo backing our generated Delegates from within the IL so we can stop abusing the stackframe here.
            foreach (var nonBlittable in nonBlittableProperties)
                arguments.Add(typingLibrary.GenerateDeserializeExpression(nonBlittable.PropertyType, buffer));
            return blockBuilder += Expression.Call(constructRecordMethod, arguments);
        }
#endif
        
        private static void GetRecordPropertyLayout(
            ITypingLibrary typingLibrary,
            RecordConstructionRecord constructionRecord,
            out (uint Key, PropertyInfo Property)[] blittables,
            out (uint Key, PropertyInfo Property)[] nonBlittables)
        {
            blittables = constructionRecord.Properties.Where(
                p => typingLibrary.IsTypeBlittable(p.Item2.PropertyType)).ToArray();
            nonBlittables = constructionRecord.Properties.Except(blittables).ToArray();
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