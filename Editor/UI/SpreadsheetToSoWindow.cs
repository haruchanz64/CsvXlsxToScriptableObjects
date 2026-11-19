using System;
using System.Collections.Generic;
using System.IO;
using CsvXlsxToSo.Editor.Generation;
using CsvXlsxToSo.Editor.Parsers;
using CsvXlsxToSo.Editor.Profiles;
using UnityEditor;
using UnityEngine;

namespace CsvXlsxToSo.Editor.UI
{
    public sealed class SpreadsheetToSoWindow : EditorWindow
    {
        private const int MaxPreviewRows = 15;

        private enum ConversionMode
        {
            MapToExisting = 0,
            GenerateNewClass = 1
        }

        // Profile
        private SpreadsheetImportProfile _profile;

        // Source & Parsing
        private UnityEngine.Object _sourceAsset;
        private string _sourceFilePath = string.Empty;
        private List<string> _sheetNames = new List<string>();
        private int _selectedSheetIndex;
        private SpreadsheetParseOptions _parseOptions = new SpreadsheetParseOptions();
        private SpreadsheetSheet _loadedSheet;
        private string _loadError;

        // Mode & Existing SO Mapping
        private ConversionMode _mode = ConversionMode.MapToExisting;
        private List<Type> _soTypes = new List<Type>();
        private string[] _soTypeNames = Array.Empty<string>();
        private int _selectedTypeIndex = -1;
        private ImportOutputMode _outputMode = ImportOutputMode.OneAssetPerRow;
        private string _outputFolder = "Assets/GeneratedScriptableObjects";
        private string _idColumnName = string.Empty;
        private string _assetNamePrefix = string.Empty;
        private string _assetNameSuffix = string.Empty;
        private string _containerAssetName = "Database";
        private string _containerListFieldName = "items";
        private bool _updateExisting = true;
        private bool _deleteOrphans = false;
        private bool _autoReimportOnChange = false;
        private string _listSeparator = "|";
        private List<ColumnFieldMapping> _mappings = new List<ColumnFieldMapping>();

        // Code Generation
        private string _genClassName = "GeneratedData";
        private string _genNamespace = string.Empty;
        private string _genOutputFolder = "Assets/Scripts/Generated";
        private string _genMenuPath = "Data/GeneratedData";
        private bool _genContainerClass = false;
        private string _genContainerClassName = string.Empty;
        private List<GeneratedFieldSpec> _genFields = new List<GeneratedFieldSpec>();

        // UI State
        private Vector2 _mainScroll;
        private Vector2 _previewScroll;
        private Vector2 _reportScroll;
        private bool _showParseOptions;
        private bool _showPreview = true;
        private ImportReport _lastReport;

        [MenuItem("Tools/CSV & XLSX to ScriptableObjects/Converter Window", priority = 100)]
        public static void OpenWindow()
        {
            var window = GetWindow<SpreadsheetToSoWindow>("CSV/XLSX to SO");
            window.minSize = new Vector2(680f, 520f);
            window.Show();
        }

        [MenuItem("Tools/CSV & XLSX to ScriptableObjects/Re-import All Profiles", priority = 101)]
        public static void ReimportAllProfiles()
        {
            List<SpreadsheetImportProfile> profiles = SpreadsheetImportProfile.FindAllProfiles();
            if (profiles.Count == 0)
            {
                EditorUtility.DisplayDialog("CSV & XLSX to ScriptableObjects", "No SpreadsheetImportProfile assets found in the project.", "OK");
                return;
            }

            int totalCreated = 0, totalUpdated = 0, totalErrors = 0;
            foreach (SpreadsheetImportProfile profile in profiles)
            {
                ImportReport report = profile.ExecuteImport();
                totalCreated += report.CreatedCount;
                totalUpdated += report.UpdatedCount;
                totalErrors += report.ErrorCount;
                Debug.Log($"[CsvXlsxToSo] Profile '{profile.name}': {report.GetSummaryLine()}");
            }

            EditorUtility.DisplayDialog(
                "Re-import Complete",
                $"Processed {profiles.Count} profile(s).\nCreated: {totalCreated}\nUpdated: {totalUpdated}\nErrors: {totalErrors}",
                "OK");
        }

