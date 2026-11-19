using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using CsvXlsxToSo.Editor.Parsers;
using NUnit.Framework;

namespace CsvXlsxToSo.Editor.Tests
{
    public sealed class XlsxParserTests
    {
        private string _tempXlsxPath;

        [SetUp]
        public void SetUp()
        {
            _tempXlsxPath = Path.Combine(Path.GetTempPath(), $"csv_xlsx_to_so_test_{System.Guid.NewGuid():N}.xlsx");
            CreateSampleXlsx(_tempXlsxPath);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempXlsxPath))
                File.Delete(_tempXlsxPath);
        }

        [Test]
        public void GetSheetNames_ReturnsAllSheets()
        {
            List<string> names = XlsxParser.GetSheetNames(_tempXlsxPath);
            CollectionAssert.AreEqual(new[] { "Items", "Metadata" }, names);
        }

        [Test]
        public void ParseWorkbook_ReadsSharedStringsSparseCellsBooleansAndDates()
        {
            SpreadsheetWorkbook wb = XlsxParser.ParseWorkbook(_tempXlsxPath);
            SpreadsheetSheet items = wb.GetSheet("Items");

            Assert.IsNotNull(items);
            Assert.AreEqual(5, items.ColumnCount);
            Assert.AreEqual(2, items.RowCount);

            // Row 0: shared string, number, bool, date serial (45658 = 2025-01-01), inline string
            Assert.AreEqual("potion_hp", items.GetCell(0, 0));
            Assert.AreEqual("50", items.GetCell(0, 1));
            Assert.AreEqual("true", items.GetCell(0, 2));
            Assert.AreEqual("2025-01-01", items.GetCell(0, 3));
            Assert.AreEqual("Heals HP", items.GetCell(0, 4));

            // Row 1: sparse cell (B3 skipped -> empty string, C3 = false, E3 = "Rare")
            Assert.AreEqual("elixir", items.GetCell(1, 0));
            Assert.AreEqual(string.Empty, items.GetCell(1, 1));
            Assert.AreEqual("false", items.GetCell(1, 2));
            Assert.AreEqual(string.Empty, items.GetCell(1, 3));
            Assert.AreEqual("Rare", items.GetCell(1, 4));

            SpreadsheetSheet meta = wb.GetSheet("Metadata");
            Assert.IsNotNull(meta);
            Assert.AreEqual(1, meta.RowCount);
            Assert.AreEqual("1.0", meta.GetCell(0, 1));
        }

        private static void CreateSampleXlsx(string path)
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

            AddEntry(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets>" +
                "<sheet name=\"Items\" sheetId=\"1\" r:id=\"rId1\"/>" +
                "<sheet name=\"Metadata\" sheetId=\"2\" r:id=\"rId2\"/>" +
                "</sheets></workbook>");

            AddEntry(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/>" +
                "</Relationships>");

            AddEntry(zip, "xl/sharedStrings.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"7\" uniqueCount=\"7\">" +
                "<si><t>Id</t></si>" +
                "<si><t>Cost</t></si>" +
                "<si><t>Consumable</t></si>" +
                "<si><t>ReleaseDate</t></si>" +
                "<si><t>Notes</t></si>" +
                "<si><t>potion_hp</t></si>" +
                "<si><t>elixir</t></si>" +
                "</sst>");

            AddEntry(zip, "xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<cellXfs count=\"2\">" +
                "<xf numFmtId=\"0\"/>" +
                "<xf numFmtId=\"14\"/>" +
                "</cellXfs></styleSheet>");

            AddEntry(zip, "xl/worksheets/sheet1.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
                "<row r=\"1\">" +
                "<c r=\"A1\" t=\"s\"><v>0</v></c>" +
                "<c r=\"B1\" t=\"s\"><v>1</v></c>" +
                "<c r=\"C1\" t=\"s\"><v>2</v></c>" +
                "<c r=\"D1\" t=\"s\"><v>3</v></c>" +
                "<c r=\"E1\" t=\"s\"><v>4</v></c>" +
                "</row>" +
                "<row r=\"2\">" +
                "<c r=\"A2\" t=\"s\"><v>5</v></c>" +
                "<c r=\"B2\"><v>50</v></c>" +
                "<c r=\"C2\" t=\"b\"><v>1</v></c>" +
                "<c r=\"D2\" s=\"1\"><v>45658</v></c>" +
                "<c r=\"E2\" t=\"inlineStr\"><is><t>Heals HP</t></is></c>" +
                "</row>" +
                "<row r=\"3\">" +
                "<c r=\"A3\" t=\"s\"><v>6</v></c>" +
                "<c r=\"C3\" t=\"b\"><v>0</v></c>" +
                "<c r=\"E3\" t=\"inlineStr\"><is><t>Rare</t></is></c>" +
                "</row>" +
                "</sheetData></worksheet>");

            AddEntry(zip, "xl/worksheets/sheet2.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
                "<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Key</t></is></c><c r=\"B1\" t=\"inlineStr\"><is><t>Value</t></is></c></row>" +
                "<row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>Version</t></is></c><c r=\"B2\" t=\"inlineStr\"><is><t>1.0</t></is></c></row>" +
                "</sheetData></worksheet>");
        }

        private static void AddEntry(ZipArchive zip, string entryName, string content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }
}
