using System;
using System.Linq;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Attributes;
using Google.Protobuf;
using ProtoTypes=BinaryRecords.Benchmarks.Protobuf;

namespace BinaryRecords.Benchmarks
{
    public record IntArrayRecord(int[] Ints);

    public record StringArrayRecord(string[] Strings);

    public record ARecord(int Int, string String);

    public record RecordArrayRecord(ARecord[] Records);
    
    [MemoryDiagnoser]
    public class ArrayBenchmarks
    {
        [Params(1, 5, 50, 500, 5000)]
        public int Size;

        private (IntArrayRecord Value, byte[] Buffer) _recordIntArray;
        private (StringArrayRecord Value, byte[] Buffer) _recordStringArray;
        private (RecordArrayRecord Value, byte[] Buffer) _recordRecordArray;

        private (ProtoTypes::IntArrayMessage Value, byte[] Buffer) _protoIntArray;
        private (ProtoTypes::StringArrayMessage Value, byte[] Buffer) _protoStringArray;
        private (ProtoTypes::MessageArrayMessage Value, byte[] Buffer) _protoMessageArray;

        private BinarySerializer _serializer;
        
        private const string Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        
        private string GenerateRandomString(Random random, int size = 12)
        {
            return string.Create(size, random, (chars, random) =>
            {
                for (var i = 0; i < chars.Length; i++) 
                    chars[i] = Characters[random.Next(Characters.Length)];
            });
        }

        [GlobalSetup]
        public void Setup()
        {
            _serializer = BinarySerializerBuilder.BuildDefault();

            var random = new Random(Size);

            var strings = Enumerable.Range(0, Size)
                .Select(_ => GenerateRandomString(random, random.Next(12, 24))).ToArray();
            var ints = Enumerable.Range(0, Size)
                .Select(_ => random.Next()).ToArray();

            var records = Enumerable.Range(0, Size)
                .Select(i => new ARecord(ints[i], strings[i])).ToArray();

            var recordIntArray = new IntArrayRecord(ints);
            _recordIntArray = (recordIntArray, _serializer.Serialize(recordIntArray));
            var recordStringArray = new StringArrayRecord(strings);
            _recordStringArray = (recordStringArray, _serializer.Serialize(recordStringArray));
            var recordArrayRecord = new RecordArrayRecord(records);
            _recordRecordArray = (recordArrayRecord, _serializer.Serialize(recordArrayRecord));
            
            var messages = Enumerable.Range(0, Size)
                .Select(i => new ProtoTypes.AMessage { A = ints[i], B = strings[i] }).ToArray();
            
            var protoIntArray = new ProtoTypes.IntArrayMessage();
            protoIntArray.Ints.AddRange(ints);
            _protoIntArray = (protoIntArray, protoIntArray.ToByteArray());
            var protoStringArray = new ProtoTypes.StringArrayMessage();
            protoStringArray.Strings.AddRange(strings);
            _protoStringArray = (protoStringArray, protoStringArray.ToByteArray());
            var protoMessageArray = new ProtoTypes.MessageArrayMessage();
            protoMessageArray.Messages.AddRange(messages);
            _protoMessageArray = (protoMessageArray, protoMessageArray.ToByteArray());
        }

        #region Int Arrays

        [Benchmark]
        public void SerializeIntArray()
        {
            var x = _serializer.Serialize(_recordIntArray.Value, _recordIntArray.Buffer);
            DeadCodeEliminationHelper.KeepAliveWithoutBoxing(x);
        }
        
        [Benchmark]
        public void SerializeIntArrayGoogleProtobuf()
        {
            _protoIntArray.Value.WriteTo(_protoIntArray.Buffer);
            DeadCodeEliminationHelper.KeepAliveWithoutBoxing(1);
        }
        
        [Benchmark]
        public void DeserializeIntArray()
        {
            var x = _serializer.Deserialize<IntArrayRecord>(_recordIntArray.Buffer);
            DeadCodeEliminationHelper.KeepAliveWithoutBoxing(x);
        }
        
        [Benchmark]
        public void DeserializeIntArrayGoogleProtobuf()
        {
            var x = ProtoTypes::IntArrayMessage.Parser.ParseFrom(_protoIntArray.Buffer);
            DeadCodeEliminationHelper.KeepAliveWithoutBoxing(x);
        }

        #endregion
        
        #region String Array
        
        [Benchmark]
        public void SerializeStringArray()
        {
            var x = _serializer.Serialize(_recordStringArray.Value, _recordStringArray.Buffer);
            DeadCodeEliminationHelper.KeepAliveWithoutBoxing(x);
        }

        [Benchmark]
        public void SerializeStringArrayGoogleProtobuf()
        {
            _protoStringArray.Value.WriteTo(_protoStringArray.Buffer);
            DeadCodeEliminationHelper.KeepAliveWithoutBoxing(1);
        }
        
        [Benchmark]
        public void DeserializeStringArray()
        {
            var x = _serializer.Deserialize<StringArrayRecord>(_recordStringArray.Buffer);
            DeadCodeEliminationHelper.KeepAliveWithoutBoxing(x);
        }
        
        [Benchmark]
        public void DeserializeStringArrayGoogleProtobuf()
        {
            var x = ProtoTypes::StringArrayMessage.Parser.ParseFrom(_protoStringArray.Buffer);
            DeadCodeEliminationHelper.KeepAliveWithoutBoxing(x);
        }
        
        #endregion
        
        #region Class Arary
        
        [Benchmark]
        public void SerializeRecordArray()
        {
            var x = _serializer.Serialize(_recordRecordArray.Value, _recordRecordArray.Buffer);
            DeadCodeEliminationHelper.KeepAliveWithoutBoxing(x);
        }
        
        [Benchmark]
        public void SerializeMessageArrayGoogleProtobuf()
        {
            _protoMessageArray.Value.WriteTo(_protoMessageArray.Buffer);
            DeadCodeEliminationHelper.KeepAliveWithoutBoxing(1);
        }

        [Benchmark]
        public void DeserializeRecordArray()
        {
            var x = _serializer.Deserialize<RecordArrayRecord>(_recordRecordArray.Buffer);
            DeadCodeEliminationHelper.KeepAliveWithoutBoxing(x);
        }
        
        [Benchmark]
        public void DeserializeMessageArrayGoogleProtobuf()
        {
            var x = ProtoTypes::MessageArrayMessage.Parser.ParseFrom(_protoMessageArray.Buffer);
            DeadCodeEliminationHelper.KeepAliveWithoutBoxing(x);
        }
        
        #endregion
    }
}
