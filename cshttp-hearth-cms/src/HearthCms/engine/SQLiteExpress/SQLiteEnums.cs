// SQLiteEnums.cs
// Part of SQLiteExpress
// https://github.com/adriancs2/SQLiteExpress

namespace System.Data.SQLite
{
    /// <summary>
    /// Represents the SQLite column data type used when defining table structure.
    /// </summary>
    public enum ColType
    {
        /// <summary>TEXT affinity - stores string values.</summary>
        Text,

        /// <summary>DATETIME affinity - stores date and time values as text in ISO 8601 format.</summary>
        DateTime,

        /// <summary>INTEGER affinity - stores signed integer values.</summary>
        Integer,

        /// <summary>DECIMAL (REAL) affinity - stores floating point or fixed-point numeric values.</summary>
        Decimal,

        /// <summary>BLOB affinity - stores binary data exactly as input.</summary>
        BLOB
    }

    /// <summary>
    /// Controls which members of a class are included when generating class field/property code.
    /// </summary>
    public enum FieldsOutputType
    {
        /// <summary>Generate private fields paired with public properties (backing field pattern).</summary>
        PrivateFielsPublicProperties,

        /// <summary>Generate public auto-properties only.</summary>
        PublicProperties,

        /// <summary>Generate public fields only.</summary>
        PublicFields
    }
}
