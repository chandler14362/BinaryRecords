using System;

namespace BinaryRecords.Attributes
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