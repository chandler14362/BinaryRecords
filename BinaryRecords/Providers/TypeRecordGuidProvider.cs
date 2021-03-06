using System;
using System.Security.Cryptography;
using BinaryRecords.Records;
using Krypton.Buffers;

namespace BinaryRecords.Providers
{
    public static class TypeRecordGuidProvider
    {
        public static Guid ComputeGuid(TypeRecord typeRecord)
        {
            var bufferWriter = new SpanBufferWriter(stackalloc byte[1024]);
            typeRecord.Hash(ref bufferWriter);
            Span<byte> md5Bytes = stackalloc byte[16];
            if (!MD5.TryHashData(bufferWriter.Data, md5Bytes, out _))
                throw new Exception("Error calculating type record md5 hash");
            return new Guid(md5Bytes);
        }
    }
}