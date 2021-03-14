namespace BinaryRecords.Delegates
{
    public delegate T GenericDeserializeDelegate<T>(ref BinaryBufferReader bufferReader);
}
