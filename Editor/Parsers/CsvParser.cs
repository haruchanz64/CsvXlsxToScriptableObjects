using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CsvXlsxToSo.Editor.Parsers
{
    /// <summary>
    /// RFC 4180 compliant CSV/TSV parser with delimiter auto-detection,
    /// quoted fields, escaped quotes, embedded newlines, and UTF-8 BOM handling.
    /// </summary>
    public static class CsvParser
    {
        private static readonly char[] CandidateDelimiters = { ',', ';', '\t' };

        public static SpreadsheetSheet ParseFile(string filePath, SpreadsheetParseOptions options = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"CSV file not found: {filePath}", filePath);

            string content = File.ReadAllText(filePath, Encoding.UTF8);
            string sheetName = Path.GetFileNameWithoutExtension(filePath);
            return ParseString(content, sheetName, options);
        }

        public static SpreadsheetSheet ParseString(string csvContent, string sheetName = "Sheet1", SpreadsheetParseOptions options = null)
        {
            options ??= new SpreadsheetParseOptions();
            if (string.IsNullOrEmpty(csvContent))
                return new SpreadsheetSheet(sheetName, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string[]>());

            // Strip UTF-8 BOM if present
            if (csvContent[0] == '\uFEFF')
                csvContent = csvContent.Substring(1);

            char delimiter = options.AutoDetectDelimiter
                ? DetectDelimiter(csvContent, options.Delimiter)
                : options.Delimiter;

            List<List<string>> rawRows = ParseRawRows(csvContent, delimiter);
            return SpreadsheetTableBuilder.BuildSheet(sheetName, rawRows, options);
        }

        public static char DetectDelimiter(string content, char fallback = ',')
        {
            if (string.IsNullOrEmpty(content))
                return fallback;

            // Inspect up to the first 5 non-empty lines outside of quotes
            int bestScore = 0;
            char bestDelimiter = fallback;

            foreach (char candidate in CandidateDelimiters)
            {
                int lineCount = 0;
                int totalDelims = 0;
                bool inQuotes = false;
                int currentLineDelims = 0;
                int consistentLines = 0;
                int firstLineDelims = -1;

                for (int i = 0; i < content.Length && lineCount < 5; i++)
                {
                    char ch = content[i];
                    if (ch == '"')
                    {
                        if (inQuotes && i + 1 < content.Length && content[i + 1] == '"')
                        {
                            i++;
                        }
                        else
                        {
                            inQuotes = !inQuotes;
                        }
                    }
                    else if (!inQuotes && ch == candidate)
                    {
                        currentLineDelims++;
                    }
                    else if (!inQuotes && (ch == '\n' || ch == '\r'))
                    {
                        if (ch == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                            i++;

                        if (currentLineDelims > 0)
                        {
                            if (firstLineDelims < 0)
                                firstLineDelims = currentLineDelims;
                            if (currentLineDelims == firstLineDelims)
                                consistentLines++;

                            totalDelims += currentLineDelims;
                        }
                        currentLineDelims = 0;
                        lineCount++;
                    }
                }

                if (currentLineDelims > 0)
                {
                    if (firstLineDelims < 0)
                        firstLineDelims = currentLineDelims;
                    if (currentLineDelims == firstLineDelims)
                        consistentLines++;
                    totalDelims += currentLineDelims;
                }

                int score = totalDelims + (consistentLines * 2);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestDelimiter = candidate;
                }
            }

            return bestDelimiter;
        }

        public static List<List<string>> ParseRawRows(string content, char delimiter)
        {
            var rows = new List<List<string>>();
            var currentRow = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            int length = content.Length;

            for (int i = 0; i < length; i++)
            {
                char ch = content[i];

                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < length && content[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(ch);
                    }
                }
                else
                {
                    if (ch == '"' && field.Length == 0)
                    {
                        inQuotes = true;
                    }
                    else if (ch == delimiter)
                    {
                        currentRow.Add(field.ToString());
                        field.Clear();
                    }
                    else if (ch == '\r' || ch == '\n')
                    {
                        if (ch == '\r' && i + 1 < length && content[i + 1] == '\n')
                            i++;

                        currentRow.Add(field.ToString());
                        field.Clear();
                        rows.Add(currentRow);
                        currentRow = new List<string>();
                    }
                    else
                    {
                        field.Append(ch);
                    }
                }
            }

            if (field.Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(field.ToString());
                rows.Add(currentRow);
            }

            return rows;
        }
    }
}
