using System;
using System.Buffers.Binary;
using Krypton.Buffers;

namespace BinaryRecords.Extensions
{
    public static class BufferExtensions
    {
        public static void WriteUInt16Bookmark(ref this SpanBufferWriter buffer,
            in SpanBufferWriter.Bookmark bookmark, ushort value)
        {
            buffer.WriteBookmark(bookmark, value, BinaryPrimitives.WriteUInt16LittleEndian);
        }

        public static void WriteDateTimeOffset(ref this SpanBufferWriter buffer, DateTimeOffset dateTimeOffset)
        {
            buffer.WriteInt64(dateTimeOffset.Ticks);
            buffer.WriteInt64(dateTimeOffset.Offset.Ticks);
        }

        public static DateTimeOffset ReadDateTimeOffset(ref this SpanBufferReader buffer)
        {
            return new(buffer.ReadInt64(), new TimeSpan(buffer.ReadInt64()));
        }
    }
}
