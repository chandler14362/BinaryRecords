using System;
using System.Collections.Generic;
using BinaryRecords.Enums;
using Krypton.Buffers;

namespace BinaryRecords.Records
{
    public record ConstructableTypeRecord(Type Type, IReadOnlyList<(uint Key, TypeRecord MemberType)> Members) 
        : TypeRecord(SerializableDataTypes.Constructable)
    {
        protected override void DoHash(ref SpanBufferWriter bufferWriter)
        {
            bufferWriter.WriteUTF8String(Type.FullName);
            foreach (var (key, memberType) in Members)
            {
                bufferWriter.WriteUInt32(key);
                memberType.Hash(ref bufferWriter);
            }
        }
    }
}