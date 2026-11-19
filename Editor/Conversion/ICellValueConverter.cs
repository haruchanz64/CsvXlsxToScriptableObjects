using System;
using System.Globalization;

namespace CsvXlsxToSo.Editor.Conversion
{
    public sealed class CellConversionContext
    {
        public char ListSeparator { get; set; } = '|';
        public string[] AssetSearchFolders { get; set; }
        public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;
    }

    /// <summary>
    /// Implement this interface in any Editor assembly to register a custom
    /// spreadsheet-cell-to-C#-type converter. Discovered automatically via UnityEditor.TypeCache.
    /// </summary>
    public interface ICellValueConverter
    {
        bool CanConvert(Type targetType);
        bool TryConvert(string rawValue, Type targetType, CellConversionContext context, out object result, out string error);
    }
}
