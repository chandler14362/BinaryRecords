# BinaryRecords

WIP C# serialization library with ultra-fast type-semantic free versioning. The versioning is completelty optional., with planned optional backwards compatibility too. Currently the only constructable types are records. It requires no attributes or registering of types. Registering your own serialize/deserialize functions is supported.

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
 - Support ValueTypes in the TypeSerializer in a machine ambiguous way using blittable block technique
 - Consider enums blittable based on their backing type
 - Look in to optimizing the blittable block data layout
 - Add warmup calls for type serializers (maybe an api for warming up an entire assembly too)
 
 - Support record inheritance (memberinit might be only non working)
 - Class and struct support
 - Ovehaul the internal buffer implementations, remove Krypton.Buffers dependency.
 - Toggable byte-order
 - Design a friendly api that lets you move the data between two different BinaryRecords serialization models, for example, you could have one for serializing LE and then one for BE. Both being able to execute completely separate from eachother. This would allow each one to plug in their own extensions to handle data differently too.
 - Redesign the version hash, current implementation was written with the old type model design in mind. Now the only data needed to determine the hash are the keys the type holds. It can probably get more compact than a guid without comprimising too much uniqueness. 

Spec is currently a todo, need to move this to its own file.

BinaryRecords Specification
---

The design phisolphoy:

BinaryRecords is designed to be maximum-performance. There will never be an attempt to compact the data. All the internal type implementations will forever be raw.
The data BinaryRecords produces is completely type-semantic free. This allows for endless, care-free, extensibility.
It is very easy to write extensions, you can override any builtin type.

I believe serialization and the ever evolving compression algorthims should work side by side. As compression evolves, BinaryRecords will become more compact.

The BinaryRecords versioning architecture:

---

The versioning architecture is being written to support backwards compatibility in the future.
Backwards compatibility is something some people care about so I think it's worth jotting down just for the future.
The backwards compatibility is first iteration, it can probably be optimized.

Current design limitations: Field count is capped at 65,535. I think this pretty fair. Yes, it's not very future proof. I have never worked in the industry before so I hold a bit of ignornance. If someone can give me a reason why it should be increased I will gladly change it. Max field size is capped at 18,446,744 terabytes, I think this is beyond fair.

Current plans for the versioning architecture:

The versioning architecure is designed to allow for a pre-calculated header size. This allows for maximum-performance in both serialization and deserialization.

version header:
guid typeVersion
ushort keyCount
repeating (ushort key, ulong size)

Backwards compatibility is completely optional, it will need to be turned on by implementing some sort of interface.

final data structure will be:
version header + flat data + (optional backwards compatibility data). optional backwards compatibility data is 
structured ushort keyCount, repeating (ushort key, ulong size) and then the flat data

again, the backwards compatibility is optional. the plan is only types that want it turn it on. just like the versioning.

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
we will have to create 2 new buffers. One buffer will contain (ushort key, ulong size), the other will contain the serialized data. 
After processing the current tracked data, we read the ushort keyCount, this data will be attempted to be processed too.
All keys and data that aren't being currently used will be shoved in to the 2 buffers that are created.
At the end of the deserialization process the 2 buffers will need to be combined with ushort keyCount at the head.
If there are no keys we want to keep track of after the deserialization process, the backwards compatible buffer will contain a single ushort 0 (keyCount).
This finalized buffer will represent the backwards compatible data.
The serializable type will have to implement an interface exposing a byte[] to hold this data.

---

And regardless of if the type is backwards compatible or not:

reserialized data gets put back at full confidence so the next time its deserialized its ultra-fast again.

```
BinaryRecords specification
Last modified at 2021-03-11 00:15:05 -0500
Chandler Stowell © 2021-03-08 8:24:00 -0500
```

Acknowledgements
---
I would like to thank my good friend Caleb Pina for his initial discussions with me on an extensible serialization library for the C# 9 record types. His encouragement led me to continue my work on BinaryRecords.
