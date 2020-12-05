using BenchmarkDotNet.Attributes;
using Krypton.Buffers;

namespace BinaryRecords.Benchmarks
{
    public record SomeTest(string A, int B, string C);
    
    public class Benchmarks
    {
        private readonly SomeTest _something = new("defghijk", 123223, "ssss");
        private readonly BinarySerializer _serializer;
        private readonly byte[] _buffer;
        private byte[] _serialized;

        public Benchmarks()
        {
            _serializer = RuntimeTypeModel.CreateSerializer();
            _serialized = _serializer.Serialize(_something);
            _buffer = new byte[_serialized.Length];
        }

        [Benchmark]
        public void SerializeRecord()
        {
            _serializer.Serialize(_something, _buffer);
        }

        
        [Benchmark]
        public void RawSerialize()
        {
            var bufferWriter = new SpanBufferWriter(_serialized, resize: false);
            bufferWriter.WriteUTF8String(_something.A);
            bufferWriter.WriteInt32(_something.B);
            bufferWriter.WriteUTF8String(_something.C);
        }
        
        [Benchmark]
        public void DeserializeRecord()
        {
            var x = _serializer.Deserialize<SomeTest>(_serialized);
        }

        [Benchmark]
        public void RawDeserialize()
        {
            var reader = new SpanBufferReader(_serialized);
            reader.ReadUTF8String();
            reader.ReadInt32();
            reader.ReadUTF8String();
        }
    }
}