# BinaryRecords

WIP C# serialization library with ultra-fast type-semantic free versioning that allows for deterministic and non-deterministic deserialization paths. 
The versioning is completely optional, with planned optional backwards compatibility too. Currently the only constructable types are records. Standard usage requires no attributes or registering of types. Registering your own serialize/deserialize functions is supported.

BinaryRecords does not offer compact versioning, in fact, the versioning data overhead is quite large. If you want a library that makes good compromise of both performance and data size checkout 
[MessagePack-CSharp](https://github.com/neuecc/MessagePack-CSharp).

Quick example:

```cs
public record SomethingVersioned([Key(0)]string First, [Key(1)]string Last, [Key(2)]int Age);

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
 - Fix nested record serialize generation lockup, just need to divert the codepath to use some referencable MethodInfo rather than inlining the expression.
 - Use varints for list type length headers.
 - Look in to optimizing the blittable block data layout, this will also allow us to generate less structs
 - Add warmup calls for type serializers (maybe an api for warming up an entire assembly too)
 - Support record inheritance (memberinit might be only non working)
 - Class and struct support
 - Non-generic serialize apis
 - Design a friendly api that lets you move the data between two different BinaryRecords serialization models, both being able to execute completely separate from each other. This would allow each one to plug in their own extensions to handle data differently too.
 - AOT codegen (need some attribute to mark the types with for this)

Spec is currently a todo, need to move this to its own file.

BinaryRecords Specification
---

The design philosophy:

BinaryRecords is designed to be maximum-performance. There will never be an attempt to compact the data. All the internal type implementations will forever be raw (with the exception of varints for list length headers, this is needed for seamless 32bit->64bit compatibility).
The data BinaryRecords produces is completely type-semantic free. This allows for endless, care-free, extensibility.
It is very easy to write extensions, you can override any builtin type.

I believe serialization and the ever evolving compression algorithms should work side by side. As compression evolves, BinaryRecords will become more compact.

The BinaryRecords versioning architecture:

---

The versioning architecture is designed to allow for a calculable header size. This allows for maximum-performance in both serialization and deserialization.
There will be two styles of versioning, 32-bit and 64-bit. The header is designed around a future 64-bit implementation that allows processing of old 32-bit headers.

version-hash:
guid versionHash

32-bit versioning architecture: 
Both field count and field size are capped at uint max, (2^32) - 1.

field-table:
uint fieldCount
4 bytes padding
repeating (uint key, uint size)

version-header:
version-hash
field-table

Forwards compatible data is structured as:
uint compatibility (reserved for future header compatibility)
4 bytes padding
version-header
serialized-data

Forwards/Backwards compatible data is structured as:
uint compatibility (reserved for future header compatibility)
4 bytes padding
version-header
serialized-data
field-table
serialized-data

---

Handling non-backwards compatible versioning serialization/deserialization (can be done allocation free, minus the actual types we deserialize):

if guid matches local version it is considered confident and header is skipped.
this allows for ultra fast deserialization as if there were no versioning to begin with.
it is done completely sequential, and is able to take advantage of the blittable block.

if guid doesn't match local, a non-confident deserialize is ran instead.
iterates through each key, skipping keys we dont have local fields for.
missing fields are filled in with default values.
data only ever seeks forward, never backwards.

---

Handling backwards compatible versioning serialization/deserialization:

handling backwards compatible type serialization:
serialize the type normally and then tack on the backwards compatible data the type is holding on to the end.

handling confident backwards compatible type deserialization:
do the ultra-fast deserialization and then calculate the data size from the backwards data header. Once it is calculated, the data can be copied and put on the object that needs to hold it.

handling non-confident backwards compatible type deserialization:
if the type is backwards compatible with its data and its a key we aren't tracking,
we will have to create 2 new buffers. One buffer will contain (uint key, uint size), the other will contain the serialized data. 
After processing the current tracked data, we process the next field-table describing the backwards compatible data we are holding.
All keys and data that aren't being currently used will be shoved in to the 2 buffers that are created.
At the end of the deserialization process the 2 buffers will need to be combined with uint unusedFieldCount + 4 bytes padding at the head.
This finalized buffer will represent the backwards compatible data.
The serializable type will have to implement an interface exposing a byte[] to hold this data.

---

And regardless of if the type is backwards compatible or not:

Reserialized data gets put back at full confidence so the next time its deserialized its ultra-fast again.

```
BinaryRecords specification
Last modified at 2021-03-11 15:21:35 -0500
Chandler Stowell © 2021-03-08 8:24:00 -0500
This copyright falls under the same MIT license included in the https://github.com/chandler14362/BinaryRecords repository.
```

Acknowledgements
---
I would like to thank my good friend Caleb Pina for his initial discussions with me on an extensible serialization library for the C# 9 record types. His encouragement led me to continue my work on BinaryRecords.
