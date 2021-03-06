using System;

namespace BinaryRecords.Exceptions
{
    public class EndOfBufferException : Exception
    {
        public int Size { get; }
        public int Offset { get; }
        public int NeededSize { get; }
        
        public EndOfBufferException(int size, int offset, int neededSize) 
            : base($"Size: {size}, Offset: {offset}, Needed Size: {neededSize}")
        {
            Size = size;
            Offset = offset;
            NeededSize = neededSize;
        }
    }
}