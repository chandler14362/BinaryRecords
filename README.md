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


Spec is currently a todo, need to move this to its own file.

BinaryRecords Specification
---


The BinaryRecords versioning architecture:

---

The versioning architecture is being written to support backwards compatibility in the future.
for now I am going to focus on writing the non-backwards stuff first but backwards compatibility
is something some people care about so I think it's worth jotting down just for the future.
The backwards compatibility is first iteration, it can probably be optimized. It's pretty ambitious so I'm just going to tackle the forward-compatible stuff first. It's likely a feature for the future.

Current plans for the versioning architecture:

version header:
guid typeVersion
uint keyCount
repeating (uint key, uint size)

Backwards compatibility is completely optional, it will need to be turned on by implementing some sort of interface.

final data structure will be:
version header + flat data + (optional backwards compatibility data). optional backwards compatibility data is 
structured uint backwardsDataSize, uint keyCount, repeating (uint key, uint size) and then the flat data

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
do the ultra-fast deserialization and then read the uint backwardsCompatibilitySize and copy the data in to a new array to be shoved right on to the type.

handling non-confident backwards compatible type deserialization:
if the type is backwards compatible with its data and its a key we aren't tracking,
we will have to create 2 new buffers. One buffer will contain (uint key, uint size), the other will contain the serialized data. 
After processing the current tracked data, we skip uint backwardsDataSize and read the uint keyCount, this data will be attempted to be processed too.
All keys and data that aren't being currently used will be shoved in to the 2 buffers that are created.
At the end of the deserialization process the 2 buffers will need to be combined with size and keyCount at the head.
If there are no keys we want to keep track of after the deserialization process, the backwards compatible buffer will contain a uint 4 (size) and uint 0 (keyCount).
This finalized buffer will represent the backwards compatible data.
The serializable type will have to implement an interface exposing a byte[] to hold this data.

---

And regardless of if the type is backwards compatible or not:

reserialized data gets put back at full confidence so the next time its deserialized its ultra fast again.

```
BinaryRecords specification
Last modified at 2021-03-08 12:51:00 +0500
Chandler Stowell © 2021-03-08 8:24:00 +0500
```
