using System.Collections.Generic;
using CsvXlsxToSo.Editor.Conversion;
using CsvXlsxToSo.Editor.Generation;
using CsvXlsxToSo.Editor.Parsers;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CsvXlsxToSo.Editor.Tests
{
    public enum TestRarity
    {
        Common,
        Rare,
        Epic
    }

    public sealed class TestItemSO : ScriptableObject
    {
        [SpreadsheetId]
        public string id;
        [SpreadsheetColumn("Display Name")]
        public string displayName;
        public int value;
        public float weight;
        public bool stackable;
        public TestRarity rarity;
        public Color tint;
        public Vector3 offset;
        public List<string> tags;
    }

    public sealed class ValueConverterAndGeneratorTests
    {
        private string _tempAssetFolder;

        [SetUp]
        public void SetUp()
        {
            _tempAssetFolder = $"Assets/CsvXlsxToSo_TempTests_{System.Guid.NewGuid():N}";
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(_tempAssetFolder))
                AssetDatabase.DeleteAsset(_tempAssetFolder);
        }

        [Test]
        public void ValueConverter_ConvertsPrimitivesEnumsVectorsColorsAndLists()
        {
            var ctx = new CellConversionContext { ListSeparator = '|' };

            Assert.IsTrue(ValueConverterRegistry.TryConvert("yes", typeof(bool), ctx, out object b, out _));
            Assert.AreEqual(true, b);

            Assert.IsTrue(ValueConverterRegistry.TryConvert("epic", typeof(TestRarity), ctx, out object e, out _));
            Assert.AreEqual(TestRarity.Epic, e);

            Assert.IsTrue(ValueConverterRegistry.TryConvert("1.5, 2.0, -3.5", typeof(Vector3), ctx, out object v3, out _));
            Assert.AreEqual(new Vector3(1.5f, 2f, -3.5f), v3);

            Assert.IsTrue(ValueConverterRegistry.TryConvert("#FF0000", typeof(Color), ctx, out object col, out _));
            Assert.AreEqual(Color.red, col);

            Assert.IsTrue(ValueConverterRegistry.TryConvert("10|20|30", typeof(int[]), ctx, out object arr, out _));
            CollectionAssert.AreEqual(new[] { 10, 20, 30 }, (int[])arr);
        }

        [Test]
        public void Generator_CreatesAndNonDestructivelyUpdatesAssetsPreservingGuid()
        {
            const string csvV1 =
                "id,Display Name,value,weight,stackable,rarity,tint,offset,tags\n" +
                "gem_01,Ruby,100,0.5,true,Rare,#FF0000,\"1,2,3\",Gem|Valuable\n";

            SpreadsheetSheet sheetV1 = CsvParser.ParseString(csvV1, "Items");
            var mappings = FieldMappingUtility.AutoMapColumns(sheetV1.Headers, typeof(TestItemSO));

            ImportReport reportV1 = ScriptableObjectGenerator.Execute(new ImportRequest
            {
                Sheet = sheetV1,
                TargetType = typeof(TestItemSO),
                OutputFolder = _tempAssetFolder,
                Mappings = mappings,
                IdColumnName = "id",
                UpdateExisting = true
            });

            Assert.AreEqual(1, reportV1.CreatedCount);
            Assert.AreEqual(0, reportV1.ErrorCount);

            string assetPath = $"{_tempAssetFolder}/gem_01.asset";
            string guidBefore = AssetDatabase.AssetPathToGUID(assetPath);
            var assetV1 = AssetDatabase.LoadAssetAtPath<TestItemSO>(assetPath);
            Assert.IsNotNull(assetV1);
            Assert.AreEqual("Ruby", assetV1.displayName);
            Assert.AreEqual(100, assetV1.value);
            Assert.AreEqual(TestRarity.Rare, assetV1.rarity);
            CollectionAssert.AreEqual(new[] { "Gem", "Valuable" }, assetV1.tags);

            // Re-import with updated values — GUID must remain identical
            const string csvV2 =
                "id,Display Name,value,weight,stackable,rarity,tint,offset,tags\n" +
                "gem_01,Flawless Ruby,500,0.5,true,Epic,#FF0000,\"1,2,3\",Gem|Legendary\n";

            SpreadsheetSheet sheetV2 = CsvParser.ParseString(csvV2, "Items");
            ImportReport reportV2 = ScriptableObjectGenerator.Execute(new ImportRequest
            {
                Sheet = sheetV2,
                TargetType = typeof(TestItemSO),
                OutputFolder = _tempAssetFolder,
                Mappings = mappings,
                IdColumnName = "id",
                UpdateExisting = true
            });

            Assert.AreEqual(0, reportV2.CreatedCount);
            Assert.AreEqual(1, reportV2.UpdatedCount);
            Assert.AreEqual(guidBefore, AssetDatabase.AssetPathToGUID(assetPath));

            var assetV2 = AssetDatabase.LoadAssetAtPath<TestItemSO>(assetPath);
            Assert.AreEqual("Flawless Ruby", assetV2.displayName);
            Assert.AreEqual(500, assetV2.value);
            Assert.AreEqual(TestRarity.Epic, assetV2.rarity);
        }

        [Test]
        public void CodeGenerator_InfersTypesAndGeneratesValidClassSource()
        {
            const string csv = "Item ID,Attack Power,Crit Chance,Is Unique,Accent Color,Tags\n" +
                               "blade_1,25,0.15,true,#00FF00,Melee|Sharp\n" +
                               "bow_1,18,0.25,false,#0000FF,Ranged\n";

            SpreadsheetSheet sheet = CsvParser.ParseString(csv, "Weapons");
            List<GeneratedFieldSpec> specs = ScriptableObjectCodeGenerator.InferFields(sheet);

            Assert.AreEqual("string", specs[0].TypeName);
            Assert.AreEqual("int", specs[1].TypeName);
            Assert.AreEqual("float", specs[2].TypeName);
            Assert.AreEqual("bool", specs[3].TypeName);
            Assert.AreEqual("Color", specs[4].TypeName);
            Assert.AreEqual("List<string>", specs[5].TypeName);

            string code = ScriptableObjectCodeGenerator.GenerateSourceCode(new CodeGenerationRequest
            {
                ClassName = "WeaponData",
                Namespace = "Game.Data",
                GenerateContainerClass = true,
                Fields = specs
            });

            StringAssert.Contains("public class WeaponData : ScriptableObject", code);
            StringAssert.Contains("public class WeaponDataDatabase : ScriptableObject", code);
            StringAssert.Contains("[SpreadsheetColumn(\"Attack Power\")]", code);
            StringAssert.Contains("public int attackPower;", code);
        }
    }
}
