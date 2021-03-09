using System.Collections.Generic;
using BinaryRecords.Enums;
using BinaryRecords.Util;
using Krypton.Buffers;

namespace BinaryRecords.Records
{
    public record ConstructableTypeRecord(IReadOnlyList<(uint Key, TypeRecord MemberType)> Members, bool Versioned) 
        : TypeRecord(SerializableDataTypes.Constructable)
    {
        protected override void DoHash(
            ref SpanBufferWriter bufferWriter, 
            ConstructableHashTracker constructableHashTracker)
        {
            // See if we have nested within ourselves somewhere
            if (!constructableHashTracker.TryGetValue(this, out var tracked))
                tracked = constructableHashTracker[this] = new HashSet<ConstructableTypeRecord>();
            
            // Dodge an infinite loop here. Just write in our member count and keys instead
            if (!tracked.Add(this))
            {
                bufferWriter.WriteUInt32((uint)Members.Count);
                foreach (var (key, _) in Members) 
                    bufferWriter.WriteUInt32(key);
                return;
            }

            foreach (var (key, memberType) in Members)
            {
                bufferWriter.WriteUInt32(key);
                memberType.Hash(ref bufferWriter, constructableHashTracker);
            }
        }
    }
}