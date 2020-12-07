using System.IO;
using BenchmarkDotNet.Attributes;
using ProtoBuf;

namespace BinaryRecords.Benchmarks
{
    [ProtoContract]
    public class ArrayTestClass
    {
        [ProtoMember(1)]
        public int[] A { get; set; }
    }

    public record ArrayTestRecord(int[] Values);

    public class Benchmarks
    {
        private readonly ArrayTestClass _arrayTestClass = new() { A = new int[50000] };
        private readonly ArrayTestRecord _arrayTestRecord = new(new int[50000]);
        
        private readonly BinarySerializer _serializer;
        
        private readonly byte[] _recordWriteBuffer;
        private readonly byte[] _recordReadBuffer;

        private MemoryStream _protoWriteStream = new();
        private MemoryStream _protoReadStream = new();

        public Benchmarks()
        {
            _serializer = RuntimeTypeModel.CreateSerializer();
            
            _recordReadBuffer = _serializer.Serialize(_arrayTestRecord);
            _recordWriteBuffer = new byte[_recordReadBuffer.Length];
            
            ProtoBuf.Serializer.Serialize(_protoWriteStream, _arrayTestClass);
            _protoReadStream.Write(_protoWriteStream.ToArray());
        }

        [Benchmark]
        public void SerializeArray()
        {
            _serializer.Serialize(_arrayTestRecord, _recordWriteBuffer);
        }

        [Benchmark]
        public void DeserializeArray()
        {
            var x = _serializer.Deserialize<ArrayTestRecord>(_recordReadBuffer);
        }
        
        [Benchmark]
        public void SerializeArrayProto()
        {
            _protoWriteStream.Seek(0, SeekOrigin.Begin);
            ProtoBuf.Serializer.Serialize(_protoWriteStream, _arrayTestClass);
        }

        [Benchmark]
        public void DeserializeArrayProto()
        {
            _protoReadStream.Seek(0, SeekOrigin.Begin);
            ProtoBuf.Serializer.Deserialize<ArrayTestClass>(_protoReadStream);
        }
    }
}
