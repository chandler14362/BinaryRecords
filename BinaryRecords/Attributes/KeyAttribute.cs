using System;

namespace BinaryRecords
{
    public class KeyAttribute : Attribute
    {
        public readonly uint Index;

        public KeyAttribute(uint index)
        {
            Index = index;
        }
    }
}