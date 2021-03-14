using System;
using BinaryRecords.Abstractions;

namespace BinaryRecords.Delegates
{
    public delegate bool ProviderIsInterestedDelegate(Type type, ITypingLibrary typingLibrary);
}