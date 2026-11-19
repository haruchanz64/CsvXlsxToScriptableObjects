using System;

namespace CsvXlsxToSo
{
    /// <summary>
    /// Maps a serialized ScriptableObject field to a specific spreadsheet column header name.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class SpreadsheetColumnAttribute : Attribute
    {
        public string ColumnName { get; }

        public SpreadsheetColumnAttribute(string columnName)
        {
            ColumnName = columnName;
        }
    }

    /// <summary>
    /// Marks a field to be ignored during automatic spreadsheet column mapping.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class SpreadsheetIgnoreAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks a field as the default identifier used for naming generated ScriptableObject assets.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class SpreadsheetIdAttribute : Attribute
    {
    }
}
