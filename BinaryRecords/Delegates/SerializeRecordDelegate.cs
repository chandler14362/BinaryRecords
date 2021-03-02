using System;
using Krypton.Buffers;

namespace BinaryRecords.Delegates
{
    internal delegate void SerializeRecordDelegate(Object obj, ref SpanBufferWriter buffer);
}
