using System;

namespace BinaryRecords.Delegates
{
    public delegate void StatelessSerializationCallback(ReadOnlySpan<byte> data);
}