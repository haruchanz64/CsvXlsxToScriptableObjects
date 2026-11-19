using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CsvXlsxToSo.Editor.Parsers;
using UnityEditor;
using UnityEngine;

namespace CsvXlsxToSo.Editor.Generation
{
    [Serializable]
    public sealed class GeneratedFieldSpec
    {
        public string ColumnName;
        public string FieldName;
        public string TypeName;
        public bool Include = true;
        public bool IsIdField = false;
    }

    public sealed class CodeGenerationRequest
    {
        public string ClassName = "GeneratedData";
        public string Namespace = string.Empty;
        public string OutputFolder = "Assets/Scripts/Generated";
        public string CreateAssetMenuPath = "Data/GeneratedData";
        public bool GenerateContainerClass = false;
        public string ContainerClassName = string.Empty;
        public List<GeneratedFieldSpec> Fields = new List<GeneratedFieldSpec>();
    }

    public static class ScriptableObjectCodeGenerator
    {
        private static readonly HashSet<string> CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while"
        };

        public static List<GeneratedFieldSpec> InferFields(SpreadsheetSheet sheet, char listSeparator = '|')
        {
            var specs = new List<GeneratedFieldSpec>();
            if (sheet == null)
                return specs;

            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            string guessedId = FieldMappingUtility.GuessIdColumn(sheet.Headers, null);

            for (int c = 0; c < sheet.ColumnCount; c++)
            {
                string header = sheet.Headers[c];
                string fieldName = ToValidIdentifier(header, lowerCamelCase: true);

                string uniqueName = fieldName;
                int suffix = 2;
                while (!usedNames.Add(uniqueName))
                {
                    uniqueName = $"{fieldName}{suffix++}";
                }

                string hintedType = (c < sheet.TypeHints.Count && !string.IsNullOrWhiteSpace(sheet.TypeHints[c]))
                    ? NormalizeTypeHint(sheet.TypeHints[c])
                    : null;

                string inferredType = hintedType ?? InferColumnType(sheet, c, listSeparator);

                specs.Add(new GeneratedFieldSpec
                {
                    ColumnName = header,
                    FieldName = uniqueName,
                    TypeName = inferredType,
                    Include = true,
                    IsIdField = string.Equals(header, guessedId, StringComparison.OrdinalIgnoreCase)
                });
            }

            return specs;
        }

