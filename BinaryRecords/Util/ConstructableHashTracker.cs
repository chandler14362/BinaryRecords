using System.Collections.Generic;
using BinaryRecords.Records;

namespace BinaryRecords.Util
{
    public sealed class ConstructableHashTracker 
        : Dictionary<ConstructableTypeRecord, HashSet<ConstructableTypeRecord>>
    {
    }
}