#if NET5_0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using BinaryRecords.Models;

namespace BinaryRecords.Providers
{
    public static class FastRecordInstantiationMethodProvider
    {
        private static Dictionary<Type, MethodInfo> _cachedMethods = new();
        
        private static string FastInstantiationMethodName(Type type) => $"{type.Name}FastInstantiation";

        private static MethodInfo GenerateFastRecordConstructionMethod(RecordConstructionModel model, Type blockType, 
            PropertyInfo[] nonBlittableArguments)
        {
            var methodName = FastInstantiationMethodName(model.type);
            var passedSpanType = typeof(ReadOnlySpan<byte>);
            var allParameters = new List<Type> {passedSpanType};
            allParameters.AddRange(nonBlittableArguments.Select(p => p.PropertyType));
            var methodBuilder = new DynamicMethod(methodName, model.type, allParameters.ToArray(), true);
            var ilGenerator = methodBuilder.GetILGenerator();
            var blockLocal = ilGenerator.DeclareLocal(blockType.MakeByRefType());
            
            // Get a reference to the block in the span
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Call, 
                typeof(MemoryMarshal).GetMethod("AsRef", new [] {passedSpanType})
                    .MakeGenericMethod(blockType));
            ilGenerator.Emit(OpCodes.Stloc, blockLocal);
            
            var blittableIndex = 0;
            var nonBlittableIndex = 0;
            foreach (var property in model.Properties)
            {
                if (nonBlittableArguments.Contains(property))
                {
                    ilGenerator.Emit(OpCodes.Ldarg, 1 + nonBlittableIndex++);
                }
                else
                {
                    ilGenerator.Emit(OpCodes.Ldloc, blockLocal);
                    ilGenerator.Emit(OpCodes.Ldfld, blockType.GetField($"Field{blittableIndex++}"));
                }
            }
            
            ilGenerator.Emit(OpCodes.Newobj, model.Constructor);
            ilGenerator.Emit(OpCodes.Ret);
            return methodBuilder;
        }

        private static MethodInfo GenerateFastRecordMemberInitMethod(RecordConstructionModel model, Type blockType,
            PropertyInfo[] nonBlittableArguments)
        {
            var methodName = FastInstantiationMethodName(model.type);
            var passedSpanType = typeof(ReadOnlySpan<byte>);
            var allParameters = new List<Type> {passedSpanType};
            allParameters.AddRange(nonBlittableArguments.Select(p => p.PropertyType));
            var methodBuilder = new DynamicMethod(methodName, model.type, allParameters.ToArray(), true);
            var ilGenerator = methodBuilder.GetILGenerator();
            var blockLocal = ilGenerator.DeclareLocal(blockType.MakeByRefType());
            
            // Get a reference to the block in the span
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Call, 
                typeof(MemoryMarshal).GetMethod("AsRef", new [] {passedSpanType})
                    .MakeGenericMethod(blockType));
            ilGenerator.Emit(OpCodes.Stloc, blockLocal);
            
            // Construct a new instance of the record
            var recordConstructor = model.type.GetConstructor(Array.Empty<Type>());
            ilGenerator.Emit(OpCodes.Newobj, recordConstructor);

            // Now we can set all the fields of the record
            var blittableIndex = 0;
            var nonBlittableIndex = 0;
            foreach (var property in model.Properties)
            {
                // Duplicate the record at the top of the stack, this is useful for chaining field sets
                ilGenerator.Emit(OpCodes.Dup);
                
                // Load the field data on to the stack
                if (nonBlittableArguments.Contains(property))
                {
                    ilGenerator.Emit(OpCodes.Ldarg, 1 + nonBlittableIndex++);
                }
                else
                {
                    ilGenerator.Emit(OpCodes.Ldloc, blockLocal);
                    ilGenerator.Emit(OpCodes.Ldfld, blockType.GetField($"Field{blittableIndex++}"));
                }
                
                // Set the field we loaded
                ilGenerator.EmitCall(OpCodes.Call, property.GetSetMethod(), null);
            }
            
            // At this point our record should be at the top of the stack
            ilGenerator.Emit(OpCodes.Ret);
            return methodBuilder;
        }

        public static MethodInfo GetFastRecordInstantiationMethod(RecordConstructionModel model, Type blockType,
            PropertyInfo[] nonBlittableArguments)
        {
            if (_cachedMethods.TryGetValue(model.type, out var method)) return method;
            return _cachedMethods[model.type] = !model.UsesMemberInit
                ? GenerateFastRecordConstructionMethod(model, blockType, nonBlittableArguments)
                : GenerateFastRecordMemberInitMethod(model, blockType, nonBlittableArguments);
        }
    }
}
#endif
