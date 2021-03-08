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
 - Support ValueTypes in the TypeSerializer in a machine ambiguous way using blittable block technique
 - Consider enums blittable based on their backing type
 - Look in to optimizing the blittable block data layout
 - Add warmup calls for type serializers (maybe an api for warming up an entire assembly too)
 
 - Support record inheritance (memberinit might be only non working)
 - Class and struct support
 - write my own code to compile expression to methodinfo, all the pieces are there in .net5, just hidden away (i think, gonna try)

Current plans for the versioning architecture:

format is
version header
flat data

version header:
type Guid
uint keyCount
repeating (uint key, uint size)

if guid matches local version it is considered confident and header is skipped.
this allows for ultra fast deserialization as if there were no versioning to begin with
it is done completely flat, no looping

if guid doesn't match local, a non-confident deserialize is ran instead
iterates through each key, skipping keys we dont have local fields for.
missing fields are filled in with default values.
data only ever seeks forward, never backwards

reserialized data gets put back at full confidence so next time its deserialized its ultra fast again