# BinaryRecords

WIP library for serializing the C# 9 record types. It requires no attributes or registering of types. Registering your own serialize/deserialize functions is supported.

Quick example:

```cs
public record Person(string First, string Last, int Age);

void Main(string[] args) 
{
    var person = new Person("Robert", "Wallace", 26);
    
    // byte[] containing data
    var serialized = BinarySerializer.Serialize(person);
    
    // deserialize
    var deserialized = BinarySerializer.Deserialize<Person>(serialized);
}
```

More examples [here](https://github.com/chandler14362/BinaryRecords/blob/main/ConsoleTest/Program.cs)

Dev todo:
 - Make an ExpressionGeneratorProvider for records, move the logic out of TypeSerializer
 - Support ValueTypes in the TypeSerializer in a machine ambiguous way using blittable block technique
 - Consider enums blittable based on their backing type
 - Look in to optimizing the blittable block data layout
 - Add warmup calls for type serializers (maybe an api for warming up an entire assembly too)
 - Support record inheritance
 - Support more than just records (?)
