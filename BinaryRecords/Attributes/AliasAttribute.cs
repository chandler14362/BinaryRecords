using System;

namespace BinaryRecords.Attributes
{
    public class AliasAttribute : Attribute
    {
        public readonly string Alias;

        public AliasAttribute(string alias)
        {
            Alias = alias;
        }
    }
}