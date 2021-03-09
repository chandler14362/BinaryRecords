using System.Collections.Generic;
using BinaryRecords.Enums;
using BinaryRecords.Util;
using Krypton.Buffers;

namespace BinaryRecords.Records
{
    public abstract record TypeRecord(SerializableDataTypes SerializableType)
    {
        public void Hash(
            ref SpanBufferWriter bufferWriter, 
            ConstructableHashTracker constructableHashTracker)
        {
            bufferWriter.WriteUInt32((uint)SerializableType);
            DoHash(ref bufferWriter, constructableHashTracker);
        }

        protected abstract void DoHash(
            ref SpanBufferWriter bufferWriter, 
            ConstructableHashTracker constructableHashTracker);
    }
}