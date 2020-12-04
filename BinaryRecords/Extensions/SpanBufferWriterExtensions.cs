using System.Buffers.Binary;
using Krypton.Buffers;

namespace BinaryRecords.Extensions
{
    public static class SpanBufferWriterExtensions
    {
        public static void WriteUInt16Bookmark(ref this SpanBufferWriter buffer,
            in SpanBufferWriter.Bookmark bookmark, ushort value)
        {
            buffer.WriteBookmark(bookmark, value, BinaryPrimitives.WriteUInt16LittleEndian);
        }
    }
}
