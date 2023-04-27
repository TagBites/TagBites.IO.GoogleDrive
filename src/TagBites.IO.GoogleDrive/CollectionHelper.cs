using System.Collections.Generic;

namespace TagBites.IO.GoogleDrive
{
    internal static class CollectionHelper
    {
        public static TValue TryGetValueDefault<TKey, TValue>(this IDictionary<TKey, TValue> collection, TKey key, TValue defaultValue = default)
        {
            return collection.TryGetValue(key, out var value)
                ? value
                : defaultValue;
        }
    }
}
