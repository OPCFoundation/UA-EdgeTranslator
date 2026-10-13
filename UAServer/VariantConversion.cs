namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua;
    using System;

    /// <summary>
    /// Converts OPC UA <see cref="Variant"/> values to the plain CLR values that the
    /// protocol drivers have always been given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Before SDK 2.0.0 a value crossed into driver code as an <see cref="object"/>
    /// holding a plain CLR value. SDK 2.0.0 carries values as a <see cref="Variant"/>
    /// struct, and two easy mistakes both compile cleanly yet break at runtime:
    /// </para>
    /// <list type="bullet">
    /// <item>Assigning the <see cref="Variant"/> to <see cref="object"/> boxes the struct
    /// itself, so driver conversions such as <c>Convert.ToInt16(value)</c> throw and type
    /// patterns such as <c>value is float</c> never match.</item>
    /// <item><see cref="Variant.AsBoxedObject"/> unwraps primitives, but returns SDK types
    /// for byte strings (<see cref="ByteString"/>), timestamps (<see cref="DateTimeUtc"/>)
    /// and arrays (<c>ArrayOf&lt;T&gt;</c>), where drivers expect <c>byte[]</c>,
    /// <see cref="DateTime"/> and <c>T[]</c>.</item>
    /// </list>
    /// <para>
    /// Use this at every point where a value leaves the OPC UA value model for
    /// object-based driver code, so the driver contract is unchanged by the upgrade.
    /// </para>
    /// </remarks>
    public static class VariantConversion
    {
        /// <summary>
        /// Returns the CLR value held by <paramref name="value"/>, mapping SDK value types
        /// to their pre-2.0.0 CLR equivalents. A null variant yields <c>null</c>.
        /// </summary>
        public static object ToClrValue(Variant value)
        {
            return ToClrValue(value.AsBoxedObject());
        }

        private static object ToClrValue(object value)
        {
            switch (value)
            {
                case null:
                    return null;

                case ByteString bytes:
                    return bytes.IsNull ? null : bytes.ToArray();

                case DateTimeUtc time:
                    return (DateTime)time;

                case IConvertableToArray arrayOf:
                    return ToClrArray(arrayOf.ToArray());

                default:
                    return value;
            }
        }

        private static Array ToClrArray(Array array)
        {
            if (array == null)
            {
                return null;
            }

            // Map element types that SDK 2.0.0 represents differently, so an array of
            // byte strings or timestamps has the same shape drivers received before.
            Type elementType = array.GetType().GetElementType();
            if (elementType == typeof(ByteString) || elementType == typeof(DateTimeUtc))
            {
                Type clrElementType = elementType == typeof(ByteString) ? typeof(byte[]) : typeof(DateTime);
                Array converted = Array.CreateInstance(clrElementType, array.Length);
                for (int i = 0; i < array.Length; i++)
                {
                    converted.SetValue(ToClrValue(array.GetValue(i)), i);
                }

                return converted;
            }

            return array;
        }
    }
}
