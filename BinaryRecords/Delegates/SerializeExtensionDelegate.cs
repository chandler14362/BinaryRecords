namespace BinaryRecords.Delegates
{
    public delegate void SerializeExtensionDelegate<in T>(
        T obj,
        ref BinaryBufferWriter buffer);
}