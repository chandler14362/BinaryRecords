using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace BinaryRecords
{
    public ref partial struct BinaryBufferReader
    {
        public ReadOnlySpan<uint> ReadUInt32Slice(int count)
        {
            const int isize = sizeof(uint);
            ThrowIfEndOfBuffer(isize * count);
            
            var x = MemoryMarshal.Cast<byte, uint>(_buffer[Offset..]);
            Offset += isize * count;

            if (BitConverter.IsLittleEndian)
                return x[..count];

            var flipped = new uint[count];
            for (var i = 0; i < count; i++)
                flipped[i] = BinaryPrimitives.ReverseEndianness(x[i]);

            return flipped;
        }
    }
}