namespace BinaryRecords.Delegates
{
    public delegate T DeserializeExtensionDelegate<out T>(ref BinaryBufferReader bufferReader);
}