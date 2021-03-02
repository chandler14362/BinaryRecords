using System;
using System.Collections.Generic;
using BinaryRecords.Delegates;
using BinaryRecords.Models;
using BinaryRecords.Providers;

namespace BinaryRecords.Abstractions
{
    public interface ITypingLibrary
    { 
        void AddGeneratorProvider<T>(
            SerializeGenericDelegate<T> serializerDelegate,
            DeserializeGenericDelegate<T> deserializerDelegate,
            string? name = null,
            ProviderPriority priority = ProviderPriority.High);
        void AddGeneratorProvider(ExpressionGeneratorProvider expressionGeneratorProvider);
        ExpressionGeneratorProvider? GetInterestedGeneratorProvider(Type type);
        
        bool IsTypeSerializable(Type type);
        bool IsTypeBlittable(Type type);

        RecordConstructionModel GetRecordConstructionModel(Type recordType);
        bool TryGetRecordConstructionModel(Type recordType, out RecordConstructionModel? model);
        IEnumerable<RecordConstructionModel> GetRecordConstructionModels();
        IReadOnlyList<ExpressionGeneratorProvider> GetExpressionGeneratorProviders();
    }
}
