using CsvXlsxToSo.Editor.Generation;
using CsvXlsxToSo.Editor.Profiles;
using UnityEditor;
using UnityEngine;

namespace CsvXlsxToSo.Editor.UI
{
    [CustomEditor(typeof(SpreadsheetImportProfile))]
    public sealed class SpreadsheetImportProfileEditor : UnityEditor.Editor
    {
        private ImportReport _lastReport;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(10f);
            var profile = (SpreadsheetImportProfile)target;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Import Now", GUILayout.Height(28f)))
                {
                    _lastReport = profile.ExecuteImport();
                    Debug.Log($"[CsvXlsxToSo] Profile '{profile.name}': {_lastReport.GetSummaryLine()}");
                }

                if (GUILayout.Button("Open in Converter Window", GUILayout.Height(28f)))
                {
                    SpreadsheetToSoWindow.OpenWithProfile(profile);
                }
            }

            if (_lastReport != null)
            {
                EditorGUILayout.Space(4f);
                MessageType msgType = _lastReport.ErrorCount > 0
                    ? MessageType.Error
                    : (_lastReport.WarningCount > 0 ? MessageType.Warning : MessageType.Info);
                EditorGUILayout.HelpBox(_lastReport.GetSummaryLine(), msgType);
            }
        }
    }
}
