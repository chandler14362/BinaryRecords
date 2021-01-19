using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BinaryRecords.Extensions
{
    public static class TypeExtensions
    {
        public static bool ImplementsGenericInterface(this Type type, Type genericType) =>
            type.GetInterfaces().Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == genericType);

        public static bool ImplementsInterface(this Type type, Type interfaceType) => 
            type.GetInterfaces().Contains(interfaceType);

        public static bool IsOrImplementsGenericType(this Type type, Type genericType) => 
            IsGenericType(type, genericType) || ImplementsGenericInterface(type, genericType);

        public static bool IsGenericType(this Type type, Type genericType) => 
            type.IsGenericType && type.GetGenericTypeDefinition() == genericType;

        public static Type GetGenericInterface(this Type type, Type genericType)
        {
            // If the type is the generic interface we need to return that
            if (type.IsGenericType && type.GetGenericTypeDefinition() == genericType) return type;
            return type.GetInterfaces()
                .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == genericType);
        }
        
        public static bool IsTuple(this Type type)
        {
#if NETSTANDARD2_1
            return type.IsGenericType && type.ImplementsInterface(typeof(ITuple));
#else
            var typeName = type.FullName;
            return type.IsGenericType && (typeName.StartsWith("System.Tuple") || typeName.StartsWith("System.ValueTuple"));
#endif
        }

        public static bool IsRecord(this Type type)
        {
            // Check if we have an EqualityContract
            var equalityContract = type.GetProperty("EqualityContract", 
                BindingFlags.Instance | BindingFlags.NonPublic);

            // TODO: Check more property info to make sure it fully matches the compiled version
            return equalityContract is not null;
        }
        
        public static bool HasPublicSetAndGet(this PropertyInfo propertyInfo) => 
            (propertyInfo.SetMethod?.IsPublic ?? false) && (propertyInfo.GetMethod?.IsPublic ?? false);
    }
}
