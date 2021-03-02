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
        private static Dictionary<string, Type> _blittableBlockCache = new();

        private static AssemblyBuilder _assemblyBuilder;

        private static ModuleBuilder _moduleBuilder;

        static BlittableBlockTypeProvider()
        {
            _assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("BinaryRecords.Dynamic.BlittableBlocks"), AssemblyBuilderAccess.Run);
            _moduleBuilder = _assemblyBuilder.DefineDynamicModule("BlittableBlocks");
        }

        private static string GenerateNameFromTypes(IEnumerable<Type> types) => 
            $"{string.Join("", types.Select(t => t.Name))}Block";

        private static Type GenerateBlittableBlockType(Type[] types, string name=null)
        {
            if (!types.All(t => t.IsBlittablePrimitive())) throw new Exception();
            var typeSize = types.Sum(t => t.GetTypeValueSize());

            var typeName = name ?? GenerateNameFromTypes(types);
            var typeBuilder = _moduleBuilder.DefineType(typeName,
                TypeAttributes.Public | TypeAttributes.ExplicitLayout | TypeAttributes.Sealed |
                TypeAttributes.BeforeFieldInit,
                typeof(ValueType), typeSize);

            var fieldOffsetConstructor = typeof(FieldOffsetAttribute).GetConstructor(new[] {typeof(int)});

            var fieldOffset = 0;
            for (var i = 0; i < types.Length; i++)
            {
                var fieldName = $"Field{i}";
                var type = types[i];
                var fieldBuilder = typeBuilder.DefineField(fieldName, type, FieldAttributes.Public);
                fieldBuilder.SetCustomAttribute(new CustomAttributeBuilder(fieldOffsetConstructor, 
                    new object[] {fieldOffset}));
                fieldOffset += type.GetTypeValueSize();
            }

            return typeBuilder.CreateType();
        }
        
        public static Type GetBlittableBlock(Type[] types)
        {
            var typeName = GenerateNameFromTypes(types);
            if (_blittableBlockCache.TryGetValue(typeName, out var generatedType)) return generatedType;
            return _blittableBlockCache[typeName] = GenerateBlittableBlockType(types, typeName);
        }
    }
}
#endif
