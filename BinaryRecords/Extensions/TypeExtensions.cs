using System;
using System.Collections.Generic;
using System.Linq;

namespace BinaryRecords.Extensions
{
    public static class TypeExtensions
    {
        public static bool ImplementsGenericInterface(this Type type, Type genericType)
        {
            return type.GetInterfaces()
                .Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == genericType);
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
            return type.GetInterfaces()
                .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == genericType);
        }
    }
}
