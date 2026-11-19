using System;
using System.Collections.Generic;
using System.IO;
using CsvXlsxToSo.Editor.Generation;
using CsvXlsxToSo.Editor.Parsers;
using UnityEditor;
using UnityEngine;

namespace CsvXlsxToSo.Editor.Profiles
{
    public sealed class SpreadsheetAutoReimporter : AssetPostprocessor
    {
        private static bool _isReimporting;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (_isReimporting || importedAssets == null || importedAssets.Length == 0)
                return;

            var changedSpreadsheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string assetPath in importedAssets)
            {
                if (SpreadsheetLoader.IsSupportedFile(assetPath))
                    changedSpreadsheets.Add(Path.GetFullPath(assetPath));
            }

            if (changedSpreadsheets.Count == 0)
                return;

            List<SpreadsheetImportProfile> profiles = SpreadsheetImportProfile.FindAllProfiles();
            var profilesToRun = new List<SpreadsheetImportProfile>();

            foreach (SpreadsheetImportProfile profile in profiles)
            {
                if (!profile.autoReimportOnChange)
                    continue;

                string resolved = profile.ResolveSourcePath();
                if (!string.IsNullOrEmpty(resolved) && changedSpreadsheets.Contains(resolved))
                    profilesToRun.Add(profile);
            }

            if (profilesToRun.Count == 0)
                return;

            // Defer execution until after the current AssetDatabase import pass completes
            EditorApplication.delayCall += () =>
            {
                if (_isReimporting)
                    return;

                try
                {
                    _isReimporting = true;
                    foreach (SpreadsheetImportProfile profile in profilesToRun)
                    {
                        if (profile == null)
                            continue;

                        ImportReport report = profile.ExecuteImport();
                        Debug.Log($"[CsvXlsxToSo] Auto-reimported profile '{profile.name}': {report.GetSummaryLine()}");
                    }
                }
                finally
                {
                    _isReimporting = false;
                }
            };
        }
    }
}
