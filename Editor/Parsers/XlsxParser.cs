using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace CsvXlsxToSo.Editor.Parsers
{
    /// <summary>
    /// Zero-dependency pure C# XLSX (OpenXML) parser using ZipArchive and XDocument.
    /// Supports multi-sheet workbooks, shared strings, inline strings, booleans,
    /// numbers, Excel date serials, cached formula results, and sparse cells.
    /// </summary>
    public static class XlsxParser
    {
        private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        private sealed class SheetDescriptor
        {
            public string Name;
            public string TargetPath;
        }

        public static List<string> GetSheetNames(string filePath)
        {
            ValidateFile(filePath);
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            List<SheetDescriptor> descriptors = ReadSheetDescriptors(archive, out _);
            var names = new List<string>(descriptors.Count);
            foreach (SheetDescriptor d in descriptors)
                names.Add(d.Name);
            return names;
        }

        public static SpreadsheetWorkbook ParseWorkbook(string filePath, SpreadsheetParseOptions options = null)
        {
            ValidateFile(filePath);
            options ??= new SpreadsheetParseOptions();

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            List<SheetDescriptor> descriptors = ReadSheetDescriptors(archive, out bool use1904DateSystem);
            List<string> sharedStrings = ReadSharedStrings(archive);
            HashSet<int> dateStyleIndices = ReadDateStyleIndices(archive);

            var sheetNames = new List<string>(descriptors.Count);
            var sheets = new Dictionary<string, SpreadsheetSheet>(StringComparer.OrdinalIgnoreCase);

            foreach (SheetDescriptor descriptor in descriptors)
            {
                sheetNames.Add(descriptor.Name);
                List<List<string>> rawRows = ReadWorksheetRows(archive, descriptor.TargetPath, sharedStrings, dateStyleIndices, use1904DateSystem);
                sheets[descriptor.Name] = SpreadsheetTableBuilder.BuildSheet(descriptor.Name, rawRows, options);
            }

            return new SpreadsheetWorkbook(filePath, sheetNames, sheets);
        }

        public static SpreadsheetSheet ParseSheet(string filePath, string sheetName = null, SpreadsheetParseOptions options = null)
        {
            ValidateFile(filePath);
            options ??= new SpreadsheetParseOptions();

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            List<SheetDescriptor> descriptors = ReadSheetDescriptors(archive, out bool use1904DateSystem);
            if (descriptors.Count == 0)
                throw new InvalidDataException($"No worksheets found in XLSX file: {filePath}");

            SheetDescriptor target = null;
            if (!string.IsNullOrEmpty(sheetName))
            {
                foreach (SheetDescriptor d in descriptors)
                {
                    if (string.Equals(d.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                    {
                        target = d;
                        break;
                    }
                }
            }

            target ??= descriptors[0];

            List<string> sharedStrings = ReadSharedStrings(archive);
            HashSet<int> dateStyleIndices = ReadDateStyleIndices(archive);
            List<List<string>> rawRows = ReadWorksheetRows(archive, target.TargetPath, sharedStrings, dateStyleIndices, use1904DateSystem);

            return SpreadsheetTableBuilder.BuildSheet(target.Name, rawRows, options);
        }

        private static void ValidateFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"XLSX file not found: {filePath}", filePath);
        }

        private static ZipArchiveEntry FindEntry(ZipArchive archive, string normalizedPath)
        {
            string target = normalizedPath.TrimStart('/').Replace('\\', '/');
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string entryPath = entry.FullName.TrimStart('/').Replace('\\', '/');
                if (string.Equals(entryPath, target, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }

        private static XDocument LoadXml(ZipArchive archive, string path)
        {
            ZipArchiveEntry entry = FindEntry(archive, path);
            if (entry == null)
                return null;

            using Stream entryStream = entry.Open();
            return XDocument.Load(entryStream);
        }

        private static List<SheetDescriptor> ReadSheetDescriptors(ZipArchive archive, out bool use1904DateSystem)
        {
            use1904DateSystem = false;
            var result = new List<SheetDescriptor>();

            XDocument workbookDoc = LoadXml(archive, "xl/workbook.xml");
            if (workbookDoc?.Root == null)
                throw new InvalidDataException("Invalid XLSX archive: missing xl/workbook.xml.");

            XElement workbookPr = workbookDoc.Root.Element(MainNs + "workbookPr");
            if (workbookPr != null)
            {
                string date1904 = (string)workbookPr.Attribute("date1904");
                use1904DateSystem = date1904 == "1" || string.Equals(date1904, "true", StringComparison.OrdinalIgnoreCase);
            }

            var relMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            XDocument relsDoc = LoadXml(archive, "xl/_rels/workbook.xml.rels");
            if (relsDoc?.Root != null)
            {
                foreach (XElement rel in relsDoc.Root.Elements(PkgRelNs + "Relationship"))
                {
                    string id = (string)rel.Attribute("Id");
                    string target = (string)rel.Attribute("Target");
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                    {
                        relMap[id] = ResolveWorkbookTarget(target);
                    }
                }
            }

            XElement sheetsElem = workbookDoc.Root.Element(MainNs + "sheets");
            if (sheetsElem == null)
                return result;

            int fallbackIndex = 1;
            foreach (XElement sheetElem in sheetsElem.Elements(MainNs + "sheet"))
            {
                string name = (string)sheetElem.Attribute("name") ?? $"Sheet{fallbackIndex}";
                string relId = (string)sheetElem.Attribute(RelNs + "id");
                string targetPath = null;

                if (!string.IsNullOrEmpty(relId) && relMap.TryGetValue(relId, out string mapped))
                    targetPath = mapped;
                else
                    targetPath = $"xl/worksheets/sheet{fallbackIndex}.xml";

                result.Add(new SheetDescriptor
                {
                    Name = name,
                    TargetPath = targetPath
                });
                fallbackIndex++;
            }

            return result;
        }

        private static string ResolveWorkbookTarget(string target)
        {
            string clean = target.Replace('\\', '/');
            if (clean.StartsWith("/"))
                return clean.TrimStart('/');
            if (clean.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                return clean;
            return "xl/" + clean.TrimStart('/');
        }

        private static List<string> ReadSharedStrings(ZipArchive archive)
        {
            var strings = new List<string>();
            XDocument sstDoc = LoadXml(archive, "xl/sharedStrings.xml");
            if (sstDoc?.Root == null)
                return strings;

            foreach (XElement si in sstDoc.Root.Elements(MainNs + "si"))
            {
                XElement tElem = si.Element(MainNs + "t");
                if (tElem != null)
                {
                    strings.Add(tElem.Value);
                }
                else
                {
                    var sb = new StringBuilder();
                    foreach (XElement run in si.Elements(MainNs + "r"))
                    {
                        XElement runText = run.Element(MainNs + "t");
                        if (runText != null)
                            sb.Append(runText.Value);
                    }
                    strings.Add(sb.ToString());
                }
            }

            return strings;
        }

        private static HashSet<int> ReadDateStyleIndices(ZipArchive archive)
        {
            var dateStyles = new HashSet<int>();
            XDocument stylesDoc = LoadXml(archive, "xl/styles.xml");
            if (stylesDoc?.Root == null)
                return dateStyles;

            var customFormats = new Dictionary<int, string>();
            XElement numFmts = stylesDoc.Root.Element(MainNs + "numFmts");
            if (numFmts != null)
            {
                foreach (XElement numFmt in numFmts.Elements(MainNs + "numFmt"))
                {
                    if (int.TryParse((string)numFmt.Attribute("numFmtId"), out int id))
                    {
                        customFormats[id] = (string)numFmt.Attribute("formatCode") ?? string.Empty;
                    }
                }
            }

            XElement cellXfs = stylesDoc.Root.Element(MainNs + "cellXfs");
            if (cellXfs == null)
                return dateStyles;

            int xfIndex = 0;
            foreach (XElement xf in cellXfs.Elements(MainNs + "xf"))
            {
                if (int.TryParse((string)xf.Attribute("numFmtId"), out int numFmtId))
                {
                    if (IsBuiltInDateFormat(numFmtId) ||
                        (customFormats.TryGetValue(numFmtId, out string fmt) && IsCustomDateFormat(fmt)))
                    {
                        dateStyles.Add(xfIndex);
                    }
                }
                xfIndex++;
            }

            return dateStyles;
        }

        private static bool IsBuiltInDateFormat(int numFmtId)
        {
            // Standard OpenXML built-in date/time format IDs
            return (numFmtId >= 14 && numFmtId <= 22) || (numFmtId >= 45 && numFmtId <= 47);
        }

        private static bool IsCustomDateFormat(string formatCode)
        {
            if (string.IsNullOrEmpty(formatCode))
                return false;

            // Strip quoted literals and bracketed locale/color tokens
            var cleaned = new StringBuilder(formatCode.Length);
            bool inQuotes = false;
            bool inBrackets = false;
            foreach (char ch in formatCode)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (inQuotes)
                    continue;
                if (ch == '[')
                {
                    inBrackets = true;
                    continue;
                }
                if (ch == ']')
                {
                    inBrackets = false;
                    continue;
                }
                if (!inBrackets)
                    cleaned.Append(char.ToLowerInvariant(ch));
            }

            string s = cleaned.ToString();
            return s.Contains("yy") || s.Contains("dd") || s.Contains("hh") || s.Contains("mm");
        }

        private static List<List<string>> ReadWorksheetRows(
            ZipArchive archive,
            string targetPath,
            List<string> sharedStrings,
            HashSet<int> dateStyleIndices,
            bool use1904DateSystem)
        {
            var rows = new List<List<string>>();
            XDocument sheetDoc = LoadXml(archive, targetPath);
            if (sheetDoc?.Root == null)
                return rows;

            XElement sheetData = sheetDoc.Root.Element(MainNs + "sheetData");
            if (sheetData == null)
                return rows;

            int expectedRowNumber = 1;
            foreach (XElement rowElem in sheetData.Elements(MainNs + "row"))
            {
                if (int.TryParse((string)rowElem.Attribute("r"), out int rowNumber))
                {
                    while (expectedRowNumber < rowNumber)
                    {
                        rows.Add(new List<string>());
                        expectedRowNumber++;
                    }
                }

                var rowCells = new List<string>();
                int nextColIndex = 0;

                foreach (XElement cellElem in rowElem.Elements(MainNs + "c"))
                {
                    string cellRef = (string)cellElem.Attribute("r");
                    int colIndex = !string.IsNullOrEmpty(cellRef)
                        ? ParseColumnIndex(cellRef)
                        : nextColIndex;

                    while (rowCells.Count < colIndex)
                        rowCells.Add(string.Empty);

                    string value = ExtractCellValue(cellElem, sharedStrings, dateStyleIndices, use1904DateSystem);
                    if (colIndex < rowCells.Count)
                        rowCells[colIndex] = value;
                    else
                        rowCells.Add(value);

                    nextColIndex = colIndex + 1;
                }

                rows.Add(rowCells);
                expectedRowNumber++;
            }

            return rows;
        }

        private static string ExtractCellValue(
            XElement cellElem,
            List<string> sharedStrings,
            HashSet<int> dateStyleIndices,
            bool use1904DateSystem)
        {
            string cellType = (string)cellElem.Attribute("t");

            if (cellType == "inlineStr")
            {
                XElement isElem = cellElem.Element(MainNs + "is");
                if (isElem == null)
                    return string.Empty;

                XElement tElem = isElem.Element(MainNs + "t");
                if (tElem != null)
                    return tElem.Value;

                var sb = new StringBuilder();
                foreach (XElement r in isElem.Elements(MainNs + "r"))
                {
                    XElement rt = r.Element(MainNs + "t");
                    if (rt != null)
                        sb.Append(rt.Value);
                }
                return sb.ToString();
            }

            XElement vElem = cellElem.Element(MainNs + "v");
            if (vElem == null)
                return string.Empty;

            string raw = vElem.Value;

            if (cellType == "s")
            {
                if (int.TryParse(raw, out int idx) && idx >= 0 && idx < sharedStrings.Count)
                    return sharedStrings[idx];
                return string.Empty;
            }

            if (cellType == "b")
            {
                return raw == "1" ? "true" : "false";
            }

            if (cellType == "str" || cellType == "e")
            {
                return raw;
            }

            // Numeric or Date value
            string styleAttr = (string)cellElem.Attribute("s");
            if (!string.IsNullOrEmpty(styleAttr) &&
                int.TryParse(styleAttr, out int styleIndex) &&
                dateStyleIndices.Contains(styleIndex) &&
                double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double serial))
            {
                DateTime dt = FromExcelSerialDate(serial, use1904DateSystem);
                return dt.TimeOfDay == TimeSpan.Zero
                    ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }

            return raw;
        }

        private static DateTime FromExcelSerialDate(double serial, bool use1904DateSystem)
        {
            if (use1904DateSystem)
                return new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddDays(serial);

            // Account for Excel's 1900 leap-year bug (serial 60 = Feb 29, 1900)
            DateTime baseDate = new DateTime(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);
            if (serial < 60)
                baseDate = new DateTime(1899, 12, 31, 0, 0, 0, DateTimeKind.Unspecified);

            return baseDate.AddDays(serial);
        }

        internal static int ParseColumnIndex(string cellReference)
        {
            int col = 0;
            for (int i = 0; i < cellReference.Length; i++)
            {
                char ch = cellReference[i];
                if (ch >= 'A' && ch <= 'Z')
                    col = col * 26 + (ch - 'A' + 1);
                else if (ch >= 'a' && ch <= 'z')
                    col = col * 26 + (ch - 'a' + 1);
                else
                    break;
            }
            return Math.Max(0, col - 1);
        }
    }
}
