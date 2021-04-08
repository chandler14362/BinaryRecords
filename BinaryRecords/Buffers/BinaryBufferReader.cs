using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using BinaryRecords.Exceptions;

namespace BinaryRecords
{
    public ref partial struct BinaryBufferReader
    {
        private ReadOnlySpan<byte> _buffer;

        public int Offset { get; private set; }
        
        public BinaryBufferReader(ReadOnlySpan<byte> buffer)
        {
            _buffer = buffer;
            Offset = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfEndOfBuffer(int neededSize)
        {
            if (Offset + neededSize > _buffer.Length)
                ThrowEndOfBuffer(_buffer.Length, Offset, neededSize);
        }
        
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowEndOfBuffer(int size, int offset, int neededSize) =>
            throw new EndOfBufferException(size, offset, neededSize);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            ThrowIfEndOfBuffer(count);
            var slice = _buffer.Slice(Offset, count);
            Offset += count;
            return slice;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            ThrowIfEndOfBuffer(sizeof(byte));
            return _buffer[Offset++];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadUInt8() => ReadByte();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public sbyte ReadInt8() => (sbyte)ReadByte();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBool() => ReadUInt8() == 1;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public short ReadInt16()
        {
            const int size = sizeof(short);
            ThrowIfEndOfBuffer(size);
            
            var x = BinaryPrimitives.ReadInt16LittleEndian(_buffer[Offset..]);
            Offset += size;
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadUInt16()
        {
            const int size = sizeof(ushort);
            ThrowIfEndOfBuffer(size);
            
            var x = BinaryPrimitives.ReadUInt16LittleEndian(_buffer[Offset..]);
            Offset += size;
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt32()
        {
            const int size = sizeof(int);
            ThrowIfEndOfBuffer(size);
            
            var x = BinaryPrimitives.ReadInt32LittleEndian(_buffer[Offset..]);
            Offset += size;
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt32()
        {
            const int size = sizeof(uint);
            ThrowIfEndOfBuffer(size);
            
            var x = BinaryPrimitives.ReadUInt32LittleEndian(_buffer[Offset..]);
            Offset += size;
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadInt64()
        {
            const int size = sizeof(long);
            ThrowIfEndOfBuffer(size);
            
            var x = BinaryPrimitives.ReadInt64LittleEndian(_buffer[Offset..]);
            Offset += size;
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadUInt64()
        {
            const int size = sizeof(ulong);
            ThrowIfEndOfBuffer(size);
            
            var x = BinaryPrimitives.ReadUInt64LittleEndian(_buffer[Offset..]);
            Offset += size;
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe float ReadSingle()
        {
            const int size = sizeof(float);
            ThrowIfEndOfBuffer(size);

#if NET5_0
            var x = BinaryPrimitives.ReadSingleLittleEndian(_buffer[Offset..]);
#else
            var uintValue = BinaryPrimitives.ReadUInt32LittleEndian(_buffer[Offset..]);
            var x = *(float*)&uintValue;
#endif
            Offset += size;
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe double ReadDouble()
        {
            const int size = sizeof(double);
            ThrowIfEndOfBuffer(size);

#if NET5_0
            var x = BinaryPrimitives.ReadDoubleLittleEndian(_buffer[Offset..]);
#else
            var ulongValue = BinaryPrimitives.ReadUInt64LittleEndian(_buffer[Offset..]);
            var x = *(double*)&ulongValue;
#endif
            Offset += size;
            return x;
        }

        public Guid ReadGuid()
        {
            const int size = 16;
            ThrowIfEndOfBuffer(size);
#if NETSTANDARD2_0
            var guid = new Guid(_buffer.Slice(Offset, size).ToArray());
#else
            var guid = new Guid(_buffer.Slice(Offset, size));
#endif
            Offset += size;    
            return guid;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ReadString(Encoding encoding)
        {
            var length = ReadUInt16();
            var bytes = ReadBytes(length);
#if NETSTANDARD2_0
            return encoding.GetString(bytes.ToArray());
#else
            return encoding.GetString(bytes);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ReadUTF8String() => ReadString(Encoding.UTF8);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ReadUTF16String() => ReadString(Encoding.Unicode);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SkipBytes(int count)
        {
            ThrowIfEndOfBuffer(count);
            Offset += count;
        }

        public ReadOnlySpan<byte> RemainingData => _buffer[Offset..];

        /// <summary>
        /// Gets the amount of remaining bytes in the <see cref="BinaryBufferReader"/>.
        /// </summary>
        public readonly int RemainingSize => _buffer.Length - Offset;
    }
}
