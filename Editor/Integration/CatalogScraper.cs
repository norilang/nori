#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Nori
{
    public static class CatalogScraper
    {
        private const string OutputPath = "ProjectSettings/NoriCatalog.json";

        [MenuItem("Tools/Nori/Generate Extern Catalog")]
        public static void Generate()
        {
            try
            {
                var json = ScrapeToJson();
                if (json != null)
                {
                    File.WriteAllText(OutputPath, json);
                    Debug.Log($"[Nori] Catalog written to {OutputPath}");
                    NoriSettings.instance.InvalidateCatalogCache();
                    NoriSettings.instance.ExternCatalogPath = OutputPath;
                    RecordSdkMetadata();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Nori] Catalog generation failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void RecordSdkMetadata()
        {
            try
            {
                var managerType = FindType("VRC.Udon.Editor.UdonEditorManager");
                if (managerType == null) return;
                string dllPath = managerType.Assembly.Location;
                if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath)) return;
                NoriSettings.instance.CatalogGeneratedFromDll = dllPath;
                NoriSettings.instance.CatalogGeneratedTimestamp =
                    File.GetLastWriteTimeUtc(dllPath).Ticks;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Nori] Could not record SDK metadata: {ex.Message}");
            }
        }

        public static string ScrapeToJson()
        {
            // Get UdonEditorManager via reflection (no compile-time VRC SDK reference)
            var managerType = FindType("VRC.Udon.Editor.UdonEditorManager");
            if (managerType == null)
            {
                Debug.LogError("[Nori] VRC.Udon.Editor.UdonEditorManager not found. Is the VRChat SDK installed?");
                return null;
            }

            var instanceProp = managerType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static);
            if (instanceProp == null)
            {
                Debug.LogError("[Nori] UdonEditorManager.Instance property not found.");
                return null;
            }

            var manager = instanceProp.GetValue(null);
            if (manager == null)
            {
                Debug.LogError("[Nori] UdonEditorManager.Instance is null.");
                return null;
            }

            // Get node registries — use GetMethods to avoid "Ambiguous match" when
            // multiple overloads exist (e.g., generic and non-generic variants).
            var getRegistriesMethods = managerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            MethodInfo getRegistriesMethod = null;
            foreach (var m in getRegistriesMethods)
            {
                if (m.Name == "GetNodeRegistries" && m.GetParameters().Length == 0)
                {
                    getRegistriesMethod = m;
                    break;
                }
            }
            if (getRegistriesMethod == null)
            {
                Debug.LogError("[Nori] GetNodeRegistries method not found.");
                return null;
            }

            var registries = getRegistriesMethod.Invoke(manager, null);
            if (registries == null)
            {
                Debug.LogError("[Nori] GetNodeRegistries returned null.");
                return null;
            }

            // Iterate registries and collect node definitions
            var externs = new List<ExternEntry>();
            var enumTypes = new Dictionary<string, EnumEntry>();
            var typeInfos = new Dictionary<string, TypeEntry>();

            var registriesDict = registries as System.Collections.IDictionary;
            if (registriesDict == null)
            {
                Debug.LogError("[Nori] GetNodeRegistries returned unexpected type.");
                return null;
            }

            int registryCount = 0;
            int totalDefs = 0;

            foreach (System.Collections.DictionaryEntry entry in registriesDict)
            {
                registryCount++;
                var registry = entry.Value;
                var regType = registry.GetType();

                // Use GetMethods to avoid ambiguous match
                var getNodesMethods = regType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                MethodInfo getNodesMethod = null;
                foreach (var m in getNodesMethods)
                {
                    if (m.Name == "GetNodeDefinitions" && m.GetParameters().Length == 0)
                    {
                        getNodesMethod = m;
                        break;
                    }
                }
                if (getNodesMethod == null)
                {
                    Debug.LogWarning($"[Nori] Registry '{entry.Key}' ({regType.Name}): GetNodeDefinitions() not found");
                    continue;
                }

                var defsResult = getNodesMethod.Invoke(registry, null);
                if (defsResult == null)
                {
                    Debug.LogWarning($"[Nori] Registry '{entry.Key}': GetNodeDefinitions() returned null");
                    continue;
                }

                // GetNodeDefinitions() may return a dictionary (IReadOnlyDictionary<string, T>).
                // If so, iterate its Values; otherwise iterate directly.
                System.Collections.IEnumerable definitions;
                var valuesProp = defsResult.GetType().GetProperty("Values");
                if (valuesProp != null)
                {
                    definitions = valuesProp.GetValue(defsResult) as System.Collections.IEnumerable;
                }
                else
                {
                    definitions = defsResult as System.Collections.IEnumerable;
                }

                if (definitions == null)
                {
                    Debug.LogWarning($"[Nori] Registry '{entry.Key}': Could not enumerate definitions (type: {defsResult.GetType().FullName})");
                    continue;
                }

                int regDefs = 0;
                foreach (var def in definitions)
                {
                    regDefs++;
                    ProcessNodeDefinition(def, externs, enumTypes, typeInfos);
                }
                totalDefs += regDefs;
            }

            Debug.Log($"[Nori] Scraped {registryCount} registries, {totalDefs} node definitions");

            // Build JSON
            var json = BuildJson(externs, enumTypes, typeInfos);

            // Print summary
            int methodCount = externs.Count(e => e.Kind == "method" || e.Kind == "static_method");
            int propCount = externs.Count(e => e.Kind == "getter" || e.Kind == "setter");
            int opCount = externs.Count(e => e.Kind == "operator");
            int ctorCount = externs.Count(e => e.Kind == "constructor");
            Debug.Log($"[Nori] Catalog summary: {externs.Count} total externs " +
                      $"({methodCount} methods, {propCount} properties, {opCount} operators, " +
                      $"{ctorCount} constructors), {enumTypes.Count} enums, {typeInfos.Count} types");

            return json;
        }

        private static void ProcessNodeDefinition(object def, List<ExternEntry> externs,
            Dictionary<string, EnumEntry> enumTypes, Dictionary<string, TypeEntry> typeInfos)
        {
            var defType = def.GetType();

            // If def is a KeyValuePair (from dictionary iteration), extract the Value
            var valueProp = defType.GetProperty("Value");
            if (valueProp != null && defType.IsGenericType &&
                defType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                def = valueProp.GetValue(def);
                if (def == null) return;
                defType = def.GetType();
            }

            // Get fullName (may be a property or field depending on VRC SDK version)
            string fullName = GetMemberValue<string>(def, defType, "fullName", "FullName");
            if (string.IsNullOrEmpty(fullName)) return;

            // Skip non-extern nodes (e.g., flow control, comments)
            if (!fullName.Contains("__")) return;

            // Get type
            Type nodeType = GetMemberValue<Type>(def, defType, "type", "Type");

            // Get inputs/outputs
            object inputsObj = GetMemberValue<object>(def, defType, "Inputs", "inputs");
            object outputsObj = GetMemberValue<object>(def, defType, "Outputs", "outputs");

            var inputs = ExtractParameters(inputsObj);
            var outputs = ExtractParameters(outputsObj);

            // Parse the extern signature
            var entry = ParseExternSignature(fullName, inputs, outputs, nodeType);
            if (entry != null)
                externs.Add(entry);

            // Check for enum type
            if (nodeType != null && nodeType.IsEnum && !enumTypes.ContainsKey(ToUdonTypeName(nodeType)))
            {
                var enumEntry = new EnumEntry
                {
                    UdonType = ToUdonTypeName(nodeType),
                    UnderlyingType = ToUdonTypeName(Enum.GetUnderlyingType(nodeType)),
                    Values = new Dictionary<string, int>()
                };
                foreach (var name in Enum.GetNames(nodeType))
                {
                    enumEntry.Values[name] = (int)Convert.ChangeType(Enum.Parse(nodeType, name), typeof(int));
                }
                enumTypes[enumEntry.UdonType] = enumEntry;
            }

            // Register type info
            if (nodeType != null)
            {
                string udonName = ToUdonTypeName(nodeType);
                if (!typeInfos.ContainsKey(udonName))
                {
                    typeInfos[udonName] = new TypeEntry
                    {
                        UdonType = udonName,
                        DotNetType = nodeType.FullName,
                        BaseType = nodeType.BaseType != null ? ToUdonTypeName(nodeType.BaseType) : null,
                        IsEnum = nodeType.IsEnum
                    };
                }
            }
        }

        private static ExternEntry ParseExternSignature(string fullName,
            List<ParamEntry> inputs, List<ParamEntry> outputs, Type nodeType)
        {
            // Parse owner type from signature prefix (before .__):
            // e.g., "UnityEngineTransform.__get_position__UnityEngineVector3"
            int methodSep = fullName.IndexOf(".__", StringComparison.Ordinal);
            if (methodSep < 0) return null;

            string ownerType = fullName.Substring(0, methodSep);
            string remainder = fullName.Substring(methodSep + 3); // skip ".__"

            // Determine kind from method name prefix
            string kind;
            string methodName;
            if (remainder.StartsWith("get_"))
            {
                kind = "getter";
                methodName = remainder.Substring(4);
                // Strip everything after __ (the return type suffix)
                int sep = methodName.IndexOf("__", StringComparison.Ordinal);
                if (sep >= 0) methodName = methodName.Substring(0, sep);
            }
            else if (remainder.StartsWith("set_"))
            {
                kind = "setter";
                methodName = remainder.Substring(4);
                int sep = methodName.IndexOf("__", StringComparison.Ordinal);
                if (sep >= 0) methodName = methodName.Substring(0, sep);
            }
            else if (remainder.StartsWith("op_"))
            {
                kind = "operator";
                methodName = remainder;
                int sep = methodName.IndexOf("__", StringComparison.Ordinal);
                if (sep >= 0) methodName = methodName.Substring(0, sep);
            }
            else if (remainder.StartsWith("ctor__"))
            {
                kind = "constructor";
                methodName = "ctor";
            }
            else
            {
                // Regular method
                kind = "method";
                methodName = remainder;
                int sep = methodName.IndexOf("__", StringComparison.Ordinal);
                if (sep >= 0) methodName = methodName.Substring(0, sep);
            }

            // Determine if instance or static from inputs
            bool isInstance = false;
            var paramInputs = new List<ParamEntry>();
            foreach (var input in inputs)
            {
                if (input.Name == "instance" || input.Name == "inst")
                {
                    isInstance = true;
                }
                else
                {
                    paramInputs.Add(input);
                }
            }

            // For static type methods, mark appropriately
            if (!isInstance && kind == "method")
                kind = "static_method";

            // Determine return type from outputs
            string returnType = "SystemVoid";
            if (outputs.Count > 0)
            {
                returnType = outputs[outputs.Count - 1].UdonType;
            }

            return new ExternEntry
            {
                Extern = fullName,
                OwnerType = ownerType,
                MethodName = methodName,
                Kind = kind,
                IsInstance = isInstance,
                ParamTypes = paramInputs.Select(p => p.UdonType).ToArray(),
                ParamNames = paramInputs.Select(p => p.Name).ToArray(),
                ReturnType = returnType
            };
        }

        private static List<ParamEntry> ExtractParameters(object paramCollection)
        {
            var result = new List<ParamEntry>();
            if (paramCollection == null) return result;

            var enumerable = paramCollection as System.Collections.IEnumerable;
            if (enumerable == null) return result;

            foreach (var param in enumerable)
            {
                var paramType = param.GetType();
                string name = GetMemberValue<string>(param, paramType, "name", "Name") ?? "";
                Type type = GetMemberValue<Type>(param, paramType, "type", "Type");

                result.Add(new ParamEntry
                {
                    Name = name,
                    UdonType = type != null ? ToUdonTypeName(type) : "SystemObject"
                });
            }

            return result;
        }

        /// <summary>
        /// Gets a member value by name, trying property first then field.
        /// Checks two name variants (e.g., "fullName" and "FullName").
        /// </summary>
        private static T GetMemberValue<T>(object obj, Type objType, string name1, string name2) where T : class
        {
            // Try properties first
            var prop = objType.GetProperty(name1) ?? objType.GetProperty(name2);
            if (prop != null)
                return prop.GetValue(obj) as T;

            // Fall back to fields
            var field = objType.GetField(name1, BindingFlags.Public | BindingFlags.Instance)
                     ?? objType.GetField(name2, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
                return field.GetValue(obj) as T;

            return null;
        }

        public static string ToUdonTypeName(Type type)
        {
            if (type == null) return "SystemObject";

            string fullName = type.FullName ?? type.Name;

            // Handle arrays
            if (type.IsArray)
            {
                var elemType = type.GetElementType();
                return ToUdonTypeName(elemType) + "Array";
            }

            // Handle by-ref
            if (type.IsByRef)
            {
                var elemType = type.GetElementType();
                return ToUdonTypeName(elemType) + "Ref";
            }

            // Remove dots, handle nested types
            return fullName
                .Replace(".", "")
                .Replace("+", "")
                .Replace("[]", "Array")
                .Replace("&", "Ref");
        }

        private static string BuildJson(List<ExternEntry> externs,
            Dictionary<string, EnumEntry> enumTypes,
            Dictionary<string, TypeEntry> typeInfos)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");

            // Externs array
            sb.AppendLine("  \"externs\": [");
            for (int i = 0; i < externs.Count; i++)
            {
                var e = externs[i];
                sb.Append("    {");
                sb.Append($"\"extern\":\"{Escape(e.Extern)}\",");
                sb.Append($"\"owner\":\"{Escape(e.OwnerType)}\",");
                sb.Append($"\"method\":\"{Escape(e.MethodName)}\",");
                sb.Append($"\"kind\":\"{e.Kind}\",");
                sb.Append($"\"instance\":{(e.IsInstance ? "true" : "false")},");
                sb.Append("\"paramTypes\":[");
                sb.Append(string.Join(",", e.ParamTypes.Select(p => $"\"{Escape(p)}\"")));
                sb.Append("],\"paramNames\":[");
                sb.Append(string.Join(",", e.ParamNames.Select(p => $"\"{Escape(p)}\"")));
                sb.Append($"],\"returnType\":\"{Escape(e.ReturnType)}\"");
                sb.Append("}");
                if (i < externs.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("  ],");

            // Enums
            sb.AppendLine("  \"enums\": [");
            var enumList = enumTypes.Values.ToList();
            for (int i = 0; i < enumList.Count; i++)
            {
                var e = enumList[i];
                sb.Append($"    {{\"udonType\":\"{Escape(e.UdonType)}\",");
                sb.Append($"\"underlyingType\":\"{Escape(e.UnderlyingType)}\",");
                sb.Append("\"values\":{");
                var vals = e.Values.ToList();
                for (int j = 0; j < vals.Count; j++)
                {
                    sb.Append($"\"{Escape(vals[j].Key)}\":{vals[j].Value}");
                    if (j < vals.Count - 1) sb.Append(",");
                }
                sb.Append("}}");
                if (i < enumList.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("  ],");

            // Types
            sb.AppendLine("  \"types\": [");
            var typeList = typeInfos.Values.ToList();
            for (int i = 0; i < typeList.Count; i++)
            {
                var t = typeList[i];
                sb.Append($"    {{\"udonType\":\"{Escape(t.UdonType)}\",");
                sb.Append($"\"dotNetType\":\"{Escape(t.DotNetType ?? "")}\",");
                sb.Append($"\"baseType\":\"{Escape(t.BaseType ?? "")}\",");
                sb.Append($"\"isEnum\":{(t.IsEnum ? "true" : "false")}");
                sb.Append("}");
                if (i < typeList.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("  ]");

            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = asm.GetType(fullName);
                    if (type != null) return type;
                }
                catch
                {
                    // Skip assemblies that throw on GetType
                }
            }
            return null;
        }

        // --- Internal data structures ---

        private class ExternEntry
        {
            public string Extern;
            public string OwnerType;
            public string MethodName;
            public string Kind;      // method, static_method, getter, setter, operator, constructor
            public bool IsInstance;
            public string[] ParamTypes;
            public string[] ParamNames;
            public string ReturnType;
        }

        private class EnumEntry
        {
            public string UdonType;
            public string UnderlyingType;
            public Dictionary<string, int> Values;
        }

        private class TypeEntry
        {
            public string UdonType;
            public string DotNetType;
            public string BaseType;
            public bool IsEnum;
        }

        private class ParamEntry
        {
            public string Name;
            public string UdonType;
        }
    }
}
#endif
