using System;
using System.Collections.Generic;

namespace BinaryRecords.Interfaces
{
    public interface ITypeLibrary
    {
        bool IsTypeSerializable(Type type);

        IList<Type> GetConstructableTypes();
    }
}
