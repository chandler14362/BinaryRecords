using System.Buffers.Binary;
using Krypton.Buffers;

namespace BinaryRecords
{
    public static class BinarySerializerUtil
    {
        public static void WriteUInt16Bookmark(ref SpanBufferWriter buffer,
            in SpanBufferWriter.Bookmark bookmark, ushort value)
        {
            buffer.WriteBookmark(bookmark, value, BinaryPrimitives.WriteUInt16LittleEndian);
        }
    }
}
