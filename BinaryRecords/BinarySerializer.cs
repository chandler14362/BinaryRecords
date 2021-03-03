using System;
using System.Buffers;
using System.Linq;
using BinaryRecords.Abstractions;
using BinaryRecords.Delegates;
using BinaryRecords.Implementations;
using BinaryRecords.Providers;
using Krypton.Buffers;

namespace BinaryRecords
{
    public static class BinarySerializer
    {
        private static readonly ITypingLibrary TypingLibrary = new TypingLibrary();
        private static readonly TypeSerializer TypeSerializer = new (TypingLibrary);
        
        static BinarySerializer()
        {
            // Initialize our builtin generator providers
            var generatorProviders = BufferExpressionGeneratorProviders.Builtin
                .Concat(CollectionExpressionGeneratorProviders.Builtin)
                .Concat(MiscExpressionGeneratorProviders.Builtin);
            foreach (var generatorProvider in generatorProviders) 
                TypingLibrary.AddGeneratorProvider(generatorProvider);
        }

        public static void AddGeneratorProvider<T>(
            SerializeGenericDelegate<T> serializerDelegate,
            DeserializeGenericDelegate<T> deserializerDelegate,
            string? name = null,
            ProviderPriority priority = ProviderPriority.High) =>
            TypingLibrary.AddGeneratorProvider(serializerDelegate, deserializerDelegate, name, priority);

        public static void AddGeneratorProvider(ExpressionGeneratorProvider expressionGeneratorProvider) =>
            TypingLibrary.AddGeneratorProvider(expressionGeneratorProvider);

        public static void Serialize<T>(
            T obj, 
            ref SpanBufferWriter buffer) =>
            TypeSerializer.Serialize(obj, ref buffer);

        public static void Serialize<T, TState>(
            T obj, 
            TState state,
            ReadOnlySpanAction<byte, TState> callback,
            int stackSize = 512) =>
            TypeSerializer.Serialize(obj, state, callback, stackSize);

        public static void Serialize(
            Type type, 
            object obj, 
            StatelessSerializationCallback callback,
            int stackSize = 512) =>
            TypeSerializer.Serialize(type, obj, callback, stackSize);

        public static void Serialize<T>(T obj, StatelessSerializationCallback callback, int stackSize = 512) =>
            TypeSerializer.Serialize(obj, callback, stackSize);

        public static int Serialize(Type objType, object obj, Memory<byte> memory) =>
            TypeSerializer.Serialize(objType, obj, memory);

        public static int Serialize<T>(T obj, Memory<byte> memory) =>
            TypeSerializer.Serialize(obj, memory);

        public static byte[] Serialize(Type objType, object obj) =>
            TypeSerializer.Serialize(objType, obj);

        public static byte[] Serialize<T>(T obj) =>
            TypeSerializer.Serialize(obj);

        public static object Deserialize(Type type, ReadOnlySpan<byte> buffer) =>
            TypeSerializer.Deserialize(type, buffer);

        public static T Deserialize<T>(ReadOnlySpan<byte> buffer) =>
            TypeSerializer.Deserialize<T>(buffer);

        public static object Deserialize(Type type, ref SpanBufferReader bufferReader) =>
            TypeSerializer.Deserialize(type, ref bufferReader);

        public static T Deserialize<T>(ref SpanBufferReader bufferReader) =>
            TypeSerializer.Deserialize<T>(ref bufferReader);
    }
}