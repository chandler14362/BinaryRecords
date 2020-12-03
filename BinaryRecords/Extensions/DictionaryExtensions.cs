using System.Collections.Generic;

namespace BinaryRecords.Extensions
{
    public static class DictionaryExtensions
    {
        public static void Add<TKey, TValue>(this IDictionary<TKey, TValue> dict, KeyValuePair<TKey, TValue> kvp)
        {
            dict.Add(kvp.Key, kvp.Value);
        }
    }
}