        public static string GenerateSourceCode(CodeGenerationRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            string className = ToValidIdentifier(request.ClassName, lowerCamelCase: false);
            bool hasNamespace = !string.IsNullOrWhiteSpace(request.Namespace);
            string indent = hasNamespace ? "    " : string.Empty;

            bool needsGenericCollections = request.GenerateContainerClass;
            bool needsAttributes = false;

            foreach (GeneratedFieldSpec field in request.Fields)
            {
                if (!field.Include)
                    continue;
                if (field.TypeName != null && field.TypeName.Contains("List<"))
                    needsGenericCollections = true;
                if (field.IsIdField || !string.Equals(field.FieldName, field.ColumnName, StringComparison.Ordinal))
                    needsAttributes = true;
            }

            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            if (needsGenericCollections)
                sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UnityEngine;");
            if (needsAttributes)
                sb.AppendLine("using CsvXlsxToSo;");
            sb.AppendLine();

            if (hasNamespace)
            {
                sb.AppendLine($"namespace {request.Namespace.Trim()}");
                sb.AppendLine("{");
            }

            string menuName = string.IsNullOrWhiteSpace(request.CreateAssetMenuPath)
                ? $"Data/{className}"
                : request.CreateAssetMenuPath.Trim();

            sb.AppendLine($"{indent}[CreateAssetMenu(fileName = \"{className}\", menuName = \"{menuName}\")]");
            sb.AppendLine($"{indent}public class {className} : ScriptableObject");
            sb.AppendLine($"{indent}{{");

            foreach (GeneratedFieldSpec field in request.Fields)
            {
                if (!field.Include)
                    continue;

                string fieldName = ToValidIdentifier(field.FieldName, lowerCamelCase: true);
                string typeName = string.IsNullOrWhiteSpace(field.TypeName) ? "string" : field.TypeName.Trim();

                if (field.IsIdField)
                    sb.AppendLine($"{indent}    [SpreadsheetId]");

                if (!string.Equals(fieldName, field.ColumnName, StringComparison.Ordinal))
                {
                    string escapedCol = field.ColumnName.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    sb.AppendLine($"{indent}    [SpreadsheetColumn(\"{escapedCol}\")]");
                }

                sb.AppendLine($"{indent}    public {typeName} {fieldName};");
            }

            sb.AppendLine($"{indent}}}");

            if (request.GenerateContainerClass)
            {
                string containerName = string.IsNullOrWhiteSpace(request.ContainerClassName)
                    ? $"{className}Database"
                    : ToValidIdentifier(request.ContainerClassName, lowerCamelCase: false);

                sb.AppendLine();
                sb.AppendLine($"{indent}[CreateAssetMenu(fileName = \"{containerName}\", menuName = \"{menuName} Database\")]");
                sb.AppendLine($"{indent}public class {containerName} : ScriptableObject");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    public List<{className}> items = new List<{className}>();");
                sb.AppendLine($"{indent}}}");
            }

            if (hasNamespace)
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        public static string WriteScriptFile(CodeGenerationRequest request)
        {
            string className = ToValidIdentifier(request.ClassName, lowerCamelCase: false);
            string outputFolder = ScriptableObjectGenerator.NormalizeUnityFolder(request.OutputFolder);
            ScriptableObjectGenerator.EnsureFolderExists(outputFolder);

            string assetPath = $"{outputFolder}/{className}.cs";
            string fullPath = Path.GetFullPath(assetPath);
            string source = GenerateSourceCode(request);

            File.WriteAllText(fullPath, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            AssetDatabase.ImportAsset(assetPath);
            AssetDatabase.Refresh();
            return assetPath;
        }

        public static string InferColumnType(SpreadsheetSheet sheet, int columnIndex, char listSeparator = '|')
        {
            var nonEmpty = new List<string>();
            for (int r = 0; r < sheet.RowCount; r++)
            {
                string val = sheet.GetCell(r, columnIndex).Trim();
                if (!string.IsNullOrEmpty(val))
                    nonEmpty.Add(val);
            }

            if (nonEmpty.Count == 0)
                return "string";

            // Check if column looks like a separated list
            bool anyContainsSeparator = false;
            foreach (string v in nonEmpty)
            {
                if (v.IndexOf(listSeparator) >= 0)
                {
                    anyContainsSeparator = true;
                    break;
                }
            }

            if (anyContainsSeparator)
            {
                var elements = new List<string>();
                foreach (string v in nonEmpty)
                {
                    foreach (string part in v.Split(new[] { listSeparator }, StringSplitOptions.RemoveEmptyEntries))
                        elements.Add(part.Trim());
                }
                string elemType = InferScalarType(elements);
                return $"List<{elemType}>";
            }

            return InferScalarType(nonEmpty);
        }

        private static string InferScalarType(List<string> values)
        {
            if (values.Count == 0)
                return "string";

            if (AllMatch(values, IsBool)) return "bool";
            if (AllMatch(values, IsInt)) return "int";
            if (AllMatch(values, IsFloat)) return "float";
            if (AllMatch(values, IsHexColor)) return "Color";
            if (AllMatch(values, v => IsVector(v, 2))) return "Vector2";
            if (AllMatch(values, v => IsVector(v, 3))) return "Vector3";

            return "string";
        }

        private static bool AllMatch(List<string> values, Func<string, bool> predicate)
        {
            foreach (string v in values)
            {
                if (!predicate(v))
                    return false;
            }
            return true;
        }

        private static bool IsBool(string v)
        {
            string l = v.ToLowerInvariant();
            return l == "true" || l == "false" || l == "yes" || l == "no";
        }

        private static bool IsInt(string v) => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

        private static bool IsFloat(string v) => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

        private static bool IsHexColor(string v)
        {
            return v.StartsWith("#") && (v.Length == 7 || v.Length == 9) && ColorUtility.TryParseHtmlString(v, out _);
        }

        private static bool IsVector(string v, int dims)
        {
            string cleaned = v.Trim('(', ')', '[', ']', ' ');
            string[] parts = cleaned.Split(',');
            if (parts.Length != dims)
                return false;
            for (int i = 0; i < dims; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return false;
            }
            return true;
        }

        private static string NormalizeTypeHint(string hint)
        {
            string trimmed = hint.Trim();
            switch (trimmed.ToLowerInvariant())
            {
                case "str":
                case "text":
                case "string": return "string";
                case "int":
                case "int32":
                case "integer": return "int";
                case "long":
                case "int64": return "long";
                case "float":
                case "single": return "float";
                case "double": return "double";
                case "bool":
                case "boolean": return "bool";
                case "vec2":
                case "vector2": return "Vector2";
                case "vec3":
                case "vector3": return "Vector3";
                case "vec4":
                case "vector4": return "Vector4";
                case "color": return "Color";
                case "sprite": return "Sprite";
                case "texture2d": return "Texture2D";
                case "gameobject":
                case "prefab": return "GameObject";
                case "audioclip": return "AudioClip";
                default: return trimmed;
            }
        }

        public static string ToValidIdentifier(string raw, bool lowerCamelCase)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return lowerCamelCase ? "field" : "GeneratedData";

            var sb = new StringBuilder();
            bool capitalizeNext = !lowerCamelCase;

            foreach (char ch in raw.Trim())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    if (sb.Length == 0 && char.IsDigit(ch))
                        sb.Append('_');

                    if (sb.Length == 0 && lowerCamelCase)
                        sb.Append(char.ToLowerInvariant(ch));
                    else if (capitalizeNext)
                        sb.Append(char.ToUpperInvariant(ch));
                    else
                        sb.Append(ch);

                    capitalizeNext = false;
                }
                else
                {
                    capitalizeNext = true;
                }
            }

            string identifier = sb.ToString();
            if (string.IsNullOrEmpty(identifier))
                identifier = lowerCamelCase ? "field" : "GeneratedData";

            if (CSharpKeywords.Contains(identifier))
                identifier = "@" + identifier;

            return identifier;
        }
    }
}
