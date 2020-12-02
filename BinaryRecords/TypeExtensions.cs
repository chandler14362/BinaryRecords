using System;
using System.Linq;

namespace BinaryRecords
{
    public static class TypeExtensions
    {
        public static bool ImplementsGenericInterface(this Type type, Type genericType)
        {
            return type.GetInterfaces()
                .Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == genericType);
        }
    }
}
