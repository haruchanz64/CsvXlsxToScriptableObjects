using System;
using System.Collections.Generic;

namespace CsvXlsxToSo.Editor.Parsers
{
    [Serializable]
    public sealed class SpreadsheetParseOptions
    {
        public bool AutoDetectDelimiter = true;
        public char Delimiter = ',';
        public int HeaderRowIndex = 0;
        public int TypeHintRowIndex = -1;
        public bool TrimWhitespace = true;
        public bool SkipEmptyRows = true;
        public string CommentPrefix = "#";

        public SpreadsheetParseOptions Clone()
        {
            return new SpreadsheetParseOptions
            {
                AutoDetectDelimiter = AutoDetectDelimiter,
                Delimiter = Delimiter,
                HeaderRowIndex = HeaderRowIndex,
                TypeHintRowIndex = TypeHintRowIndex,
                TrimWhitespace = TrimWhitespace,
                SkipEmptyRows = SkipEmptyRows,
                CommentPrefix = CommentPrefix
            };
        }
    }

    public sealed class SpreadsheetSheet
    {
        public string Name { get; }
        public IReadOnlyList<string> Headers { get; }
        public IReadOnlyList<string> TypeHints { get; }
        public IReadOnlyList<string[]> Rows { get; }

        public int ColumnCount => Headers.Count;
        public int RowCount => Rows.Count;

        public SpreadsheetSheet(
            string name,
            IReadOnlyList<string> headers,
            IReadOnlyList<string> typeHints,
            IReadOnlyList<string[]> rows)
        {
            Name = name ?? string.Empty;
            Headers = headers ?? Array.Empty<string>();
            TypeHints = typeHints ?? Array.Empty<string>();
            Rows = rows ?? Array.Empty<string[]>();
        }

        public int GetColumnIndex(string headerName)
        {
            if (string.IsNullOrEmpty(headerName))
                return -1;

            for (int i = 0; i < Headers.Count; i++)
            {
                if (string.Equals(Headers[i], headerName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        public string GetCell(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || rowIndex >= Rows.Count)
                return string.Empty;

            string[] row = Rows[rowIndex];
            if (columnIndex < 0 || columnIndex >= row.Length)
                return string.Empty;

            return row[columnIndex] ?? string.Empty;
        }
    }

    public sealed class SpreadsheetWorkbook
    {
        public string FilePath { get; }
        public IReadOnlyList<string> SheetNames { get; }
        public IReadOnlyDictionary<string, SpreadsheetSheet> Sheets { get; }

        public SpreadsheetWorkbook(
            string filePath,
            IReadOnlyList<string> sheetNames,
            IReadOnlyDictionary<string, SpreadsheetSheet> sheets)
        {
            FilePath = filePath ?? string.Empty;
            SheetNames = sheetNames ?? Array.Empty<string>();
            Sheets = sheets ?? new Dictionary<string, SpreadsheetSheet>();
        }

        public SpreadsheetSheet GetSheet(string name)
        {
            if (!string.IsNullOrEmpty(name) && Sheets.TryGetValue(name, out SpreadsheetSheet sheet))
                return sheet;

            if (SheetNames.Count > 0 && Sheets.TryGetValue(SheetNames[0], out SpreadsheetSheet first))
                return first;

            return null;
        }
    }

    internal static class SpreadsheetTableBuilder
    {
        public static SpreadsheetSheet BuildSheet(string sheetName, List<List<string>> rawRows, SpreadsheetParseOptions options)
        {
            options ??= new SpreadsheetParseOptions();

            if (rawRows == null || rawRows.Count == 0)
                return new SpreadsheetSheet(sheetName, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string[]>());

            int headerRowIdx = Math.Max(0, options.HeaderRowIndex);
            if (headerRowIdx >= rawRows.Count)
                return new SpreadsheetSheet(sheetName, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string[]>());

            List<string> rawHeaderRow = rawRows[headerRowIdx];
            int columnCount = rawHeaderRow.Count;

            // Expand column count if any data row is wider than the header row
            for (int r = headerRowIdx + 1; r < rawRows.Count; r++)
            {
                if (rawRows[r].Count > columnCount)
                    columnCount = rawRows[r].Count;
            }

            // Trim trailing completely empty columns
            while (columnCount > 0 && IsColumnCompletelyEmpty(rawRows, headerRowIdx, columnCount - 1))
                columnCount--;

            string[] headers = new string[columnCount];
            var seenHeaders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < columnCount; c++)
            {
                string raw = c < rawHeaderRow.Count ? rawHeaderRow[c] : string.Empty;
                string cleaned = options.TrimWhitespace ? (raw ?? string.Empty).Trim() : (raw ?? string.Empty);
                if (string.IsNullOrEmpty(cleaned))
                    cleaned = $"Column{c + 1}";

                if (seenHeaders.TryGetValue(cleaned, out int count))
                {
                    seenHeaders[cleaned] = count + 1;
                    cleaned = $"{cleaned}_{count + 1}";
                }
                else
                {
                    seenHeaders[cleaned] = 1;
                }

                headers[c] = cleaned;
            }

            string[] typeHints = Array.Empty<string>();
            if (options.TypeHintRowIndex >= 0 && options.TypeHintRowIndex < rawRows.Count)
            {
                List<string> rawHintRow = rawRows[options.TypeHintRowIndex];
                typeHints = new string[columnCount];
                for (int c = 0; c < columnCount; c++)
                {
                    string raw = c < rawHintRow.Count ? rawHintRow[c] : string.Empty;
                    typeHints[c] = options.TrimWhitespace ? (raw ?? string.Empty).Trim() : (raw ?? string.Empty);
                }
            }

            int startDataRow = Math.Max(headerRowIdx, options.TypeHintRowIndex) + 1;
            var dataRows = new List<string[]>();

            for (int r = startDataRow; r < rawRows.Count; r++)
            {
                List<string> rawRow = rawRows[r];
                if (IsCommentRow(rawRow, options.CommentPrefix))
                    continue;

                string[] normalized = new string[columnCount];
                bool allEmpty = true;
                for (int c = 0; c < columnCount; c++)
                {
                    string val = c < rawRow.Count ? rawRow[c] ?? string.Empty : string.Empty;
                    if (options.TrimWhitespace)
                        val = val.Trim();
                    normalized[c] = val;
                    if (!string.IsNullOrEmpty(val))
                        allEmpty = false;
                }

                if (allEmpty && options.SkipEmptyRows)
                    continue;

                dataRows.Add(normalized);
            }

            return new SpreadsheetSheet(sheetName, headers, typeHints, dataRows);
        }

        private static bool IsColumnCompletelyEmpty(List<List<string>> rawRows, int startRow, int colIndex)
        {
            for (int r = startRow; r < rawRows.Count; r++)
            {
                if (colIndex < rawRows[r].Count && !string.IsNullOrWhiteSpace(rawRows[r][colIndex]))
                    return false;
            }
            return true;
        }

        private static bool IsCommentRow(List<string> row, string commentPrefix)
        {
            if (string.IsNullOrEmpty(commentPrefix) || row == null || row.Count == 0)
                return false;

            string first = row[0];
            return !string.IsNullOrEmpty(first) && first.TrimStart().StartsWith(commentPrefix, StringComparison.Ordinal);
        }
    }
}
