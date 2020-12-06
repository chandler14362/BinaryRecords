using System;
using System.Collections.Generic;
using BinaryRecords;
using Krypton.Buffers;

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

        public enum TestEnum
        {
            A,
            B,
            C
        }
        
        public static void Main(string[] args)
        {
            RuntimeTypeModel.Register(
                (ref SpanBufferWriter buffer, strangeint value) => buffer.WriteInt32(value + 24),
                (ref SpanBufferReader bufferReader) => bufferReader.ReadInt32() - 24
            );
            //RuntimeTypeModel.Register(
            //    (ref SpanBufferWriter buffer, DateTimeOffset value) => buffer.WriteInt64(value.UtcTicks),
            //    (ref SpanBufferReader bufferReader) => new(bufferReader.ReadInt64(), TimeSpan.Zero)
            //);
            var serializer = RuntimeTypeModel.CreateSerializer();
            
            var listTest = new ListTest(new List<(int, int)> {(1, 2), (3, 4), (5, 6)});
            serializer.Serialize(listTest, data =>
            {
                Console.WriteLine($"Serialized list test data is {data.Length} long!");
                var deserialized = serializer.Deserialize<ListTest>(data);
                Console.WriteLine(string.Join(" ", deserialized.AllTheInts));
            });
            
            var hashSetTest = new HashSetTest(new HashSet<int> {0, 2, 4});
            serializer.Serialize(hashSetTest, data =>
            {
                Console.WriteLine($"Serialized hash set test data is {data.Length} long!");
                var deserialized = serializer.Deserialize<HashSetTest>(data);
                Console.WriteLine(string.Join(" ", deserialized.SomeSet));
            });
            
            var dictTest = new DictTest(new Dictionary<int, string> { {0, "asssa"} });
            serializer.Serialize(dictTest, data =>
            {
                Console.WriteLine($"Serialized dict test data is {data.Length} long!");
                var deserialized = serializer.Deserialize<DictTest>(data);
                Console.WriteLine(string.Join(" ", deserialized.Value));
            });

            var ll = new LinkedList<int>();
            ll.AddLast(0);
            ll.AddLast(1);
            var linkedListTest = new LinkedListTest(ll);
            serializer.Serialize(linkedListTest, data =>
            {
                Console.WriteLine($"Serialized linked list test data is {data.Length} long!");
                var deserialized = serializer.Deserialize<LinkedListTest>(data);
                Console.WriteLine(string.Join(" ", deserialized.Values));
            });

            var tupleTest = new TupleTest((("asdas", "qwoiu"), 2));
            serializer.Serialize(tupleTest, data =>
            {
                Console.WriteLine($"Serialized tuple test data is {data.Length} long!");
                var deserialized = serializer.Deserialize<TupleTest>(data);
                Console.WriteLine(deserialized);
            });
            
            var kvpTest = new KvpTest(new KeyValuePair<int, int>(2, 4));
            serializer.Serialize(kvpTest, data =>
            {
                Console.WriteLine($"Serialized kvp test data is {data.Length} long!");
                var deserialized = serializer.Deserialize<KvpTest>(data);
                Console.WriteLine(deserialized);
            });
            
            var arrayTest = new ArrayTest(new [] { 3, 4, 6, 7 });
            serializer.Serialize(arrayTest, data =>
            {
                Console.WriteLine($"Serialized array test data is {data.Length} long!");
                var deserialized = serializer.Deserialize<ArrayTest>(data);
                Console.WriteLine(string.Join(" ", deserialized.Array));
            });
            
            var recordTest = new RecordTest(new KvpTest(new KeyValuePair<int, int>(2, 4)), 5, TestEnum.B);
            serializer.Serialize(recordTest, data =>
            {
                Console.WriteLine($"Serialized record test data is {data.Length} long!");
                var deserialized = serializer.Deserialize<RecordTest>(data);
                Console.WriteLine(deserialized);
            });
        }
    }
}
