using System;
using System.Buffers.Binary;
using System.Reflection;

namespace BinaryRecords.Extensions
{
    public static class BufferExtensions
    {
        private static readonly Type BufferExtensionsType = typeof(BufferExtensions);
        
        public static void WriteUInt16Bookmark(
            ref this BinaryBufferWriter buffer,
            in BinaryBufferWriter.Bookmark bookmark, 
            ushort value) =>
            buffer.WriteBookmark(bookmark, value, BinaryPrimitives.WriteUInt16LittleEndian);
        
        public static readonly MethodInfo WriteSizedArrayBookmarkMethod =
            BufferExtensionsType.GetMethod("WriteSizedArrayBookmark")!;
        
        public static void WriteSizedArrayBookmark(
            ref this BinaryBufferWriter buffer,
            in BinaryBufferWriter.Bookmark bookmark,
            byte[] array,
            int size) =>
            buffer.WriteBookmark(bookmark, (Array: array, Size: size), (span, state) =>
                state.Array.AsSpan(0, size).CopyTo(span)
            );

        public static readonly MethodInfo ReadAndCopyUInt32SliceMethod =
            BufferExtensionsType.GetMethod("ReadAndCopyUInt32Slice")!;
        
        public static void ReadAndCopyUInt32Slice(
            ref this BinaryBufferReader buffer,
            uint[] array,
            int count) =>
            buffer.ReadUInt32Slice(count).CopyTo(array);

        public static unsafe ReadOnlySpan<byte> ReadBlittableBytes<T>(ref this BinaryBufferReader buffer) 
            where T : unmanaged =>
            buffer.ReadBytes(sizeof(T));

        /*
        public static void WriteDateTime(ref this BinaryBufferWriter buffer, DateTime dateTime)
        {
            buffer.WriteInt64(dateTime.Ticks);
            buffer.WriteUInt8((byte)dateTime.Kind);
        }

        public static DateTime ReadDateTime(ref this BinaryBufferReader buffer)
        {
            return new(buffer.ReadInt64(), (DateTimeKind)buffer.ReadUInt8());
        }

        public static void WriteDateTimeOffset(ref this BinaryBufferWriter buffer, DateTimeOffset dateTimeOffset)
        {
            buffer.WriteInt64(dateTimeOffset.Ticks);
            buffer.WriteInt64(dateTimeOffset.Offset.Ticks);
        }

        public static DateTimeOffset ReadDateTimeOffset(ref this BinaryBufferReader buffer)
        {
            return new(buffer.ReadInt64(), new TimeSpan(buffer.ReadInt64()));
        }
        */
    }
}
