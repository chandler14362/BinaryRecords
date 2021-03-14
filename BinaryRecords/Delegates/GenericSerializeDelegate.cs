namespace BinaryRecords.Delegates
{
    public delegate void GenericSerializeDelegate<T>(T obj, ref BinaryBufferWriter buffer);
}
