using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using BinaryRecords.Enums;
using BinaryRecords.Records;

namespace BinaryRecords.Providers
{
    public static class ConstructableVersionGuidProvider
    {
        private static readonly Dictionary<ConstructableTypeRecord, Guid> CachedGuids = new();
        
        public static Guid ComputeGuid(ConstructableTypeRecord constructableType, BitSize bitSize)
        {
            Debug.Assert(constructableType.Versioned);
            if (bitSize != BitSize.B32)
                throw new NotImplementedException("Only 32-bit is currently supported.");
            if (CachedGuids.TryGetValue(constructableType, out var guid))
                return guid;
            var bufferSize = constructableType.Members.Count * sizeof(uint);
            Span<byte> keyBuffer = bufferSize < 512 ? stackalloc byte[bufferSize] : new byte[bufferSize];
            var bufferWriter = new BinaryBufferWriter(keyBuffer);
            foreach (var (key, _) in constructableType.Members)
                bufferWriter.WriteUInt32(key);
            Span<byte> md5Bytes = stackalloc byte[16];
            if (!MD5.TryHashData(bufferWriter.Data, md5Bytes, out _))
                throw new Exception("Error calculating constructable md5 hash");
            return CachedGuids[constructableType] = new Guid(md5Bytes);
        }
    }
}