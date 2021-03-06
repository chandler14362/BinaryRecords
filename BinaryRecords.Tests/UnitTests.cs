using System;
using System.Collections.Generic;
using NUnit.Framework;
using Krypton.Buffers;

namespace BinaryRecords.Tests
{
    public struct varint
    {
        private int _value;

        public varint(int value)
        {
            _value = value;
        }

        public static implicit operator varint(int value) => new (value);
        public static implicit operator int(varint value) => value._value;

        public override string ToString()
        {
            return _value.ToString();
        }
    }

    public record Employee(string First, string Last, varint Age);

    public record ListTest(List<int> AllTheInts);
    
    public record NullableTest(string Test, string Test2, int? Test3, int Test4);
    
    public record Argh
    {
        public string Name { get; init; }
        public string Puke { get; init; }

        public string Weeee => $"{Name}:{Puke}";
    }

    public class Tests
    {
        [SetUp]
        public void Setup()
        {
            BinarySerializer.AddGeneratorProvider(
                (ref SpanBufferWriter buffer, varint value) => buffer.WriteInt32(value + 24), 
                (ref SpanBufferReader bufferReader) => bufferReader.ReadInt32() - 24
            );
        }

        [Test]
        public void TestAssemblyRecordDetection()
        {
            var employee = new Employee("Joe", "ashdhkjasdkjh", 25);
            var buffer = new SpanBufferWriter(stackalloc byte[64]);
            BinarySerializer.Serialize(employee, ref buffer);
            Assert.AreEqual(employee, BinarySerializer.Deserialize<Employee>(buffer.Data));
            
            // Example of allocation free serialization without needed SpanBufferWriter, optional state you can pass too
            BinarySerializer.Serialize(employee, (data) =>
            {
                Console.WriteLine($"Serialized data is {data.Length} long!");
            });
            
            Console.WriteLine(employee);

            var argh = new Argh
            {
                Name = "Bread",
                Puke = "WTF"
            };
            buffer = new SpanBufferWriter(stackalloc byte[64]);
            BinarySerializer.Serialize(argh, ref buffer);
            Assert.AreEqual(argh, BinarySerializer.Deserialize<Argh>(buffer.Data));
            Console.WriteLine(argh);

            var listTest = new ListTest(new List<int> {5, 6, 7});
            BinarySerializer.Serialize(listTest, data =>
            {
                Console.WriteLine($"Serialized list test data is {data.Length} long!");
            });
        }
    }
}
