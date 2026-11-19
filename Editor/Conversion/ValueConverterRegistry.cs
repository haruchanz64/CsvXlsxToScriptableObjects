using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace CsvXlsxToSo.Editor.Conversion
{
    public static class ValueConverterRegistry
    {
        private static readonly List<ICellValueConverter> CustomConverters = new List<ICellValueConverter>();
        private static bool _discovered;

        public static void RegisterConverter(ICellValueConverter converter)
        {
            if (converter != null && !CustomConverters.Contains(converter))
                CustomConverters.Insert(0, converter);
        }

        private static void EnsureCustomConvertersDiscovered()
        {
            if (_discovered)
                return;
            _discovered = true;

            foreach (Type type in TypeCache.GetTypesDerivedFrom<ICellValueConverter>())
            {
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                try
                {
                    if (Activator.CreateInstance(type) is ICellValueConverter instance)
                        CustomConverters.Add(instance);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CsvXlsxToSo] Failed to instantiate custom converter '{type.FullName}': {ex.Message}");
                }
            }
        }

        public static bool TryConvert(
            string rawValue,
            Type targetType,
            CellConversionContext context,
            out object result,
            out string error)
        {
            context ??= new CellConversionContext();
            error = null;
            result = null;

            if (targetType == null)
            {
                error = "Target type is null.";
                return false;
            }

            EnsureCustomConvertersDiscovered();

            // 1. Custom converters take precedence
            for (int i = 0; i < CustomConverters.Count; i++)
            {
                ICellValueConverter converter = CustomConverters[i];
                if (converter.CanConvert(targetType))
                    return converter.TryConvert(rawValue, targetType, context, out result, out error);
            }

            // 2. Handle Nullable<T>
            Type nullableUnderlying = Nullable.GetUnderlyingType(targetType);
            if (nullableUnderlying != null)
            {
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    result = null;
                    return true;
                }
                return TryConvert(rawValue, nullableUnderlying, context, out result, out error);
            }

            // 3. String
            if (targetType == typeof(string))
            {
                result = rawValue ?? string.Empty;
                return true;
            }

            // 4. Collections: T[] and List<T>
            if (targetType.IsArray && targetType.GetArrayRank() == 1)
            {
                return TryConvertArray(rawValue, targetType.GetElementType(), context, out result, out error);
            }

            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type elementType = targetType.GetGenericArguments()[0];
                return TryConvertList(rawValue, targetType, elementType, context, out result, out error);
            }

            // 5. UnityEngine.Object asset references
            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
            {
                return TryResolveUnityObject(rawValue, targetType, context, out result, out error);
            }

            string trimmed = (rawValue ?? string.Empty).Trim();

            // Empty cells fall back to default(T) for value types
            if (string.IsNullOrEmpty(trimmed))
            {
                result = targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
                return true;
            }

            // 6. Enums
            if (targetType.IsEnum)
            {
                return TryConvertEnum(trimmed, targetType, context, out result, out error);
            }

            CultureInfo culture = context.Culture ?? CultureInfo.InvariantCulture;

            // 7. Primitives & common .NET structs
            if (targetType == typeof(bool))
                return TryConvertBool(trimmed, out result, out error);

            if (targetType == typeof(int))
            {
                if (int.TryParse(trimmed, NumberStyles.Integer, culture, out int v)) { result = v; return true; }
                if (double.TryParse(trimmed, NumberStyles.Float, culture, out double dv) && Math.Abs(dv - Math.Round(dv)) < 1e-6)
                {
                    result = (int)Math.Round(dv);
                    return true;
                }
                error = $"Cannot parse '{trimmed}' as int.";
                return false;
            }

            if (targetType == typeof(long))
            {
                if (long.TryParse(trimmed, NumberStyles.Integer, culture, out long v)) { result = v; return true; }
                error = $"Cannot parse '{trimmed}' as long.";
                return false;
            }

            if (targetType == typeof(short))
            {
                if (short.TryParse(trimmed, NumberStyles.Integer, culture, out short v)) { result = v; return true; }
                error = $"Cannot parse '{trimmed}' as short.";
                return false;
            }

            if (targetType == typeof(byte))
            {
                if (byte.TryParse(trimmed, NumberStyles.Integer, culture, out byte v)) { result = v; return true; }
                error = $"Cannot parse '{trimmed}' as byte.";
                return false;
            }

            if (targetType == typeof(sbyte))
            {
                if (sbyte.TryParse(trimmed, NumberStyles.Integer, culture, out sbyte v)) { result = v; return true; }
                error = $"Cannot parse '{trimmed}' as sbyte.";
                return false;
            }

            if (targetType == typeof(uint))
            {
                if (uint.TryParse(trimmed, NumberStyles.Integer, culture, out uint v)) { result = v; return true; }
                error = $"Cannot parse '{trimmed}' as uint.";
                return false;
            }

            if (targetType == typeof(ulong))
            {
                if (ulong.TryParse(trimmed, NumberStyles.Integer, culture, out ulong v)) { result = v; return true; }
                error = $"Cannot parse '{trimmed}' as ulong.";
                return false;
            }

            if (targetType == typeof(ushort))
            {
                if (ushort.TryParse(trimmed, NumberStyles.Integer, culture, out ushort v)) { result = v; return true; }
                error = $"Cannot parse '{trimmed}' as ushort.";
                return false;
            }

            if (targetType == typeof(float))
            {
                if (float.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, culture, out float v)) { result = v; return true; }
                error = $"Cannot parse '{trimmed}' as float.";
                return false;
            }

            if (targetType == typeof(double))
            {
                if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, culture, out double v)) { result = v; return true; }
                error = $"Cannot parse '{trimmed}' as double.";
                return false;
            }

            if (targetType == typeof(decimal))
            {
                if (decimal.TryParse(trimmed, NumberStyles.Number, culture, out decimal v)) { result = v; return true; }
                error = $"Cannot parse '{trimmed}' as decimal.";
                return false;
            }

            if (targetType == typeof(DateTime))
            {
                if (DateTime.TryParse(trimmed, culture, DateTimeStyles.None, out DateTime v)) { result = v; return true; }
                error = $"Cannot parse '{trimmed}' as DateTime.";
                return false;
            }

            if (targetType == typeof(Guid))
            {
                if (Guid.TryParse(trimmed, out Guid v)) { result = v; return true; }
                error = $"Cannot parse '{trimmed}' as Guid.";
                return false;
            }

            // 8. Unity structs
            if (targetType == typeof(Vector2))
                return TryConvertVector(trimmed, 2, culture, out result, out error);
            if (targetType == typeof(Vector3))
                return TryConvertVector(trimmed, 3, culture, out result, out error);
            if (targetType == typeof(Vector4))
                return TryConvertVector(trimmed, 4, culture, out result, out error);
            if (targetType == typeof(Vector2Int))
                return TryConvertVectorInt(trimmed, 2, culture, out result, out error);
            if (targetType == typeof(Vector3Int))
                return TryConvertVectorInt(trimmed, 3, culture, out result, out error);
            if (targetType == typeof(Color))
                return TryConvertColor(trimmed, culture, out result, out error);
            if (targetType == typeof(Color32))
            {
                if (TryConvertColor(trimmed, culture, out object colorObj, out error))
                {
                    result = (Color32)(Color)colorObj;
                    return true;
                }
                return false;
            }

            error = $"Unsupported target type '{targetType.Name}'. Implement ICellValueConverter to support custom types.";
            return false;
        }

        private static bool TryConvertBool(string value, out object result, out string error)
        {
            error = null;
            switch (value.ToLowerInvariant())
            {
                case "true":
                case "1":
                case "yes":
                case "y":
                    result = true;
                    return true;
                case "false":
                case "0":
                case "no":
                case "n":
                    result = false;
                    return true;
                default:
                    result = false;
                    error = $"Cannot parse '{value}' as bool.";
                    return false;
            }
        }

        private static bool TryConvertEnum(string value, Type enumType, CellConversionContext context, out object result, out string error)
        {
            error = null;
            string normalized = value.Replace(context.ListSeparator, ',');
            try
            {
                result = Enum.Parse(enumType, normalized, ignoreCase: true);
                return true;
            }
            catch
            {
                result = Activator.CreateInstance(enumType);
                error = $"Cannot parse '{value}' as enum {enumType.Name}.";
                return false;
            }
        }

        private static bool TryConvertVector(string value, int dims, CultureInfo culture, out object result, out string error)
        {
            error = null;
            result = null;
            string cleaned = value.Trim('(', ')', '[', ']', ' ');
            string[] parts = cleaned.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < dims)
            {
                error = $"Expected {dims} numeric components for Vector{dims}, got '{value}'.";
                return false;
            }

            float[] nums = new float[dims];
            for (int i = 0; i < dims; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, culture, out nums[i]))
                {
                    error = $"Invalid float component '{parts[i]}' in Vector{dims} value '{value}'.";
                    return false;
                }
            }

            result = dims switch
            {
                2 => new Vector2(nums[0], nums[1]),
                3 => new Vector3(nums[0], nums[1], nums[2]),
                _ => new Vector4(nums[0], nums[1], nums[2], nums[3])
            };
            return true;
        }

        private static bool TryConvertVectorInt(string value, int dims, CultureInfo culture, out object result, out string error)
        {
            error = null;
            result = null;
            string cleaned = value.Trim('(', ')', '[', ']', ' ');
            string[] parts = cleaned.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < dims)
            {
                error = $"Expected {dims} integer components for Vector{dims}Int, got '{value}'.";
                return false;
            }

            int[] nums = new int[dims];
            for (int i = 0; i < dims; i++)
            {
                if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, culture, out nums[i]))
                {
                    error = $"Invalid int component '{parts[i]}' in Vector{dims}Int value '{value}'.";
                    return false;
                }
            }

            result = dims == 2
                ? (object)new Vector2Int(nums[0], nums[1])
                : new Vector3Int(nums[0], nums[1], nums[2]);
            return true;
        }

        private static bool TryConvertColor(string value, CultureInfo culture, out object result, out string error)
        {
            error = null;
            if (ColorUtility.TryParseHtmlString(value.StartsWith("#") ? value : "#" + value, out Color hexColor) &&
                (value.StartsWith("#") || value.Length == 6 || value.Length == 8))
            {
                result = hexColor;
                return true;
            }

            string cleaned = value.Trim('(', ')', '[', ']', ' ');
            string[] parts = cleaned.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 || parts.Length == 4)
            {
                float[] c = new float[4] { 0f, 0f, 0f, 1f };
                bool allParsed = true;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, culture, out c[i]))
                    {
                        allParsed = false;
                        break;
                    }
                }

                if (allParsed)
                {
                    // Auto-normalize 0-255 values if any component > 1
                    if (c[0] > 1f || c[1] > 1f || c[2] > 1f || (parts.Length == 4 && c[3] > 1f))
                    {
                        c[0] /= 255f;
                        c[1] /= 255f;
                        c[2] /= 255f;
                        if (parts.Length == 4) c[3] /= 255f;
                    }
                    result = new Color(c[0], c[1], c[2], c[3]);
                    return true;
                }
            }

            result = Color.white;
            error = $"Cannot parse '{value}' as Color (expected #RRGGBB, #RRGGBBAA, or r,g,b[,a]).";
            return false;
        }

        private static bool TryConvertArray(
            string rawValue,
            Type elementType,
            CellConversionContext context,
            out object result,
            out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                result = Array.CreateInstance(elementType, 0);
                return true;
            }

            string[] tokens = rawValue.Split(new[] { context.ListSeparator }, StringSplitOptions.RemoveEmptyEntries);
            Array array = Array.CreateInstance(elementType, tokens.Length);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!TryConvert(tokens[i].Trim(), elementType, context, out object item, out string elemError))
                {
                    result = array;
                    error = $"Element [{i}]: {elemError}";
                    return false;
                }
                array.SetValue(item, i);
            }

            result = array;
            return true;
        }

        private static bool TryConvertList(
            string rawValue,
            Type listType,
            Type elementType,
            CellConversionContext context,
            out object result,
            out string error)
        {
            error = null;
            IList list = (IList)Activator.CreateInstance(listType);
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                result = list;
                return true;
            }

            string[] tokens = rawValue.Split(new[] { context.ListSeparator }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!TryConvert(tokens[i].Trim(), elementType, context, out object item, out string elemError))
                {
                    result = list;
                    error = $"Element [{i}]: {elemError}";
                    return false;
                }
                list.Add(item);
            }

            result = list;
            return true;
        }

        private static bool TryResolveUnityObject(
            string rawValue,
            Type targetType,
            CellConversionContext context,
            out object result,
            out string error)
        {
            error = null;
            string trimmed = (rawValue ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                result = null;
                return true;
            }

            // 1. Direct AssetDatabase path (e.g. Assets/Icons/Sword.png)
            if (trimmed.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(trimmed, targetType);
                if (asset != null)
                {
                    result = asset;
                    return true;
                }
            }

            // 2. Asset GUID (32 hex chars)
            if (trimmed.Length == 32 && IsHexString(trimmed))
            {
                string guidPath = AssetDatabase.GUIDToAssetPath(trimmed);
                if (!string.IsNullOrEmpty(guidPath))
                {
                    UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(guidPath, targetType);
                    if (asset != null)
                    {
                        result = asset;
                        return true;
                    }
                }
            }

            // 3. Resources.Load path
            UnityEngine.Object resource = Resources.Load(trimmed, targetType);
            if (resource != null)
            {
                result = resource;
                return true;
            }

            // 4. AssetDatabase.FindAssets by exact name
            string searchFilter = $"{trimmed} t:{targetType.Name}";
            string[] guids = (context.AssetSearchFolders != null && context.AssetSearchFolders.Length > 0)
                ? AssetDatabase.FindAssets(searchFilter, context.AssetSearchFolders)
                : AssetDatabase.FindAssets(searchFilter);

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UnityEngine.Object candidate = AssetDatabase.LoadAssetAtPath(path, targetType);
                if (candidate != null && string.Equals(candidate.name, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    result = candidate;
                    return true;
                }
            }

            // Fallback: first matching asset if any
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                UnityEngine.Object candidate = AssetDatabase.LoadAssetAtPath(path, targetType);
                if (candidate != null)
                {
                    result = candidate;
                    return true;
                }
            }

            result = null;
            error = $"Could not resolve {targetType.Name} asset '{trimmed}'.";
            return false;
        }

        private static bool IsHexString(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex)
                    return false;
            }
            return true;
        }
    }
}
