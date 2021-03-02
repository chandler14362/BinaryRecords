using System;

namespace BinaryRecords.Interfaces
{
    public interface ITypeLibrary
    {
        bool IsTypeSerializable(Type type);

        bool IsTypeBlittable(Type type);
    }
}
