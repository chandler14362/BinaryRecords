using System;
using System.Reflection;

namespace BinaryRecords.Models
{
    public record RecordConstructionModel(Type type, PropertyInfo[] Properties, ConstructorInfo Constructor)
    {
        public bool UsesMemberInit => Constructor.GetParameters().Length == 0;
    }
}
