using System;
using System.Collections.Generic;
using System.Reflection;

namespace BinaryRecords.Records
{
    public record RecordConstructionRecord(
        IReadOnlyList<(uint Key, PropertyInfo Property)> Properties,
        ConstructorInfo ConstructorInfo)
    {
        public Type Type => ConstructorInfo.DeclaringType!;
        public bool UsesMemberInit => ConstructorInfo.GetParameters().Length == 0;
    }
}