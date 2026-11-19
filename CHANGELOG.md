# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-11-19
### Added
- Zero-dependency RFC 4180 CSV parser with delimiter auto-detection, quoted fields, multi-line cells, and comment row support.
- Zero-dependency pure C# XLSX (OpenXML) parser supporting multi-sheet workbooks, shared strings, inline strings, booleans, numbers, Excel dates, cached formula values, and sparse cells.
- Rich type conversion registry supporting primitives, enums (including `[Flags]`), `Vector2`/`Vector3`/`Vector4`/`Vector2Int`/`Vector3Int`, `Color`/`Color32`, `DateTime`, `Guid`, `T[]`, `List<T>`, and `UnityEngine.Object` asset references.
- Extensible `ICellValueConverter` interface for custom project types.
- Non-destructive ScriptableObject asset generation and updating (preserves GUIDs and scene/prefab references) in both One-Asset-Per-Row and Single-Container modes.
- Automatic C# `ScriptableObject` class generation with type inference from spreadsheet data or type-hint rows.
- Reusable `SpreadsheetImportProfile` assets and optional `AssetPostprocessor` auto-reimport.
- Unity Editor Window (`Tools/CSV & XLSX to ScriptableObjects/Converter Window`) with live preview, column mapping UI, and validation reporting.
- EditMode unit test suite and Basic Example sample.
