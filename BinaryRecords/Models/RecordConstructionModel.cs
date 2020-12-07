#nullable enable

using System;
using System.Reflection;

namespace BinaryRecords.Models
{
    public record RecordConstructionModel(Type type, PropertyInfo[] Properties, ConstructorInfo? Constructor);
}
