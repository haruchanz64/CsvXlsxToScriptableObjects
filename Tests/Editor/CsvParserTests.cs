using CsvXlsxToSo.Editor.Parsers;
using NUnit.Framework;

namespace CsvXlsxToSo.Editor.Tests
{
    public sealed class CsvParserTests
    {
        [Test]
        public void ParseString_HandlesQuotedFieldsEscapedQuotesAndNewlines()
        {
            const string csv = "\uFEFFId,Name,Description\r\n" +
                               "sword_01,\"Iron, \"\"Long\"\" Sword\",\"Line 1\nLine 2\"\r\n" +
                               "# comment row should be skipped\r\n" +
                               "shield_01,Wooden Shield,Basic defense\r\n";

            SpreadsheetSheet sheet = CsvParser.ParseString(csv, "Items");

            Assert.AreEqual(3, sheet.ColumnCount);
            Assert.AreEqual(2, sheet.RowCount);
            Assert.AreEqual("Id", sheet.Headers[0]);
            Assert.AreEqual("Iron, \"Long\" Sword", sheet.GetCell(0, 1));
            Assert.AreEqual("Line 1\nLine 2", sheet.GetCell(0, 2));
            Assert.AreEqual("shield_01", sheet.GetCell(1, 0));
        }

        [Test]
        public void ParseString_AutoDetectsSemicolonDelimiter()
        {
            const string csv = "Id;Price;Active\nitem_1;150;true\nitem_2;250;false";

            SpreadsheetSheet sheet = CsvParser.ParseString(csv);

            Assert.AreEqual(3, sheet.ColumnCount);
            Assert.AreEqual(2, sheet.RowCount);
            Assert.AreEqual("150", sheet.GetCell(0, 1));
            Assert.AreEqual("false", sheet.GetCell(1, 2));
        }

        [Test]
        public void ParseString_ReadsTypeHintRow()
        {
            const string csv = "Id,Damage,Tags\nstring,int,List<string>\naxe_01,42,Melee|Heavy";
            var opts = new SpreadsheetParseOptions { HeaderRowIndex = 0, TypeHintRowIndex = 1 };

            SpreadsheetSheet sheet = CsvParser.ParseString(csv, "Weapons", opts);

            Assert.AreEqual(1, sheet.RowCount);
            Assert.AreEqual("int", sheet.TypeHints[1]);
            Assert.AreEqual("42", sheet.GetCell(0, 1));
        }
    }
}
