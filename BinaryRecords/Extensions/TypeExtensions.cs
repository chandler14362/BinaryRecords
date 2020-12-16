using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BinaryRecords.Extensions
{
    public static class TypeExtensions
    {
        private static IReadOnlyList<Type> _blittableTypes = new[]
        {
            typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), 
            typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double),
        };

        public static bool ImplementsGenericInterface(this Type type, Type genericType)
        {
            return type.GetInterfaces()
                .Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == genericType);
        }

        public static bool ImplementsInterface(this Type type, Type interfaceType)
        {
            return type.GetInterfaces().Contains(interfaceType);
        }

        public static bool IsOrImplementsGenericType(this Type type, Type genericType)
        {
            return IsGenericType(type, genericType) || ImplementsGenericInterface(type, genericType);
        }

        public static bool IsGenericType(this Type type, Type genericType)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == genericType;
        }

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
            return type.IsGenericType &&
                   (typeName.StartsWith("System.Tuple") || typeName.StartsWith("System.ValueTuple"));
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

        public static bool IsBlittable(this Type type)
        {
            // TODO: Support blittable structures
            return _blittableTypes.Contains(type);
        }

        public static int GetBlittableSize(this Type type)
        {
            // TODO: Support blittable structures
            if (type == typeof(byte) || type == typeof(sbyte)) return sizeof(byte);
            if (type == typeof(short) || type == typeof(ushort)) return sizeof(short);
            if (type == typeof(int) || type == typeof(uint)) return sizeof(int);
            if (type == typeof(long) || type == typeof(ulong)) return sizeof(long);
            if (type == typeof(float)) return sizeof(float);
            if (type == typeof(double)) return sizeof(double);
            return -1;
        }

        public static bool HasPublicSetAndGet(this PropertyInfo propertyInfo)
        {
            return (propertyInfo.SetMethod?.IsPublic ?? false) && (propertyInfo.GetMethod?.IsPublic ?? false);
        }
    }
}
