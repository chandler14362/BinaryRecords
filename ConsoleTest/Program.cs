using System;
using System.Collections.Generic;
using BinaryRecords;

namespace ConsoleTest
{
    public struct strangeint
    {
        private int _value;

        public strangeint(int value)
        {
            _value = value;
        }

        public static implicit operator strangeint(int value) => new (value);
        public static implicit operator int(strangeint value) => value._value;

        public override string ToString()
        {
            return _value.ToString();
        }
    }
    
    class Program
    {
        public record ListTest(IEnumerable<(int, int)> AllTheInts);

        public record HashSetTest(HashSet<int> SomeSet);
        
        public record DictTest(Dictionary<int, string> Value);

        public record LinkedListTest(LinkedList<int> Values);
        
        public record TupleTest(((string, string) X, int Z) Value);

        public record KvpTest(KeyValuePair<int, int> Kvp);

        public record ArrayTest(int[] Array);
        
        public record RecordTest(KvpTest Nested, strangeint Something, TestEnum Test);

        public record GenericRecord<T>([Key(3)] string S, [Key(0)] T Value, [Key(1)] int X, [Key(2)] int Y);

        public record SimilarRecord([Key(0)] int Value, [Key(2)] int X, [Key(1)] int Y, [Key(3)] string Z);

        public enum TestEnum
        {
            A,
            B,
            C
        }

        public static void Main(string[] args)
        {
            
            BinarySerializer.AddGeneratorProvider(
                (strangeint value, ref BinaryBufferWriter buffer) => buffer.WriteInt32(value + 24),
                (ref BinaryBufferReader bufferReader) => bufferReader.ReadInt32() - 24
            );
            BinarySerializer.AddGeneratorProvider(
                (DateTimeOffset value, ref BinaryBufferWriter buffer) => buffer.WriteInt64(value.UtcTicks),
                (ref BinaryBufferReader bufferReader) => new(bufferReader.ReadInt64(), TimeSpan.Zero)
            );

            var listTest = new ListTest(new List<(int, int)> {(1, 2), (3, 4), (5, 6)});
            BinarySerializer.Serialize(listTest, 0, (data, state) =>
            {
                Console.WriteLine($"Serialized list test data is {data.Length} long!");
                var deserialized = BinarySerializer.Deserialize<ListTest>(data);
                Console.WriteLine(string.Join(" ", deserialized.AllTheInts));
            });
            
            var hashSetTest = new HashSetTest(new HashSet<int> {0, 2, 4});
            BinarySerializer.Serialize(hashSetTest, 0, (data, state) =>
            {
                Console.WriteLine($"Serialized hash set test data is {data.Length} long!");
                var deserialized = BinarySerializer.Deserialize<HashSetTest>(data);
                Console.WriteLine(string.Join(" ", deserialized.SomeSet));
            });
            
            var dictTest = new DictTest(new Dictionary<int, string> { {0, "asssa"} });
            BinarySerializer.Serialize(dictTest, 0, (data, state) =>
            {
                Console.WriteLine($"Serialized dict test data is {data.Length} long!");
                var deserialized = BinarySerializer.Deserialize<DictTest>(data);
                Console.WriteLine(string.Join(" ", deserialized.Value));
            });

            var ll = new LinkedList<int>();
            ll.AddLast(0);
            ll.AddLast(1);
            var linkedListTest = new LinkedListTest(ll);
            BinarySerializer.Serialize(linkedListTest, 0, (data, state) =>
            {
                Console.WriteLine($"Serialized linked list test data is {data.Length} long!");
                var deserialized = BinarySerializer.Deserialize<LinkedListTest>(data);
                Console.WriteLine(string.Join(" ", deserialized.Values));
            });

            var tupleTest = new TupleTest((("asdas", "qwoiu"), 2));
            BinarySerializer.Serialize(tupleTest, 0, (data, state) =>
            {
                Console.WriteLine($"Serialized tuple test data is {data.Length} long!");
                var deserialized = BinarySerializer.Deserialize<TupleTest>(data);
                Console.WriteLine(deserialized);
            });
            
            var kvpTest = new KvpTest(new KeyValuePair<int, int>(2, 4));
            BinarySerializer.Serialize(kvpTest, 0, (data, state) =>
            {
                Console.WriteLine($"Serialized kvp test data is {data.Length} long!");
                var deserialized = BinarySerializer.Deserialize<KvpTest>(data);
                Console.WriteLine(deserialized);
            });
            
            var arrayTest = new ArrayTest(new [] { 3, 4, 6, 7 });
            BinarySerializer.Serialize(arrayTest, 0, (data, state) =>
            {
                Console.WriteLine($"Serialized array test data is {data.Length} long!");
                var deserialized = BinarySerializer.Deserialize<ArrayTest>(data);
                Console.WriteLine(string.Join(" ", deserialized.Array));
            });
            
            var recordTest = new RecordTest(new KvpTest(new KeyValuePair<int, int>(2, 4)), 5, TestEnum.B);
            BinarySerializer.Serialize(recordTest, 0, (data, state) =>
            {
                Console.WriteLine($"Serialized record test data is {data.Length} long!");
                var deserialized = BinarySerializer.Deserialize<RecordTest>(data);
                Console.WriteLine(deserialized);
            });
            
            var genericRecordTest = new GenericRecord<int>("saasddsdasdsa", 20, 4, 6);
            BinarySerializer.Serialize(genericRecordTest, 0, (data, state) =>
            {
                Console.WriteLine($"Serialized generic record test data is {data.Length} long!");
                var deserialized = BinarySerializer.Deserialize<GenericRecord<int>>(data);
                Console.WriteLine(deserialized);
            });
        }
    }
}
