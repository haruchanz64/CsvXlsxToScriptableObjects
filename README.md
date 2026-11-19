# CSV & XLSX to ScriptableObjects (Unity Plugin)

A zero-dependency Unity Editor package that converts `.csv`, `.tsv`, and `.xlsx` (Excel) spreadsheets into Unity `ScriptableObject` assets.

## Features

- **Zero External Dependencies:** Pure C# RFC 4180 CSV/TSV parser and OpenXML (`.xlsx`) reader using built-in `System.IO.Compression` and `System.Xml.Linq`. No NuGet packages or third-party DLLs required.
- **Multi-Sheet XLSX Support:** Inspect and select any worksheet inside `.xlsx` workbooks, including shared strings, inline strings, booleans, numbers, Excel date serials, cached formula results, and sparse cells.
- **Two Workflows:**
  - **Map to Existing `ScriptableObject`:** Auto-maps columns to serialized fields (with `[SpreadsheetColumn]`, `[SpreadsheetId]`, and `[SpreadsheetIgnore]` attributes) plus manual dropdown overrides.
  - **Generate New `ScriptableObject` Class:** Infers field types from spreadsheet data (or an optional type-hint row) and generates a clean `.cs` ScriptableObject script (plus an optional Database/Container class).
- **Rich Type Support:**
  - Primitives: `string`, `int`, `long`, `short`, `byte`, `float`, `double`, `decimal`, `bool` (`true`/`false`/`1`/`0`/`yes`/`no`)
  - Enums (case-insensitive names, integers, and `[Flags]`)
  - Unity types: `Vector2`, `Vector3`, `Vector4`, `Vector2Int`, `Vector3Int`, `Color` (hex `#RRGGBB`/`#RRGGBBAA` or `r,g,b,a`), `Color32`
  - Collections: `T[]` and `List<T>` using a configurable separator (default `|`)
  - `UnityEngine.Object` references (`Sprite`, `Texture2D`, `GameObject`, `AudioClip`, `Material`, other `ScriptableObject`s) resolved by asset path, GUID, `Resources` path, or asset name
  - Custom converters via `ICellValueConverter`
- **Non-Destructive Re-Imports:** Updates existing `.asset` files in place so Unity GUIDs and all scene/prefab references remain intact.
- **Reusable Import Profiles & Auto-Reimport:** Save configurations as `SpreadsheetImportProfile` assets, batch re-import all profiles, or enable automatic re-importing whenever a tracked spreadsheet inside `Assets/` is saved.

## Installation (Unity Package Manager)

1. Open **Window > Package Manager** in Unity.
2. Click **+** (top-left) > **Add package from git URL...**
3. Enter:
   ```
   https://github.com/haruchanz64/CsvXlsxToScriptableObjects.git
   ```

## Quick Start

1. Open **Tools > CSV & XLSX to ScriptableObjects > Converter Window** (or right-click any `.csv`/`.xlsx` file in the Project window and choose **Convert to ScriptableObjects...**).
2. Drag in a `.csv` or `.xlsx` file (or click **Browse...** to pick a file from anywhere on disk).
3. Choose either:
   - **Map to Existing ScriptableObject** to map columns to your existing `ScriptableObject` fields and click **Convert Spreadsheet to ScriptableObjects**.
   - **Generate New ScriptableObject Class** to generate a `.cs` class from your columns first.
4. Click **Save Profile** to store your mapping as a reusable `SpreadsheetImportProfile` asset.

## Attributes

```csharp
using CsvXlsxToSo;
using UnityEngine;

public class ItemData : ScriptableObject
{
    [SpreadsheetId]
    public string id;

    [SpreadsheetColumn("Display Name")]
    public string displayName;

    [SpreadsheetIgnore]
    public int runtimeOnlyCache;
}
```

## Custom Converters

Implement `ICellValueConverter` in any Editor assembly; it is discovered automatically via `UnityEditor.TypeCache`:

```csharp
using System;
using CsvXlsxToSo.Editor.Conversion;

public sealed class MyCustomConverter : ICellValueConverter
{
    public bool CanConvert(Type targetType) => targetType == typeof(MyStruct);

    public bool TryConvert(string rawValue, Type targetType, CellConversionContext context, out object result, out string error)
    {
        // Parse rawValue into MyStruct
        result = default(MyStruct);
        error = null;
        return true;
    }
}
```

## License

MIT — see [LICENSE.md](LICENSE.md).
