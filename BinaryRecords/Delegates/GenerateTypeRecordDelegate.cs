using System;
using BinaryRecords.Abstractions;
using BinaryRecords.Records;

namespace BinaryRecords.Delegates
{
    public delegate TypeRecord GenerateTypeRecordDelegate(Type type, ITypingLibrary typingLibrary);
}