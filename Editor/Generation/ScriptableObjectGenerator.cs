using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CsvXlsxToSo.Editor.Conversion;
using CsvXlsxToSo.Editor.Parsers;
using UnityEditor;
using UnityEngine;

namespace CsvXlsxToSo.Editor.Generation
{
    public enum ImportOutputMode
    {
        OneAssetPerRow = 0,
        SingleContainerAsset = 1
    }

    public enum ImportMessageSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class ImportMessage
    {
        public ImportMessageSeverity Severity { get; }
        public int RowIndex { get; }
        public string ColumnName { get; }
        public string AssetPath { get; }
        public string Text { get; }

        public ImportMessage(ImportMessageSeverity severity, string text, int rowIndex = -1, string columnName = null, string assetPath = null)
        {
            Severity = severity;
            Text = text;
            RowIndex = rowIndex;
            ColumnName = columnName;
            AssetPath = assetPath;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(Severity).Append("] ");
            if (RowIndex >= 0)
                sb.Append($"Row {RowIndex + 1}");
            if (!string.IsNullOrEmpty(ColumnName))
                sb.Append(RowIndex >= 0 ? $", Col '{ColumnName}'" : $"Col '{ColumnName}'");
            if (RowIndex >= 0 || !string.IsNullOrEmpty(ColumnName))
                sb.Append(": ");
            sb.Append(Text);
            return sb.ToString();
        }
    }

    public sealed class ImportReport
    {
        public int CreatedCount { get; internal set; }
        public int UpdatedCount { get; internal set; }
        public int SkippedCount { get; internal set; }
        public int DeletedCount { get; internal set; }
        public List<ImportMessage> Messages { get; } = new List<ImportMessage>();

        public int WarningCount
        {
            get
            {
                int c = 0;
                foreach (ImportMessage m in Messages)
                    if (m.Severity == ImportMessageSeverity.Warning) c++;
                return c;
            }
        }

        public int ErrorCount
        {
            get
            {
                int c = 0;
                foreach (ImportMessage m in Messages)
                    if (m.Severity == ImportMessageSeverity.Error) c++;
                return c;
            }
        }

        public void AddMessage(ImportMessageSeverity severity, string text, int rowIndex = -1, string columnName = null, string assetPath = null)
        {
            Messages.Add(new ImportMessage(severity, text, rowIndex, columnName, assetPath));
        }

        public string GetSummaryLine()
        {
            return $"Created: {CreatedCount} | Updated: {UpdatedCount} | Skipped: {SkippedCount} | Deleted: {DeletedCount} | Warnings: {WarningCount} | Errors: {ErrorCount}";
        }
    }

    public sealed class ImportRequest
    {
        public SpreadsheetSheet Sheet;
        public Type TargetType;
        public string OutputFolder = "Assets/GeneratedScriptableObjects";
        public List<ColumnFieldMapping> Mappings = new List<ColumnFieldMapping>();
        public string IdColumnName;
        public string AssetNamePrefix = string.Empty;
        public string AssetNameSuffix = string.Empty;
        public ImportOutputMode OutputMode = ImportOutputMode.OneAssetPerRow;
        public string ContainerAssetName = "Database";
        public string ContainerListFieldName = "items";
        public bool UpdateExisting = true;
        public bool DeleteOrphans = false;
        public char ListSeparator = '|';
        public string[] AssetSearchFolders;
    }

