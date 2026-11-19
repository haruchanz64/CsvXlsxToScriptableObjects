using System;
using System.Collections.Generic;
using System.IO;

namespace CsvXlsxToSo.Editor.Parsers
{
    public static class SpreadsheetLoader
    {
        public static bool IsSupportedFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            string ext = Path.GetExtension(filePath);
            return string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".tsv", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsXlsx(string filePath)
        {
            return !string.IsNullOrEmpty(filePath) &&
                   string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase);
        }

        public static List<string> GetSheetNames(string filePath)
        {
            if (IsXlsx(filePath))
                return XlsxParser.GetSheetNames(filePath);

            return new List<string> { Path.GetFileNameWithoutExtension(filePath) };
        }

        public static SpreadsheetWorkbook LoadWorkbook(string filePath, SpreadsheetParseOptions options = null)
        {
            if (IsXlsx(filePath))
                return XlsxParser.ParseWorkbook(filePath, options);

            SpreadsheetParseOptions effective = options?.Clone() ?? new SpreadsheetParseOptions();
            if (string.Equals(Path.GetExtension(filePath), ".tsv", StringComparison.OrdinalIgnoreCase))
            {
                effective.AutoDetectDelimiter = false;
                effective.Delimiter = '\t';
            }

            SpreadsheetSheet sheet = CsvParser.ParseFile(filePath, effective);
            var sheets = new Dictionary<string, SpreadsheetSheet>(StringComparer.OrdinalIgnoreCase)
            {
                [sheet.Name] = sheet
            };
            return new SpreadsheetWorkbook(filePath, new[] { sheet.Name }, sheets);
        }

        public static SpreadsheetSheet LoadSheet(string filePath, string sheetName = null, SpreadsheetParseOptions options = null)
        {
            if (IsXlsx(filePath))
                return XlsxParser.ParseSheet(filePath, sheetName, options);

            SpreadsheetParseOptions effective = options?.Clone() ?? new SpreadsheetParseOptions();
            if (string.Equals(Path.GetExtension(filePath), ".tsv", StringComparison.OrdinalIgnoreCase))
            {
                effective.AutoDetectDelimiter = false;
                effective.Delimiter = '\t';
            }

            return CsvParser.ParseFile(filePath, effective);
        }
    }
}
