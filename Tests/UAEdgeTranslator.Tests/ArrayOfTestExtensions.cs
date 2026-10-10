namespace Opc.Ua.Edge.Translator.Tests
{
    using System.Collections.Generic;
    using Opc.Ua;

    /// <summary>
    /// Test helpers for SDK 2.0.0 <see cref="ArrayOf{T}"/> values.
    /// </summary>
    internal static class ArrayOfTestExtensions
    {
        /// <summary>
        /// Copies an <see cref="ArrayOf{T}"/> into a list so tests can use LINQ and
        /// <see cref="List{T}"/> members. <see cref="ArrayOf{T}"/> is not
        /// <see cref="IEnumerable{T}"/>, and a null instance enumerates as empty, so this
        /// also replaces the old <c>?? new XxxCollection()</c> null fallback.
        /// </summary>
        public static List<T> ToList<T>(this ArrayOf<T> source)
        {
            List<T> result = new(source.Count);
            foreach (T item in source)
            {
                result.Add(item);
            }

            return result;
        }
    }
}
