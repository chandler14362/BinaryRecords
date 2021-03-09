using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using BinaryRecords.Records;
using BinaryRecords.Util;
using Krypton.Buffers;

namespace BinaryRecords.Providers
{
    public static class TypeRecordGuidProvider
    {
        private static readonly Dictionary<TypeRecord, Guid> _cachedGuids = new();
        
        public static Guid ComputeGuid(TypeRecord typeRecord)
        {
            if (_cachedGuids.TryGetValue(typeRecord, out var guid))
                return guid;
            var constructableHashTracker = new ConstructableHashTracker();
            var bufferWriter = new SpanBufferWriter(stackalloc byte[1024]);
            typeRecord.Hash(ref bufferWriter, constructableHashTracker);
            Span<byte> md5Bytes = stackalloc byte[16];
            if (!MD5.TryHashData(bufferWriter.Data, md5Bytes, out _))
                throw new Exception("Error calculating type record md5 hash");
            return _cachedGuids[typeRecord] = new Guid(md5Bytes);
        }
    }
}