        [MenuItem("Assets/Convert to ScriptableObjects...", isValidateFunction: false, priority = 1200)]
        private static void OpenFromSelectedAsset()
        {
            UnityEngine.Object selected = Selection.activeObject;
            if (selected == null)
                return;

            var window = GetWindow<SpreadsheetToSoWindow>("CSV/XLSX to SO");
            window.minSize = new Vector2(680f, 520f);
            window.LoadFromAsset(selected);
            window.Show();
        }

        [MenuItem("Assets/Convert to ScriptableObjects...", isValidateFunction: true)]
        private static bool ValidateOpenFromSelectedAsset()
        {
            if (Selection.activeObject == null)
                return false;

            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return SpreadsheetLoader.IsSupportedFile(path);
        }

        public static void OpenWithProfile(SpreadsheetImportProfile profile)
        {
            var window = GetWindow<SpreadsheetToSoWindow>("CSV/XLSX to SO");
            window.minSize = new Vector2(680f, 520f);
            window.ApplyProfile(profile);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshScriptableObjectTypes();
        }

        private void RefreshScriptableObjectTypes()
        {
            Type previouslySelected = GetSelectedTargetType();
            _soTypes = FieldMappingUtility.GetAllScriptableObjectTypes();
            _soTypeNames = new string[_soTypes.Count];
            _selectedTypeIndex = -1;

            for (int i = 0; i < _soTypes.Count; i++)
            {
                _soTypeNames[i] = string.IsNullOrEmpty(_soTypes[i].Namespace)
                    ? _soTypes[i].Name
                    : $"{_soTypes[i].Name}  ({_soTypes[i].Namespace})";

                if (previouslySelected != null && _soTypes[i] == previouslySelected)
                    _selectedTypeIndex = i;
            }
        }

        private Type GetSelectedTargetType()
        {
            if (_selectedTypeIndex >= 0 && _selectedTypeIndex < _soTypes.Count)
                return _soTypes[_selectedTypeIndex];
            return null;
        }

        public void LoadFromAsset(UnityEngine.Object asset)
        {
            _sourceAsset = asset;
            _sourceFilePath = asset != null ? AssetDatabase.GetAssetPath(asset) : string.Empty;
            ReloadSpreadsheet(resetMappings: true);
        }

