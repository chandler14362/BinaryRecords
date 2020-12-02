using System;
using System.Collections.Generic;
using System.Linq;
using BinaryRecords;

namespace ConsoleTest
{
    class Program
    {
        public record ListTest(List<(int, int)> AllTheInts);

        public record TupleTest(((string, string) X, int Z) Value);
        
        static void Main(string[] args)
        {
            var serializer = RuntimeTypeModel.CreateSerializer();
            var listTest = new ListTest(new List<(int, int)> {(1, 2), (3, 4), (5, 6)});
            serializer.Serialize(listTest, data =>
            {
                Console.WriteLine($"Serialized list test data is {data.Length} long!");
                var deserialized = serializer.Deserialize<ListTest>(data);
                Console.WriteLine(string.Join(" ", deserialized.AllTheInts));
            });
            
            var tupleTest = new TupleTest((("asdas", "qwoiu"), 2));
            serializer.Serialize(tupleTest, data =>
            {
                Console.WriteLine($"Serialized tuple test data is {data.Length} long!");
                var deserialized = serializer.Deserialize<TupleTest>(data);
                Console.WriteLine(deserialized);
            });
        }
    }
}
