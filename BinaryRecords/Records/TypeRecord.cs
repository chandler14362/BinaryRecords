using BinaryRecords.Enums;
using Krypton.Buffers;

namespace BinaryRecords.Records
{
    public abstract record TypeRecord(SerializableDataTypes SerializableType)
    {
        public void Hash(ref SpanBufferWriter bufferWriter)
        {
            bufferWriter.WriteUInt32((uint)SerializableType);
            DoHash(ref bufferWriter);
        }

        protected abstract void DoHash(ref SpanBufferWriter bufferWriter);
    }
}