    public static class ScriptableObjectGenerator
    {
        public static ImportReport Execute(ImportRequest request)
        {
            var report = new ImportReport();
            if (request == null)
            {
                report.AddMessage(ImportMessageSeverity.Error, "Import request is null.");
                return report;
            }

            if (request.Sheet == null)
            {
                report.AddMessage(ImportMessageSeverity.Error, "No spreadsheet data loaded.");
                return report;
            }

            if (request.TargetType == null || !typeof(ScriptableObject).IsAssignableFrom(request.TargetType) || request.TargetType.IsAbstract)
            {
                report.AddMessage(ImportMessageSeverity.Error, "Target type must be a concrete ScriptableObject subclass.");
                return report;
            }

            string outputFolder = NormalizeUnityFolder(request.OutputFolder);
            if (!outputFolder.StartsWith("Assets", StringComparison.Ordinal))
            {
                report.AddMessage(ImportMessageSeverity.Error, $"Output folder must be inside 'Assets/': '{request.OutputFolder}'.");
                return report;
            }

            EnsureFolderExists(outputFolder);

            var context = new CellConversionContext
            {
                ListSeparator = request.ListSeparator,
                AssetSearchFolders = request.AssetSearchFolders
            };

            try
            {
                AssetDatabase.StartAssetEditing();

                if (request.OutputMode == ImportOutputMode.SingleContainerAsset)
                {
                    ExecuteContainerMode(request, outputFolder, context, report);
                }
                else
                {
                    ExecuteOneAssetPerRowMode(request, outputFolder, context, report);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return report;
        }

        private static void ExecuteOneAssetPerRowMode(
            ImportRequest request,
            string outputFolder,
            CellConversionContext context,
            ImportReport report)
        {
            Dictionary<string, FieldInfo> fieldLookup = BuildFieldLookup(request.TargetType);
            List<(int colIndex, string colName, FieldInfo field)> activeBindings = ResolveBindings(request.Sheet, request.Mappings, fieldLookup, report);

            if (activeBindings.Count == 0)
            {
                report.AddMessage(ImportMessageSeverity.Warning, "No enabled column-to-field mappings found.");
                return;
            }

            int idColIndex = request.Sheet.GetColumnIndex(request.IdColumnName);
            var generatedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int r = 0; r < request.Sheet.RowCount; r++)
            {
                string rawId = idColIndex >= 0
                    ? request.Sheet.GetCell(r, idColIndex)
                    : $"{request.TargetType.Name}_{r + 1:D3}";

                if (string.IsNullOrWhiteSpace(rawId))
                {
                    report.SkippedCount++;
                    report.AddMessage(ImportMessageSeverity.Warning, "Empty ID cell; row skipped.", r, request.IdColumnName);
                    continue;
                }

                string fileName = SanitizeFileName($"{request.AssetNamePrefix}{rawId}{request.AssetNameSuffix}");
                string assetPath = $"{outputFolder}/{fileName}.asset";

                if (!generatedPaths.Add(assetPath))
                {
                    report.SkippedCount++;
                    report.AddMessage(ImportMessageSeverity.Warning, $"Duplicate asset name '{fileName}'; row skipped.", r, request.IdColumnName, assetPath);
                    continue;
                }

                ScriptableObject instance = AssetDatabase.LoadAssetAtPath(assetPath, request.TargetType) as ScriptableObject;
                bool existed = instance != null;

                if (existed && !request.UpdateExisting)
                {
                    report.SkippedCount++;
                    report.AddMessage(ImportMessageSeverity.Info, $"Asset '{fileName}' already exists and UpdateExisting is disabled; skipped.", r, null, assetPath);
                    continue;
                }

                if (!existed)
                {
                    instance = ScriptableObject.CreateInstance(request.TargetType);
                    instance.name = fileName;
                    ApplyRowToObject(instance, request.Sheet, r, activeBindings, context, report, assetPath);
                    AssetDatabase.CreateAsset(instance, assetPath);
                    report.CreatedCount++;
                }
                else
                {
                    ApplyRowToObject(instance, request.Sheet, r, activeBindings, context, report, assetPath);
                    EditorUtility.SetDirty(instance);
                    report.UpdatedCount++;
                }
            }

            if (request.DeleteOrphans)
            {
                string[] existingGuids = AssetDatabase.FindAssets($"t:{request.TargetType.Name}", new[] { outputFolder });
                foreach (string guid in existingGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.Equals(Path.GetDirectoryName(path)?.Replace('\\', '/'), outputFolder, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!generatedPaths.Contains(path))
                    {
                        if (AssetDatabase.DeleteAsset(path))
                        {
                            report.DeletedCount++;
                            report.AddMessage(ImportMessageSeverity.Info, $"Deleted orphan asset '{Path.GetFileName(path)}'.", assetPath: path);
                        }
                    }
                }
            }
        }

        private static void ExecuteContainerMode(
            ImportRequest request,
            string outputFolder,
            CellConversionContext context,
            ImportReport report)
        {
            Dictionary<string, FieldInfo> containerFields = BuildFieldLookup(request.TargetType);
            if (!containerFields.TryGetValue(request.ContainerListFieldName, out FieldInfo listField))
            {
                report.AddMessage(ImportMessageSeverity.Error, $"Container type '{request.TargetType.Name}' does not have a serializable list field named '{request.ContainerListFieldName}'.");
                return;
            }

            Type elementType = GetListOrArrayElementType(listField.FieldType);
            if (elementType == null)
            {
                report.AddMessage(ImportMessageSeverity.Error, $"Field '{request.ContainerListFieldName}' on '{request.TargetType.Name}' must be a List<T> or T[].");
                return;
            }

            Dictionary<string, FieldInfo> elementFieldLookup = BuildFieldLookup(elementType);
            List<(int colIndex, string colName, FieldInfo field)> activeBindings = ResolveBindings(request.Sheet, request.Mappings, elementFieldLookup, report);

            string containerFileName = SanitizeFileName(string.IsNullOrWhiteSpace(request.ContainerAssetName) ? request.TargetType.Name : request.ContainerAssetName);
            string containerPath = $"{outputFolder}/{containerFileName}.asset";

            ScriptableObject container = AssetDatabase.LoadAssetAtPath(containerPath, request.TargetType) as ScriptableObject;
            bool existed = container != null;

            if (!existed)
            {
                container = ScriptableObject.CreateInstance(request.TargetType);
                container.name = containerFileName;
                AssetDatabase.CreateAsset(container, containerPath);
                report.CreatedCount++;
            }
            else
            {
                report.UpdatedCount++;
            }

            bool elementIsScriptableObject = typeof(ScriptableObject).IsAssignableFrom(elementType);
            if (elementIsScriptableObject)
            {
                // Remove old sub-assets of this type before rebuilding
                UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(containerPath);
                foreach (UnityEngine.Object sub in subAssets)
                {
                    if (sub != null && sub != container && elementType.IsInstanceOfType(sub))
                        UnityEngine.Object.DestroyImmediate(sub, allowDestroyingAssets: true);
                }
            }

            var items = new List<object>(request.Sheet.RowCount);
            int idColIndex = request.Sheet.GetColumnIndex(request.IdColumnName);

            for (int r = 0; r < request.Sheet.RowCount; r++)
            {
                object rowObj = elementIsScriptableObject
                    ? ScriptableObject.CreateInstance(elementType)
                    : Activator.CreateInstance(elementType);

                if (rowObj is ScriptableObject subSo)
                {
                    string rowName = idColIndex >= 0 ? request.Sheet.GetCell(r, idColIndex) : $"{elementType.Name}_{r + 1:D3}";
                    subSo.name = string.IsNullOrWhiteSpace(rowName) ? $"{elementType.Name}_{r + 1:D3}" : SanitizeFileName(rowName);
                    AssetDatabase.AddObjectToAsset(subSo, container);
                }

                ApplyRowToObject(rowObj, request.Sheet, r, activeBindings, context, report, containerPath);
                items.Add(rowObj);
            }

            if (listField.FieldType.IsArray)
            {
                Array arr = Array.CreateInstance(elementType, items.Count);
                for (int i = 0; i < items.Count; i++)
                    arr.SetValue(items[i], i);
                listField.SetValue(container, arr);
            }
            else
            {
                IList list = (IList)Activator.CreateInstance(listField.FieldType);
                foreach (object item in items)
                    list.Add(item);
                listField.SetValue(container, list);
            }

            EditorUtility.SetDirty(container);
        }

        private static void ApplyRowToObject(
            object target,
            SpreadsheetSheet sheet,
            int rowIndex,
            List<(int colIndex, string colName, FieldInfo field)> bindings,
            CellConversionContext context,
            ImportReport report,
            string assetPath)
        {
            foreach ((int colIndex, string colName, FieldInfo field) in bindings)
            {
                string rawValue = sheet.GetCell(rowIndex, colIndex);
                if (ValueConverterRegistry.TryConvert(rawValue, field.FieldType, context, out object converted, out string error))
                {
                    field.SetValue(target, converted);
                }
                else
                {
                    report.AddMessage(ImportMessageSeverity.Warning, error, rowIndex, colName, assetPath);
                }
            }
        }

        private static Dictionary<string, FieldInfo> BuildFieldLookup(Type type)
        {
            var dict = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            foreach (SerializableFieldDescriptor desc in FieldMappingUtility.GetSerializableFields(type))
                dict[desc.FieldName] = desc.Field;
            return dict;
        }

        private static List<(int colIndex, string colName, FieldInfo field)> ResolveBindings(
            SpreadsheetSheet sheet,
            List<ColumnFieldMapping> mappings,
            Dictionary<string, FieldInfo> fieldLookup,
            ImportReport report)
        {
            var result = new List<(int, string, FieldInfo)>();
            if (mappings == null)
                return result;

            foreach (ColumnFieldMapping mapping in mappings)
            {
                if (!mapping.Enabled || string.IsNullOrEmpty(mapping.TargetFieldName))
                    continue;

                int colIdx = sheet.GetColumnIndex(mapping.ColumnName);
                if (colIdx < 0)
                {
                    report.AddMessage(ImportMessageSeverity.Warning, $"Mapped column '{mapping.ColumnName}' not found in spreadsheet headers.");
                    continue;
                }

                if (!fieldLookup.TryGetValue(mapping.TargetFieldName, out FieldInfo field))
                {
                    report.AddMessage(ImportMessageSeverity.Warning, $"Mapped field '{mapping.TargetFieldName}' not found on target type.", columnName: mapping.ColumnName);
                    continue;
                }

                result.Add((colIdx, mapping.ColumnName, field));
            }

            return result;
        }

        public static Type GetListOrArrayElementType(Type fieldType)
        {
            if (fieldType == null)
                return null;
            if (fieldType.IsArray && fieldType.GetArrayRank() == 1)
                return fieldType.GetElementType();
            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
                return fieldType.GetGenericArguments()[0];
            return null;
        }

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Unnamed";

            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char ch in name.Trim())
            {
                bool isBad = Array.IndexOf(invalid, ch) >= 0 || ch == '/' || ch == '\\';
                sb.Append(isBad ? '_' : ch);
            }

            string result = sb.ToString().Trim('.', ' ');
            return string.IsNullOrEmpty(result) ? "Unnamed" : result;
        }

        public static string NormalizeUnityFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return "Assets";
            return folder.Replace('\\', '/').TrimEnd('/');
        }

        public static void EnsureFolderExists(string unityFolderPath)
        {
            string normalized = NormalizeUnityFolder(unityFolderPath);
            if (AssetDatabase.IsValidFolder(normalized))
                return;

            string[] segments = normalized.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = $"{current}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }
    }
}
