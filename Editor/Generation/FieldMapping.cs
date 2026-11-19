using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CsvXlsxToSo.Editor.Generation
{
    [Serializable]
    public sealed class ColumnFieldMapping
    {
        public string ColumnName;
        public string TargetFieldName;
        public bool Enabled = true;

        public ColumnFieldMapping() { }

        public ColumnFieldMapping(string columnName, string targetFieldName, bool enabled = true)
        {
            ColumnName = columnName;
            TargetFieldName = targetFieldName;
            Enabled = enabled;
        }
    }

    public sealed class SerializableFieldDescriptor
    {
        public FieldInfo Field { get; }
        public string FieldName => Field.Name;
        public Type FieldType => Field.FieldType;
        public string ExplicitColumnName { get; }
        public bool IsIdField { get; }

        public SerializableFieldDescriptor(FieldInfo field, string explicitColumnName, bool isIdField)
        {
            Field = field;
            ExplicitColumnName = explicitColumnName;
            IsIdField = isIdField;
        }
    }

    public static class FieldMappingUtility
    {
        public static List<Type> GetAllScriptableObjectTypes()
        {
            var result = new List<Type>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
            {
                if (type.IsAbstract || type.IsGenericType || type.IsInterface)
                    continue;
                if (typeof(EditorWindow).IsAssignableFrom(type) || typeof(UnityEditor.Editor).IsAssignableFrom(type))
                    continue;

                string ns = type.Namespace ?? string.Empty;
                if (ns.StartsWith("UnityEditor", StringComparison.Ordinal) ||
                    ns.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                    ns.StartsWith("Unity.", StringComparison.Ordinal) ||
                    ns.StartsWith("TMPro", StringComparison.Ordinal))
                    continue;

                result.Add(type);
            }

            result.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public static List<SerializableFieldDescriptor> GetSerializableFields(Type targetType)
        {
            var result = new List<SerializableFieldDescriptor>();
            if (targetType == null)
                return result;

            var hierarchy = new Stack<Type>();
            Type current = targetType;
            while (current != null && current != typeof(ScriptableObject) && current != typeof(UnityEngine.Object) && current != typeof(object))
            {
                hierarchy.Push(current);
                current = current.BaseType;
            }

            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            while (hierarchy.Count > 0)
            {
                Type layer = hierarchy.Pop();
                foreach (FieldInfo field in layer.GetFields(flags))
                {
                    if (field.IsStatic || field.IsInitOnly || field.IsLiteral)
                        continue;
                    if (field.GetCustomAttribute<NonSerializedAttribute>() != null)
                        continue;
                    if (field.GetCustomAttribute<SpreadsheetIgnoreAttribute>() != null)
                        continue;

                    bool isSerializable = field.IsPublic || field.GetCustomAttribute<SerializeField>() != null;
                    if (!isSerializable)
                        continue;

                    if (!seenNames.Add(field.Name))
                        continue;

                    var colAttr = field.GetCustomAttribute<SpreadsheetColumnAttribute>();
                    bool isId = field.GetCustomAttribute<SpreadsheetIdAttribute>() != null;

                    result.Add(new SerializableFieldDescriptor(field, colAttr?.ColumnName, isId));
                }
            }

            return result;
        }

        public static List<ColumnFieldMapping> AutoMapColumns(IReadOnlyList<string> headers, Type targetType)
        {
            var mappings = new List<ColumnFieldMapping>();
            if (headers == null || headers.Count == 0)
                return mappings;

            List<SerializableFieldDescriptor> fields = GetSerializableFields(targetType);

            foreach (string header in headers)
            {
                SerializableFieldDescriptor match = FindBestFieldMatch(header, fields);
                mappings.Add(new ColumnFieldMapping(
                    header,
                    match?.FieldName ?? string.Empty,
                    enabled: match != null));
            }

            return mappings;
        }

        public static string GuessIdColumn(IReadOnlyList<string> headers, Type targetType)
        {
            if (headers == null || headers.Count == 0)
                return string.Empty;

            if (targetType != null)
            {
                List<SerializableFieldDescriptor> fields = GetSerializableFields(targetType);
                foreach (SerializableFieldDescriptor field in fields)
                {
                    if (!field.IsIdField)
                        continue;

                    foreach (string header in headers)
                    {
                        if (!string.IsNullOrEmpty(field.ExplicitColumnName) &&
                            string.Equals(header, field.ExplicitColumnName, StringComparison.OrdinalIgnoreCase))
                            return header;

                        if (string.Equals(NormalizeName(header), NormalizeName(field.FieldName), StringComparison.Ordinal))
                            return header;
                    }
                }
            }

            string[] commonIdNames = { "id", "key", "assetname", "name", "code", "slug" };
            foreach (string candidate in commonIdNames)
            {
                foreach (string header in headers)
                {
                    if (string.Equals(NormalizeName(header), candidate, StringComparison.Ordinal))
                        return header;
                }
            }

            return headers[0];
        }

        private static SerializableFieldDescriptor FindBestFieldMatch(string header, List<SerializableFieldDescriptor> fields)
        {
            if (string.IsNullOrWhiteSpace(header) || fields == null || fields.Count == 0)
                return null;

            // 1. Explicit [SpreadsheetColumn("...")] match
            foreach (SerializableFieldDescriptor f in fields)
            {
                if (!string.IsNullOrEmpty(f.ExplicitColumnName) &&
                    string.Equals(f.ExplicitColumnName, header, StringComparison.OrdinalIgnoreCase))
                    return f;
            }

            // 2. Exact field name match (case-insensitive)
            foreach (SerializableFieldDescriptor f in fields)
            {
                if (string.Equals(f.FieldName, header, StringComparison.OrdinalIgnoreCase))
                    return f;
            }

            // 3. Normalized match (ignoring underscores, spaces, hyphens, and leading m_)
            string normalizedHeader = NormalizeName(header);
            foreach (SerializableFieldDescriptor f in fields)
            {
                if (string.Equals(NormalizeName(f.FieldName), normalizedHeader, StringComparison.Ordinal))
                    return f;
            }

            return null;
        }

        public static string NormalizeName(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            string trimmed = raw.Trim();
            if (trimmed.StartsWith("m_", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(2);
            else if (trimmed.StartsWith("_", StringComparison.Ordinal))
                trimmed = trimmed.Substring(1);

            var sb = new StringBuilder(trimmed.Length);
            foreach (char ch in trimmed)
            {
                if (char.IsLetterOrDigit(ch))
                    sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }
    }
}
