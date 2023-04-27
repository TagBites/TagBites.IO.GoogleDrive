using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;

namespace TagBites.IO.GoogleDrive
{
    [DebuggerStepThrough]
    internal static class Guard
    {
        public static void ArgumentNotNull<T>(T value, string name)
        {
            if (Helpers<T>.IsNull(value))
                ThrowArgumentNullException(name);
        }
        public static void ArgumentNotNull(object value, string name)
        {
            if (ReferenceEquals(value, null))
                ThrowArgumentNullException(name);
        }

        public static void ArgumentNotNullOrEmpty(string value, string name)
        {
            if (string.IsNullOrEmpty(value))
                ThrowArgumentException(name, value);
        }
        public static void ArgumentNotNullOrWhiteSpace(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                ThrowArgumentException(name, value);
        }
        public static void ArgumentNotNullOrEmpty(ICollection value, string name)
        {
            ArgumentNotNull(value, name);

            if (value.Count == 0)
                ThrowArgumentException(name, value);
        }
        public static void ArgumentNotNullOrEmpty(IEnumerable value, string name)
        {
            ArgumentNotNull(value, name);

            if (!value.GetEnumerator().MoveNext())
                ThrowArgumentException(name, value);
        }

        private static void ThrowArgumentException(string propName, object val)
        {
            var arg = ReferenceEquals(val, string.Empty)
                ? "String.Empty"
                : (val == null ? "null" : val.ToString());
            var message = string.Format("'{0}' is not a valid value for '{1}'", arg, propName);
            throw new ArgumentException(message);
        }
        private static void ThrowArgumentNullException(string propName)
        {
            throw new ArgumentNullException(propName);
        }

        private static class Helpers<T>
        {
            public static readonly Func<T, bool> IsNull = typeof(T).GetTypeInfo().IsValueType
                ? (Func<T, bool>)(v => false)
                : v => ReferenceEquals(v, null);
        }
    }
}
