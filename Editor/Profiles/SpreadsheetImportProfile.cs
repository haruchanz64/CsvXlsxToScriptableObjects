using System;
using System.Collections.Generic;
using System.IO;
using CsvXlsxToSo.Editor.Generation;
using CsvXlsxToSo.Editor.Parsers;
using UnityEditor;
using UnityEngine;

namespace CsvXlsxToSo.Editor.Profiles
{
    [CreateAssetMenu(fileName = "SpreadsheetImportProfile", menuName = "CSV & XLSX to ScriptableObjects/Import Profile")]
    public sealed class SpreadsheetImportProfile : ScriptableObject
    {
        [Header("Source")]
        public UnityEngine.Object sourceAsset;
        public string sourceFilePath;
        public string sheetName;
        public SpreadsheetParseOptions parseOptions = new SpreadsheetParseOptions();

        [Header("Target")]
        public string targetTypeAssemblyQualifiedName;
        public ImportOutputMode outputMode = ImportOutputMode.OneAssetPerRow;
        public string outputFolder = "Assets/GeneratedScriptableObjects";
        public string idColumnName;
        public string assetNamePrefix = string.Empty;
        public string assetNameSuffix = string.Empty;
        public string containerAssetName = "Database";
        public string containerListFieldName = "items";

        [Header("Options")]
        public bool updateExisting = true;
        public bool deleteOrphans = false;
        public bool autoReimportOnChange = false;
        public string listSeparator = "|";
        public string[] assetSearchFolders = Array.Empty<string>();

        [Header("Column Mappings")]
        public List<ColumnFieldMapping> mappings = new List<ColumnFieldMapping>();

        public string ResolveSourcePath()
        {
            if (sourceAsset != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(sourceAsset);
                if (!string.IsNullOrEmpty(assetPath))
                    return Path.GetFullPath(assetPath);
            }

            if (string.IsNullOrWhiteSpace(sourceFilePath))
                return string.Empty;

            if (Path.IsPathRooted(sourceFilePath))
                return sourceFilePath;

            return Path.GetFullPath(sourceFilePath);
        }

        public Type ResolveTargetType()
        {
            if (string.IsNullOrWhiteSpace(targetTypeAssemblyQualifiedName))
                return null;

            Type type = Type.GetType(targetTypeAssemblyQualifiedName);
            if (type != null)
                return type;

            // Fallback lookup by FullName across loaded assemblies
            foreach (Type candidate in FieldMappingUtility.GetAllScriptableObjectTypes())
            {
                if (string.Equals(candidate.FullName, targetTypeAssemblyQualifiedName, StringComparison.Ordinal) ||
                    string.Equals(candidate.AssemblyQualifiedName, targetTypeAssemblyQualifiedName, StringComparison.Ordinal))
                    return candidate;
            }

            return null;
        }

        public ImportReport ExecuteImport()
        {
            var report = new ImportReport();
            string fullPath = ResolveSourcePath();
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                report.AddMessage(ImportMessageSeverity.Error, $"Source spreadsheet not found: '{fullPath}'.");
                return report;
            }

            Type targetType = ResolveTargetType();
            if (targetType == null)
            {
                report.AddMessage(ImportMessageSeverity.Error, $"Target ScriptableObject type could not be resolved: '{targetTypeAssemblyQualifiedName}'.");
                return report;
            }

            SpreadsheetSheet sheet;
            try
            {
                sheet = SpreadsheetLoader.LoadSheet(fullPath, sheetName, parseOptions);
            }
            catch (Exception ex)
            {
                report.AddMessage(ImportMessageSeverity.Error, $"Failed to parse spreadsheet: {ex.Message}");
                return report;
            }

            List<ColumnFieldMapping> effectiveMappings = mappings != null && mappings.Count > 0
                ? mappings
                : FieldMappingUtility.AutoMapColumns(sheet.Headers, targetType);

            char separator = !string.IsNullOrEmpty(listSeparator) ? listSeparator[0] : '|';

            var request = new ImportRequest
            {
                Sheet = sheet,
                TargetType = targetType,
                OutputFolder = outputFolder,
                Mappings = effectiveMappings,
                IdColumnName = string.IsNullOrEmpty(idColumnName)
                    ? FieldMappingUtility.GuessIdColumn(sheet.Headers, targetType)
                    : idColumnName,
                AssetNamePrefix = assetNamePrefix,
                AssetNameSuffix = assetNameSuffix,
                OutputMode = outputMode,
                ContainerAssetName = containerAssetName,
                ContainerListFieldName = containerListFieldName,
                UpdateExisting = updateExisting,
                DeleteOrphans = deleteOrphans,
                ListSeparator = separator,
                AssetSearchFolders = assetSearchFolders
            };

            return ScriptableObjectGenerator.Execute(request);
        }

        public static List<SpreadsheetImportProfile> FindAllProfiles()
        {
            var profiles = new List<SpreadsheetImportProfile>();
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(SpreadsheetImportProfile)}");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<SpreadsheetImportProfile>(path);
                if (profile != null)
                    profiles.Add(profile);
            }
            return profiles;
        }
    }
}
