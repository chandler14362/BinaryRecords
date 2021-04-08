using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using BinaryRecords.Buffers;
using BinaryRecords.Exceptions;

namespace BinaryRecords
{
    public ref struct BinaryBufferWriter
    {
        public readonly struct Bookmark
        {
            public readonly int Offset;
            public readonly int Size;

            public Bookmark(int offset, int size)
            {
                Offset = offset;
                Size = size;
            }
        }

        private readonly bool _resize;
        
        private readonly IPoolingStrategy _poolingStrategy;

        private byte[]? _pooledBuffer;

        private Span<byte> _buffer;

        private int _offset;

        /// <summary>
        /// Creates a new BinaryBufferWriter that is based off an existing buffer
        /// </summary>
        /// <param name="buffer">The buffer</param>
        /// <param name="resize">If the buffer can resize</param>
        /// <param name="poolingStrategy">The pooling strategy used when resizing the buffer</param>
        public BinaryBufferWriter(Span<byte> buffer, bool resize = true, IPoolingStrategy? poolingStrategy = null)
        {
            _resize = resize;
            _poolingStrategy = poolingStrategy ?? DefaultPoolingStrategy.Instance;
            _pooledBuffer = null;
            _buffer = buffer;
            _offset = 0;
        }

        /// <summary>
        /// Creates a new BinaryBufferWriter that allocates its initial buffer from a pool
        /// </summary>
        /// <param name="size">The initial buffer size</param>
        /// <param name="resize">If the buffer can resize</param>
        /// <param name="poolingStrategy">The pooling strategy used when resizing the buffer</param>
        public BinaryBufferWriter(int size, bool resize = true, IPoolingStrategy? poolingStrategy = null)
        {
            _resize = resize;
            _poolingStrategy = poolingStrategy ?? DefaultPoolingStrategy.Instance;
            _pooledBuffer = _poolingStrategy.Resize(1, size);
            _buffer = _pooledBuffer.AsSpan();
            _offset = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Reserve(int length)
        {
            var neededLength = _offset + length;
            if (neededLength <= _buffer.Length)
                return;
            ResizeBuffer(neededLength);
        }

        private void ResizeBuffer(int neededLength)
        {
            // If we can't resize we need to let the user know we are out of space
            if (!_resize)
                throw new OutOfSpaceException(_buffer.Length, _offset, neededLength);

            var resized = _poolingStrategy.Resize(_buffer.Length, neededLength);
            _buffer.CopyTo(resized.AsSpan());
            if (_pooledBuffer != null)
                _poolingStrategy.Free(_pooledBuffer);
            _pooledBuffer = resized;
            _buffer = resized.AsSpan();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBool(bool x)
        {
            Reserve(1);
            _buffer[_offset++] = x ? (byte)1 : (byte)0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt8(sbyte x)
        {
            Reserve(1);
            _buffer[_offset++] = (byte)x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt8(byte x)
        {
            Reserve(1);
            _buffer[_offset++] = x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt16(ushort x)
        {
            const int size = sizeof(ushort);
            Reserve(size);
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer[_offset..], x);
            _offset += size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt32(int x)
        {
            const int size = sizeof(int);
            Reserve(size);
            BinaryPrimitives.WriteInt32LittleEndian(_buffer[_offset..], x);
            _offset += size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt32(uint x)
        {
            const int size = sizeof(uint);
            Reserve(size);
            BinaryPrimitives.WriteUInt32LittleEndian(_buffer[_offset..], x);
            _offset += size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt64(ulong x)
        {
            const int size = sizeof(ulong);
            Reserve(size);
            BinaryPrimitives.WriteUInt64LittleEndian(_buffer[_offset..], x);
            _offset += size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt64(long x)
        {
            const int size = sizeof(long);
            Reserve(size);
            BinaryPrimitives.WriteInt64LittleEndian(_buffer[_offset..], x);
            _offset += size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteSingle(float x)
        {
            const int size = sizeof(float);
            Reserve(size);
#if NET5_0
            BinaryPrimitives.WriteSingleLittleEndian(_buffer[_offset..], x);
#else
            BinaryPrimitives.WriteUInt32LittleEndian(_buffer[_offset..], *((uint*)&x));
#endif
            _offset += size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteDouble(double x)
        {
            const int size = sizeof(double);
            Reserve(size);
#if NET5_0
            BinaryPrimitives.WriteDoubleLittleEndian(_buffer[_offset..], x);
#else
            BinaryPrimitives.WriteUInt64LittleEndian(_buffer[_offset..], *((ulong*)&x));
#endif
            _offset += size;
        }

        public void WriteGuid(Guid guid)
        {
            const int size = 16;
            Reserve(16);
#if NETSTANDARD2_0
            guid.ToByteArray().AsSpan().CopyTo(_buffer.Slice(_offset, size));
#else
            _ = guid.TryWriteBytes(_buffer.Slice(_offset));
#endif
            _offset += size;
        }

        public void WriteString(string str, Encoding encoding)
        {
            var byteCount = encoding.GetByteCount(str);
            
            Reserve(byteCount + 2);
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer[_offset..], (ushort)byteCount);
            _offset += 2;
            
            var bytes = _buffer.Slice(_offset, byteCount);
#if NETSTANDARD2_0
            encoding.GetBytes(str).AsSpan().CopyTo(bytes);
#else
            encoding.GetBytes(str.AsSpan(), bytes);
#endif
            _offset += byteCount;
        }

        public void WriteUTF8String(string str)
            => WriteString(str, Encoding.UTF8);

        public void WriteUTF16String(string str)
            => WriteString(str, Encoding.Unicode);

        public void WriteBytes(ReadOnlySpan<byte> x)
        {
            Reserve(x.Length);
            x.CopyTo(_buffer[_offset..]);
            _offset += x.Length;
        }
        
        public Bookmark ReserveBookmark(int size)
        {
            Reserve(size);
            var bookmark = new Bookmark(_offset, size);
            _offset += size;
            return bookmark;
        }

        public void WriteBookmark<TState>(in Bookmark bookmark, TState state, SpanAction<byte, TState> output)
        {
            var slice = _buffer.Slice(bookmark.Offset, bookmark.Size);
            output(slice, state);
        }

        public void PadBytes(int n)
        {
            Reserve(n);
            _offset += n;
        }

        public void Dispose()
        {
            if (_pooledBuffer == null)
                return;

            _poolingStrategy.Free(_pooledBuffer);
            _pooledBuffer = null;
            _buffer = Span<byte>.Empty;
            _offset = 0;
        }

        public ReadOnlySpan<byte> Data => _buffer[.._offset];

        public int Size => _offset;

        public static implicit operator ReadOnlySpan<byte>(BinaryBufferWriter buffer) => buffer.Data;
    }
}