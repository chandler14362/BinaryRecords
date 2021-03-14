using System;
using BinaryRecords.Abstractions;
using BinaryRecords.Records;

namespace BinaryRecords.Delegates
{
    public delegate TypeRecord GenerateTypeRecordDelegate(ITypingLibrary typingLibrary, Type type);
}