        public void ApplyProfile(SpreadsheetImportProfile profile)
        {
            _profile = profile;
            if (profile == null)
                return;

            _sourceAsset = profile.sourceAsset;
            _sourceFilePath = profile.sourceFilePath;
            _parseOptions = profile.parseOptions != null ? profile.parseOptions.Clone() : new SpreadsheetParseOptions();
            _outputMode = profile.outputMode;
            _outputFolder = profile.outputFolder;
            _idColumnName = profile.idColumnName;
            _assetNamePrefix = profile.assetNamePrefix;
            _assetNameSuffix = profile.assetNameSuffix;
            _containerAssetName = profile.containerAssetName;
            _containerListFieldName = profile.containerListFieldName;
            _updateExisting = profile.updateExisting;
            _deleteOrphans = profile.deleteOrphans;
            _autoReimportOnChange = profile.autoReimportOnChange;
            _listSeparator = string.IsNullOrEmpty(profile.listSeparator) ? "|" : profile.listSeparator;

            RefreshScriptableObjectTypes();
            Type resolved = profile.ResolveTargetType();
            _selectedTypeIndex = resolved != null ? _soTypes.IndexOf(resolved) : -1;

            ReloadSpreadsheet(resetMappings: false);

            if (!string.IsNullOrEmpty(profile.sheetName) && _sheetNames.Count > 0)
            {
                int idx = _sheetNames.FindIndex(s => string.Equals(s, profile.sheetName, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0 && idx != _selectedSheetIndex)
                {
                    _selectedSheetIndex = idx;
                    ReloadSelectedSheet(resetMappings: false);
                }
            }

            if (profile.mappings != null && profile.mappings.Count > 0)
            {
                _mappings = new List<ColumnFieldMapping>();
                foreach (ColumnFieldMapping m in profile.mappings)
                    _mappings.Add(new ColumnFieldMapping(m.ColumnName, m.TargetFieldName, m.Enabled));
            }
        }

        private void OnGUI()
        {
            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll);

            DrawProfileBar();
            EditorGUILayout.Space(6f);
            DrawSourceSection();

            if (!string.IsNullOrEmpty(_loadError))
            {
                EditorGUILayout.HelpBox(_loadError, MessageType.Error);
            }

            if (_loadedSheet != null)
            {
                EditorGUILayout.Space(6f);
                DrawPreviewSection();
                EditorGUILayout.Space(6f);
                DrawModeTabs();
            }

            if (_lastReport != null)
            {
                EditorGUILayout.Space(6f);
                DrawReportSection();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawProfileBar()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Import Profile", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var newProfile = (SpreadsheetImportProfile)EditorGUILayout.ObjectField(
                        "Profile Asset",
                        _profile,
                        typeof(SpreadsheetImportProfile),
                        false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        ApplyProfile(newProfile);
                    }

                    if (GUILayout.Button("Save Profile", GUILayout.Width(95f)))
                        SaveCurrentProfile();

                    if (GUILayout.Button("Re-import All", GUILayout.Width(95f)))
                        ReimportAllProfiles();
                }
            }
        }

        private void DrawSourceSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("1. Source Spreadsheet (.csv / .tsv / .xlsx)", EditorStyles.boldLabel);

                // Project asset slot with drag-and-drop
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    UnityEngine.Object picked = EditorGUILayout.ObjectField("Project Asset", _sourceAsset, typeof(UnityEngine.Object), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (picked == null)
                        {
                            _sourceAsset = null;
                        }
                        else
                        {
                            string path = AssetDatabase.GetAssetPath(picked);
                            if (SpreadsheetLoader.IsSupportedFile(path))
                            {
                                _sourceAsset = picked;
                                _sourceFilePath = path;
                                ReloadSpreadsheet(resetMappings: true);
                            }
                            else
                            {
                                _loadError = "Selected asset must be a .csv, .tsv, or .xlsx file.";
                            }
                        }
                    }

                    if (GUILayout.Button("Browse...", GUILayout.Width(80f)))
                    {
                        string selected = EditorUtility.OpenFilePanel("Select CSV or XLSX Spreadsheet", Application.dataPath, "csv,tsv,xlsx");
                        if (!string.IsNullOrEmpty(selected))
                        {
                            _sourceAsset = null;
                            _sourceFilePath = ToProjectRelativeIfInsideAssets(selected);
                            if (_sourceFilePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                                _sourceAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(_sourceFilePath);
                            ReloadSpreadsheet(resetMappings: true);
                        }
                    }

                    if (GUILayout.Button("Reload", GUILayout.Width(65f)))
                    {
                        ReloadSpreadsheet(resetMappings: false);
                    }
                }

                // Manual / external path display
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    _sourceFilePath = EditorGUILayout.TextField("File Path", _sourceFilePath);
                    if (EditorGUI.EndChangeCheck() && File.Exists(ResolveCurrentSourcePath()))
                    {
                        ReloadSpreadsheet(resetMappings: true);
                    }
                }

                // Sheet selector for XLSX
                if (_sheetNames.Count > 1)
                {
                    EditorGUI.BeginChangeCheck();
                    _selectedSheetIndex = EditorGUILayout.Popup("Worksheet", _selectedSheetIndex, _sheetNames.ToArray());
                    if (EditorGUI.EndChangeCheck())
                    {
                        ReloadSelectedSheet(resetMappings: true);
                    }
                }

                // Parse options foldout
                _showParseOptions = EditorGUILayout.Foldout(_showParseOptions, "Parse Options", true);
                if (_showParseOptions)
                {
                    EditorGUI.indentLevel++;
                    EditorGUI.BeginChangeCheck();
                    _parseOptions.HeaderRowIndex = Math.Max(0, EditorGUILayout.IntField("Header Row Index (0-based)", _parseOptions.HeaderRowIndex));
                    _parseOptions.TypeHintRowIndex = EditorGUILayout.IntField("Type Hint Row Index (-1 = none)", _parseOptions.TypeHintRowIndex);
                    _parseOptions.TrimWhitespace = EditorGUILayout.Toggle("Trim Cell Whitespace", _parseOptions.TrimWhitespace);
                    _parseOptions.SkipEmptyRows = EditorGUILayout.Toggle("Skip Empty Rows", _parseOptions.SkipEmptyRows);
                    _parseOptions.CommentPrefix = EditorGUILayout.TextField("Comment Row Prefix", _parseOptions.CommentPrefix);

                    if (!SpreadsheetLoader.IsXlsx(_sourceFilePath))
                    {
                        _parseOptions.AutoDetectDelimiter = EditorGUILayout.Toggle("Auto-Detect Delimiter", _parseOptions.AutoDetectDelimiter);
                        using (new EditorGUI.DisabledScope(_parseOptions.AutoDetectDelimiter))
                        {
                            string delimStr = EditorGUILayout.TextField("CSV Delimiter", _parseOptions.Delimiter.ToString());
                            if (!string.IsNullOrEmpty(delimStr))
                                _parseOptions.Delimiter = delimStr[0];
                        }
                    }

                    if (EditorGUI.EndChangeCheck() && _loadedSheet != null)
                    {
                        ReloadSelectedSheet(resetMappings: false);
                    }
                    EditorGUI.indentLevel--;
                }
            }
        }

        private void DrawPreviewSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _showPreview = EditorGUILayout.Foldout(
                    _showPreview,
                    $"2. Data Preview — Sheet '{_loadedSheet.Name}' ({_loadedSheet.RowCount} rows, {_loadedSheet.ColumnCount} columns)",
                    true,
                    EditorStyles.foldoutHeader);

                if (!_showPreview)
                    return;

                int previewCount = Math.Min(_loadedSheet.RowCount, MaxPreviewRows);
                float colWidth = 130f;

                _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.Height(Mathf.Min(200f, 36f + previewCount * 20f)));

                // Header row
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    GUILayout.Label("#", EditorStyles.boldLabel, GUILayout.Width(32f));
                    for (int c = 0; c < _loadedSheet.ColumnCount; c++)
                    {
                        GUILayout.Label(_loadedSheet.Headers[c], EditorStyles.boldLabel, GUILayout.Width(colWidth));
                    }
                }

                // Data rows
                for (int r = 0; r < previewCount; r++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label((r + 1).ToString(), EditorStyles.miniLabel, GUILayout.Width(32f));
                        for (int c = 0; c < _loadedSheet.ColumnCount; c++)
                        {
                            string cell = _loadedSheet.GetCell(r, c).Replace("\n", " ");
                            GUILayout.Label(cell, EditorStyles.label, GUILayout.Width(colWidth));
                        }
                    }
                }

                if (_loadedSheet.RowCount > MaxPreviewRows)
                {
                    EditorGUILayout.LabelField($"... and {_loadedSheet.RowCount - MaxPreviewRows} more rows.", EditorStyles.miniLabel);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawModeTabs()
        {
            _mode = (ConversionMode)GUILayout.Toolbar(
                (int)_mode,
                new[] { "Map to Existing ScriptableObject", "Generate New ScriptableObject Class" },
                GUILayout.Height(26f));

            EditorGUILayout.Space(4f);

            if (_mode == ConversionMode.MapToExisting)
                DrawExistingMappingMode();
            else
                DrawCodeGenerationMode();
        }

        private void DrawExistingMappingMode()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("3. Target ScriptableObject & Output Settings", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    _selectedTypeIndex = EditorGUILayout.Popup("Target SO Type", _selectedTypeIndex, _soTypeNames);
                    if (EditorGUI.EndChangeCheck())
                    {
                        RebuildMappingsForCurrentType();
                    }

                    if (GUILayout.Button("Refresh", GUILayout.Width(65f)))
                        RefreshScriptableObjectTypes();
                }

                Type targetType = GetSelectedTargetType();

                _outputMode = (ImportOutputMode)EditorGUILayout.EnumPopup("Output Mode", _outputMode);

                using (new EditorGUILayout.HorizontalScope())
                {
                    _outputFolder = EditorGUILayout.TextField("Output Folder", _outputFolder);
                    if (GUILayout.Button("Select", GUILayout.Width(65f)))
                    {
                        string picked = EditorUtility.OpenFolderPanel("Select Output Folder Inside Assets", Application.dataPath, "");
                        if (!string.IsNullOrEmpty(picked))
                            _outputFolder = ToProjectRelativeIfInsideAssets(picked);
                    }
                }

                if (_outputMode == ImportOutputMode.OneAssetPerRow)
                {
                    if (_loadedSheet.Headers.Count > 0)
                    {
                        string[] headerArr = new string[_loadedSheet.Headers.Count];
                        for (int i = 0; i < _loadedSheet.Headers.Count; i++)
                            headerArr[i] = _loadedSheet.Headers[i];

                        int idIdx = Math.Max(0, Array.FindIndex(headerArr, h => string.Equals(h, _idColumnName, StringComparison.OrdinalIgnoreCase)));
                        idIdx = EditorGUILayout.Popup("ID / Asset Name Column", idIdx, headerArr);
                        _idColumnName = headerArr[idIdx];
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _assetNamePrefix = EditorGUILayout.TextField("Asset Name Prefix", _assetNamePrefix);
                        _assetNameSuffix = EditorGUILayout.TextField("Suffix", _assetNameSuffix);
                    }

                    _updateExisting = EditorGUILayout.Toggle("Update Existing Assets (Preserve GUIDs)", _updateExisting);
                    _deleteOrphans = EditorGUILayout.Toggle("Delete Orphan Assets in Output Folder", _deleteOrphans);
                }
                else
                {
                    _containerAssetName = EditorGUILayout.TextField("Container Asset Name", _containerAssetName);
                    _containerListFieldName = EditorGUILayout.TextField("Container List Field Name", _containerListFieldName);
                }

                _listSeparator = EditorGUILayout.TextField("List/Array Element Separator", _listSeparator);
                _autoReimportOnChange = EditorGUILayout.Toggle("Auto-Reimport When Saved (Profile)", _autoReimportOnChange);

                EditorGUILayout.Space(6f);

                // Column-to-field mapping table
                Type mappingTargetType = ResolveMappingTargetType(targetType);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("4. Column-to-Field Mappings", EditorStyles.boldLabel);
                    if (GUILayout.Button("Auto-Map", GUILayout.Width(85f)))
                        RebuildMappingsForCurrentType();
                }

                if (mappingTargetType == null)
                {
                    EditorGUILayout.HelpBox("Select a valid target ScriptableObject type to configure column mappings.", MessageType.Info);
                }
                else
                {
                    List<SerializableFieldDescriptor> fields = FieldMappingUtility.GetSerializableFields(mappingTargetType);
                    var fieldOptions = new List<string> { "(Unmapped)" };
                    var fieldNames = new List<string> { string.Empty };
                    foreach (SerializableFieldDescriptor f in fields)
                    {
                        fieldOptions.Add($"{f.FieldName}  ({f.FieldType.Name})");
                        fieldNames.Add(f.FieldName);
                    }

                    string[] fieldOptionsArr = fieldOptions.ToArray();

                    for (int i = 0; i < _mappings.Count; i++)
                    {
                        ColumnFieldMapping m = _mappings[i];
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            m.Enabled = EditorGUILayout.Toggle(m.Enabled, GUILayout.Width(20f));
                            EditorGUILayout.LabelField(m.ColumnName, GUILayout.Width(180f));
                            GUILayout.Label("→", GUILayout.Width(18f));

                            int currentIdx = Math.Max(0, fieldNames.IndexOf(m.TargetFieldName ?? string.Empty));
                            int nextIdx = EditorGUILayout.Popup(currentIdx, fieldOptionsArr);
                            if (nextIdx != currentIdx)
                            {
                                m.TargetFieldName = fieldNames[nextIdx];
                                m.Enabled = nextIdx > 0;
                            }
                        }
                    }
                }

                EditorGUILayout.Space(8f);
                using (new EditorGUI.DisabledScope(targetType == null))
                {
                    if (GUILayout.Button("Convert Spreadsheet to ScriptableObjects", GUILayout.Height(32f)))
                    {
                        RunConversion();
                    }
                }
            }
        }

        private void DrawCodeGenerationMode()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("3. Generate New ScriptableObject C# Class", EditorStyles.boldLabel);

                _genClassName = EditorGUILayout.TextField("Class Name", _genClassName);
                _genNamespace = EditorGUILayout.TextField("Namespace (Optional)", _genNamespace);
                _genMenuPath = EditorGUILayout.TextField("CreateAssetMenu Path", _genMenuPath);

                using (new EditorGUILayout.HorizontalScope())
                {
                    _genOutputFolder = EditorGUILayout.TextField("Script Output Folder", _genOutputFolder);
                    if (GUILayout.Button("Select", GUILayout.Width(65f)))
                    {
                        string picked = EditorUtility.OpenFolderPanel("Select Script Output Folder", Application.dataPath, "");
                        if (!string.IsNullOrEmpty(picked))
                            _genOutputFolder = ToProjectRelativeIfInsideAssets(picked);
                    }
                }

                _genContainerClass = EditorGUILayout.Toggle("Also Generate Container Database Class", _genContainerClass);
                if (_genContainerClass)
                {
                    _genContainerClassName = EditorGUILayout.TextField("Container Class Name", string.IsNullOrWhiteSpace(_genContainerClassName) ? $"{_genClassName}Database" : _genContainerClassName);
                }

                EditorGUILayout.Space(6f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Inferred Fields", EditorStyles.boldLabel);
                    if (GUILayout.Button("Re-Infer Types", GUILayout.Width(105f)))
                        RebuildInferredFields();
                }

                for (int i = 0; i < _genFields.Count; i++)
                {
                    GeneratedFieldSpec spec = _genFields[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        spec.Include = EditorGUILayout.Toggle(spec.Include, GUILayout.Width(20f));
                        EditorGUILayout.LabelField(spec.ColumnName, GUILayout.Width(140f));
                        spec.FieldName = EditorGUILayout.TextField(spec.FieldName, GUILayout.Width(150f));
                        spec.TypeName = EditorGUILayout.TextField(spec.TypeName, GUILayout.Width(110f));
                        spec.IsIdField = GUILayout.Toggle(spec.IsIdField, "ID", "Button", GUILayout.Width(36f));
                    }
                }

                EditorGUILayout.Space(8f);
                if (GUILayout.Button("Generate C# Script", GUILayout.Height(32f)))
                {
                    var req = new CodeGenerationRequest
                    {
                        ClassName = _genClassName,
                        Namespace = _genNamespace,
                        OutputFolder = _genOutputFolder,
                        CreateAssetMenuPath = _genMenuPath,
                        GenerateContainerClass = _genContainerClass,
                        ContainerClassName = _genContainerClassName,
                        Fields = _genFields
                    };

                    string path = ScriptableObjectCodeGenerator.WriteScriptFile(req);
                    EditorUtility.DisplayDialog(
                        "Script Generated",
                        $"Generated ScriptableObject class at:\n{path}\n\nWait for Unity to finish compiling, then switch to 'Map to Existing ScriptableObject' to generate the .asset files.",
                        "OK");
                    _mode = ConversionMode.MapToExisting;
                }
            }
        }

        private void DrawReportSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                MessageType msgType = _lastReport.ErrorCount > 0
                    ? MessageType.Error
                    : (_lastReport.WarningCount > 0 ? MessageType.Warning : MessageType.Info);

                EditorGUILayout.HelpBox(_lastReport.GetSummaryLine(), msgType);

                if (_lastReport.Messages.Count > 0)
                {
                    _reportScroll = EditorGUILayout.BeginScrollView(_reportScroll, GUILayout.MaxHeight(150f));
                    foreach (ImportMessage msg in _lastReport.Messages)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(msg.ToString(), EditorStyles.wordWrappedMiniLabel);
                            if (!string.IsNullOrEmpty(msg.AssetPath) && GUILayout.Button("Ping", GUILayout.Width(45f)))
                            {
                                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(msg.AssetPath);
                                if (asset != null)
                                    EditorGUIUtility.PingObject(asset);
                            }
                        }
                    }
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void ReloadSpreadsheet(bool resetMappings)
        {
            _loadError = null;
            _loadedSheet = null;
            _sheetNames.Clear();
            _selectedSheetIndex = 0;

            string fullPath = ResolveCurrentSourcePath();
            if (string.IsNullOrEmpty(fullPath))
                return;

            if (!File.Exists(fullPath))
            {
                _loadError = $"File not found: {fullPath}";
                return;
            }

            try
            {
                _sheetNames = SpreadsheetLoader.GetSheetNames(fullPath);
                ReloadSelectedSheet(resetMappings);
            }
            catch (Exception ex)
            {
                _loadError = $"Failed to read spreadsheet: {ex.Message}";
            }
        }

        private void ReloadSelectedSheet(bool resetMappings)
        {
            _loadError = null;
            string fullPath = ResolveCurrentSourcePath();
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
                return;

            try
            {
                string sheetName = (_selectedSheetIndex >= 0 && _selectedSheetIndex < _sheetNames.Count)
                    ? _sheetNames[_selectedSheetIndex]
                    : null;

                _loadedSheet = SpreadsheetLoader.LoadSheet(fullPath, sheetName, _parseOptions);
                _genClassName = ScriptableObjectCodeGenerator.ToValidIdentifier(_loadedSheet.Name, lowerCamelCase: false);
                _genMenuPath = $"Data/{_genClassName}";

                RebuildInferredFields();

                if (resetMappings || _mappings.Count == 0)
                {
                    RebuildMappingsForCurrentType();
                }
            }
            catch (Exception ex)
            {
                _loadError = $"Failed to parse sheet: {ex.Message}";
            }
        }

        private void RebuildMappingsForCurrentType()
        {
            if (_loadedSheet == null)
                return;

            Type mappingTargetType = ResolveMappingTargetType(GetSelectedTargetType());
            _mappings = FieldMappingUtility.AutoMapColumns(_loadedSheet.Headers, mappingTargetType);
            _idColumnName = FieldMappingUtility.GuessIdColumn(_loadedSheet.Headers, mappingTargetType);
        }

        private void RebuildInferredFields()
        {
            if (_loadedSheet == null)
                return;

            char sep = !string.IsNullOrEmpty(_listSeparator) ? _listSeparator[0] : '|';
            _genFields = ScriptableObjectCodeGenerator.InferFields(_loadedSheet, sep);
        }

        private Type ResolveMappingTargetType(Type targetType)
        {
            if (targetType == null)
                return null;

            if (_outputMode == ImportOutputMode.OneAssetPerRow)
                return targetType;

            foreach (SerializableFieldDescriptor f in FieldMappingUtility.GetSerializableFields(targetType))
            {
                if (string.Equals(f.FieldName, _containerListFieldName, StringComparison.Ordinal))
                    return ScriptableObjectGenerator.GetListOrArrayElementType(f.FieldType);
            }
            return null;
        }

        private void RunConversion()
        {
            Type targetType = GetSelectedTargetType();
            char sep = !string.IsNullOrEmpty(_listSeparator) ? _listSeparator[0] : '|';

            var request = new ImportRequest
            {
                Sheet = _loadedSheet,
                TargetType = targetType,
                OutputFolder = _outputFolder,
                Mappings = _mappings,
                IdColumnName = _idColumnName,
                AssetNamePrefix = _assetNamePrefix,
                AssetNameSuffix = _assetNameSuffix,
                OutputMode = _outputMode,
                ContainerAssetName = _containerAssetName,
                ContainerListFieldName = _containerListFieldName,
                UpdateExisting = _updateExisting,
                DeleteOrphans = _deleteOrphans,
                ListSeparator = sep
            };

            _lastReport = ScriptableObjectGenerator.Execute(request);
            Debug.Log($"[CsvXlsxToSo] Import finished — {_lastReport.GetSummaryLine()}");
        }

        private void SaveCurrentProfile()
        {
            if (_profile == null)
            {
                string savePath = EditorUtility.SaveFilePanelInProject(
                    "Save Spreadsheet Import Profile",
                    $"{(_loadedSheet != null ? _loadedSheet.Name : "Spreadsheet")}ImportProfile",
                    "asset",
                    "Choose a location to save the import profile asset.");

                if (string.IsNullOrEmpty(savePath))
                    return;

                _profile = CreateInstance<SpreadsheetImportProfile>();
                AssetDatabase.CreateAsset(_profile, savePath);
            }

            _profile.sourceAsset = _sourceAsset;
            _profile.sourceFilePath = _sourceFilePath;
            _profile.sheetName = (_selectedSheetIndex >= 0 && _selectedSheetIndex < _sheetNames.Count) ? _sheetNames[_selectedSheetIndex] : string.Empty;
            _profile.parseOptions = _parseOptions.Clone();
            _profile.targetTypeAssemblyQualifiedName = GetSelectedTargetType()?.AssemblyQualifiedName ?? string.Empty;
            _profile.outputMode = _outputMode;
            _profile.outputFolder = _outputFolder;
            _profile.idColumnName = _idColumnName;
            _profile.assetNamePrefix = _assetNamePrefix;
            _profile.assetNameSuffix = _assetNameSuffix;
            _profile.containerAssetName = _containerAssetName;
            _profile.containerListFieldName = _containerListFieldName;
            _profile.updateExisting = _updateExisting;
            _profile.deleteOrphans = _deleteOrphans;
            _profile.autoReimportOnChange = _autoReimportOnChange;
            _profile.listSeparator = _listSeparator;
            _profile.mappings = new List<ColumnFieldMapping>();
            foreach (ColumnFieldMapping m in _mappings)
                _profile.mappings.Add(new ColumnFieldMapping(m.ColumnName, m.TargetFieldName, m.Enabled));

            EditorUtility.SetDirty(_profile);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(_profile);
        }

        private string ResolveCurrentSourcePath()
        {
            if (_sourceAsset != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(_sourceAsset);
                if (!string.IsNullOrEmpty(assetPath))
                    return Path.GetFullPath(assetPath);
            }

            if (string.IsNullOrWhiteSpace(_sourceFilePath))
                return string.Empty;

            return Path.IsPathRooted(_sourceFilePath)
                ? _sourceFilePath
                : Path.GetFullPath(_sourceFilePath);
        }

        private static string ToProjectRelativeIfInsideAssets(string absolutePath)
        {
            string normPath = absolutePath.Replace('\\', '/');
            string normDataPath = Application.dataPath.Replace('\\', '/');
            if (normPath.StartsWith(normDataPath, StringComparison.OrdinalIgnoreCase))
            {
                return "Assets" + normPath.Substring(normDataPath.Length);
            }
            return absolutePath;
        }
    }
}
