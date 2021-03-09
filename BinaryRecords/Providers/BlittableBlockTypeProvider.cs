#if NET5_0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using BinaryRecords.Extensions;

namespace BinaryRecords.Providers
{
    public static class BlittableBlockTypeProvider
    {
        private static readonly Dictionary<string, Type> BlittableBlockCache = new();

        private static readonly AssemblyBuilder AssemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("BinaryRecords.Dynamic.BlittableBlocks"), AssemblyBuilderAccess.Run);

        private static readonly ModuleBuilder ModuleBuilder = AssemblyBuilder.DefineDynamicModule("BlittableBlocks");
        
        private static string GenerateNameFromTypes(IEnumerable<Type> types) => 
            $"{string.Join("", types.Select(t => t.Name))}Block";

        private static Type GenerateBlittableBlockType(Type[] types, string? name=null)
        {
            if (!types.All(t => t.IsBlittablePrimitive())) throw new Exception();
            var typeSize = types.Sum(t => t.GetTypeValueSize());

            var typeName = name ?? GenerateNameFromTypes(types);
            var typeBuilder = ModuleBuilder.DefineType(typeName,
                TypeAttributes.Public | TypeAttributes.ExplicitLayout | TypeAttributes.Sealed |
                TypeAttributes.BeforeFieldInit,
                typeof(ValueType), typeSize);

            var fieldOffsetConstructor = typeof(FieldOffsetAttribute).GetConstructor(new[] {typeof(int)})!;

            var fieldOffset = 0;
            for (var i = 0; i < types.Length; i++)
            {
                var fieldName = $"Field{i}";
                var type = types[i];
                var fieldBuilder = typeBuilder.DefineField(fieldName, type, FieldAttributes.Public);
                fieldBuilder.SetCustomAttribute(
                    new CustomAttributeBuilder(fieldOffsetConstructor, new object[] {fieldOffset}));
                fieldOffset += type.GetTypeValueSize();
            }

            return typeBuilder.CreateType()!;
        }
        
        public static Type GetBlittableBlock(Type[] types)
        {
            var typeName = GenerateNameFromTypes(types);
            if (BlittableBlockCache.TryGetValue(typeName, out var generatedType)) 
                return generatedType;
            return BlittableBlockCache[typeName] = GenerateBlittableBlockType(types, typeName);
        }
    }
}
#endif
