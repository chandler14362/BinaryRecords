# BinaryRecords

WIP library for serializing the C# 9 record types. It requires no attributes or registering of types. Registering your own serialize/deserialize functions is supported.

Quick example:

```cs
public record Person(string First, string Last, int Age);

void Main(string[] args) 
{
    _serializer = RuntimeTypeModel.CreateSerializer();
    
    var person = new("Robert", "Wallace", 26);
    
    // byte[] containing data
    var serialized = _serializer.Serialize(person);
    
    // deserialize
    var deserialized = _serializer.Deserialize<Person>(serialized);
}
```

More examples [here](https://github.com/chandler14362/BinaryRecords/blob/main/ConsoleTest/Program.cs)
