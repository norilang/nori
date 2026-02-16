using System;
using System.Collections.Generic;
using System.Linq;

namespace Nori.Compiler
{
    /// <summary>
    /// Full extern catalog loaded from a JSON file produced by CatalogScraper.
    /// Implements IExternCatalog with enhanced overload resolution and enum support.
    /// </summary>
    public class FullCatalog : IExternCatalog
    {
        // Properties: (ownerType, propName) -> PropertyInfo
        private readonly Dictionary<(string, string), PropertyInfo> _properties
            = new Dictionary<(string, string), PropertyInfo>();

        // Instance methods: (ownerType, methodName) -> list of overloads
        private readonly Dictionary<(string, string), List<ExternSignature>> _methods
            = new Dictionary<(string, string), List<ExternSignature>>();

        // Static methods: (typeUdonName, methodName) -> list of overloads
        private readonly Dictionary<(string, string), List<ExternSignature>> _staticMethods
            = new Dictionary<(string, string), List<ExternSignature>>();

        // Operators: (op, leftType, rightType) -> OperatorInfo
        private readonly Dictionary<(TokenKind, string, string), OperatorInfo> _operators
            = new Dictionary<(TokenKind, string, string), OperatorInfo>();

        // Unary operators: (op, type) -> OperatorInfo
        private readonly Dictionary<(TokenKind, string), OperatorInfo> _unaryOperators
            = new Dictionary<(TokenKind, string), OperatorInfo>();

        // Enum types: udonType -> EnumTypeInfo
        private readonly Dictionary<string, EnumTypeInfo> _enums
            = new Dictionary<string, EnumTypeInfo>();

        // Type metadata: udonType -> CatalogTypeInfo
        private readonly Dictionary<string, CatalogTypeInfo> _typeInfos
            = new Dictionary<string, CatalogTypeInfo>();

        private readonly HashSet<string> _knownTypes = new HashSet<string>();

        // Types that have static members
        private readonly HashSet<string> _staticTypes = new HashSet<string>();

        // Short name -> UdonType mapping for static type registration
        private readonly Dictionary<string, string> _shortNameToUdon = new Dictionary<string, string>();

        // Fallback catalog for operators/methods not in the scraped data
        private readonly IExternCatalog _fallback = BuiltinCatalog.Instance;

        /// <summary>Total number of externs loaded from JSON.</summary>
        public int ExternCount { get; private set; }

        private FullCatalog() { }

        /// <summary>
        /// Loads a catalog from JSON text (Newtonsoft-free, manual JSON parsing).
        /// </summary>
        public static FullCatalog LoadFromJson(string json)
        {
            var catalog = new FullCatalog();
            catalog.ParseJson(json);
            return catalog;
        }

        // --- JSON Parsing (minimal, Newtonsoft-free) ---

        private void ParseJson(string json)
        {
            // Use a simple state-machine JSON parser for the known schema.
            // The JSON has three top-level arrays: externs, enums, types.

            var externs = ParseExternsArray(json);
            var enums = ParseEnumsArray(json);
            var types = ParseTypesArray(json);

            // Register types
            foreach (var t in types)
            {
                _typeInfos[t.UdonType] = new CatalogTypeInfo(t.UdonType, t.DotNetType, t.BaseType, t.IsEnum);
                _knownTypes.Add(t.UdonType);

                // Build short name mapping (e.g., "UnityEngineTransform" -> "Transform")
                string shortName = ExtractShortName(t.UdonType, t.DotNetType);
                if (shortName != null && !_shortNameToUdon.ContainsKey(shortName))
                    _shortNameToUdon[shortName] = t.UdonType;
            }

            // Register enums
            foreach (var e in enums)
            {
                _enums[e.UdonType] = new EnumTypeInfo(e.UdonType, e.UnderlyingType, e.Values);
                _knownTypes.Add(e.UdonType);

                // Register enum as a static type (for Enum.Value access)
                string shortName = ExtractShortName(e.UdonType, null);
                if (shortName != null)
                {
                    _staticTypes.Add(e.UdonType);
                    if (!_shortNameToUdon.ContainsKey(shortName))
                        _shortNameToUdon[shortName] = e.UdonType;
                }
            }

            // Register externs
            ExternCount = externs.Count;
            foreach (var e in externs)
            {
                _knownTypes.Add(e.OwnerType);

                var namedParams = new MethodParam[e.ParamTypes.Length];
                for (int i = 0; i < e.ParamTypes.Length; i++)
                {
                    namedParams[i] = new MethodParam(
                        i < e.ParamNames.Length ? e.ParamNames[i] : $"arg{i}",
                        e.ParamTypes[i]);
                }

                var sig = new ExternSignature(e.Extern, e.ParamTypes, e.ReturnType,
                    e.IsInstance, namedParams);

                switch (e.Kind)
                {
                    case "getter":
                        EnsureProperty(e.OwnerType, e.MethodName, e.ReturnType, sig, null, e.IsInstance);
                        break;
                    case "setter":
                        EnsureProperty(e.OwnerType, e.MethodName,
                            e.ParamTypes.Length > 0 ? e.ParamTypes[e.ParamTypes.Length - 1] : "SystemVoid",
                            null, sig, e.IsInstance);
                        break;
                    case "method":
                        AddMethod(e.OwnerType, e.MethodName, sig);
                        break;
                    case "static_method":
                        AddStaticMethod(e.OwnerType, e.MethodName, sig);
                        _staticTypes.Add(e.OwnerType);
                        break;
                    case "operator":
                        RegisterOperator(e);
                        break;
                    case "constructor":
                        AddStaticMethod(e.OwnerType, "ctor", sig);
                        break;
                }
            }
        }

        private void EnsureProperty(string owner, string name, string type,
            ExternSignature getter, ExternSignature setter, bool isInstance)
        {
            var key = (owner, name);
            if (_properties.TryGetValue(key, out var existing))
            {
                // Merge getter/setter
                var mergedGetter = getter ?? existing.Getter;
                var mergedSetter = setter ?? existing.Setter;
                var mergedType = type ?? existing.Type;
                _properties[key] = new PropertyInfo(mergedType, mergedGetter, mergedSetter);
            }
            else
            {
                _properties[key] = new PropertyInfo(type, getter, setter);
            }

            // Static properties make the owner a static type
            if (!isInstance)
                _staticTypes.Add(owner);
        }

        private void AddMethod(string owner, string name, ExternSignature sig)
        {
            var key = (owner, name);
            if (!_methods.ContainsKey(key))
                _methods[key] = new List<ExternSignature>();
            _methods[key].Add(sig);
        }

        private void AddStaticMethod(string owner, string name, ExternSignature sig)
        {
            var key = (owner, name);
            if (!_staticMethods.ContainsKey(key))
                _staticMethods[key] = new List<ExternSignature>();
            _staticMethods[key].Add(sig);
        }

        private void RegisterOperator(ExternEntryData e)
        {
            string opName = e.MethodName;
            if (opName.StartsWith("op_"))
                opName = opName.Substring(3);

            // Parse operand types from extern signature
            string leftType = e.ParamTypes.Length > 0 ? e.ParamTypes[0] : e.OwnerType;
            string rightType = e.ParamTypes.Length > 1 ? e.ParamTypes[1] : leftType;

            TokenKind? tok = OpNameToTokenKind(opName);
            if (tok.HasValue)
            {
                if (IsUnaryOp(opName))
                {
                    var key = (tok.Value, leftType);
                    if (!_unaryOperators.ContainsKey(key))
                        _unaryOperators[key] = new OperatorInfo(e.Extern, e.ReturnType, leftType, null);
                }
                else
                {
                    var key = (tok.Value, leftType, rightType);
                    if (!_operators.ContainsKey(key))
                        _operators[key] = new OperatorInfo(e.Extern, e.ReturnType, leftType, rightType);
                }
            }
        }

        // --- IExternCatalog Implementation ---

        public PropertyInfo ResolveProperty(string ownerUdonType, string propertyName)
        {
            if (_properties.TryGetValue((ownerUdonType, propertyName), out var prop))
                return prop;
            // SystemObject fallback
            if (ownerUdonType != "SystemObject" &&
                _properties.TryGetValue(("SystemObject", propertyName), out prop))
                return prop;
            return _fallback.ResolveProperty(ownerUdonType, propertyName);
        }

        public ExternSignature ResolveMethod(string ownerUdonType, string methodName, string[] argTypes)
        {
            if (_methods.TryGetValue((ownerUdonType, methodName), out var overloads))
            {
                var result = FindBestOverload(overloads, argTypes);
                if (result != null) return result;
            }
            // SystemObject fallback
            if (ownerUdonType != "SystemObject" &&
                _methods.TryGetValue(("SystemObject", methodName), out overloads))
            {
                var result = FindBestOverload(overloads, argTypes);
                if (result != null) return result;
            }
            return _fallback.ResolveMethod(ownerUdonType, methodName, argTypes);
        }

        public ExternSignature ResolveStaticMethod(string typeUdonName, string methodName, string[] argTypes)
        {
            if (_staticMethods.TryGetValue((typeUdonName, methodName), out var overloads))
            {
                var result = FindBestOverload(overloads, argTypes);
                if (result != null) return result;
            }
            return _fallback.ResolveStaticMethod(typeUdonName, methodName, argTypes);
        }

        public OperatorInfo ResolveOperator(TokenKind op, string leftType, string rightType)
        {
            if (_operators.TryGetValue((op, leftType, rightType), out var info))
                return info;

            // Try widening: int op float -> float op float
            if (leftType == "SystemInt32" && rightType == "SystemSingle")
            {
                if (_operators.TryGetValue((op, "SystemSingle", "SystemSingle"), out info))
                    return new OperatorInfo(info.Extern, info.ReturnType, "SystemSingle", "SystemSingle");
            }
            if (leftType == "SystemSingle" && rightType == "SystemInt32")
            {
                if (_operators.TryGetValue((op, "SystemSingle", "SystemSingle"), out info))
                    return new OperatorInfo(info.Extern, info.ReturnType, "SystemSingle", "SystemSingle");
            }

            // Object fallback for equality/inequality (null comparisons)
            if (op == TokenKind.EqualsEquals || op == TokenKind.BangEquals)
            {
                if (leftType == "SystemObject" || rightType == "SystemObject")
                {
                    if (_operators.TryGetValue((op, "SystemObject", "SystemObject"), out info))
                        return new OperatorInfo(info.Extern, info.ReturnType,
                            "SystemObject", "SystemObject");
                }
            }

            return _fallback.ResolveOperator(op, leftType, rightType);
        }

        public OperatorInfo ResolveUnaryOperator(TokenKind op, string operandType)
        {
            if (_unaryOperators.TryGetValue((op, operandType), out var info))
                return info;
            return _fallback.ResolveUnaryOperator(op, operandType);
        }

        public bool IsKnownType(string udonType) =>
            _knownTypes.Contains(udonType) || _fallback.IsKnownType(udonType);

        public IEnumerable<string> GetMethodNames(string ownerUdonType)
        {
            var names = new HashSet<string>(
                _methods.Keys
                    .Where(k => k.Item1 == ownerUdonType)
                    .Select(k => k.Item2));
            foreach (var n in _fallback.GetMethodNames(ownerUdonType))
                names.Add(n);
            return names;
        }

        public IEnumerable<string> GetPropertyNames(string ownerUdonType)
        {
            var names = new HashSet<string>(
                _properties.Keys
                    .Where(k => k.Item1 == ownerUdonType)
                    .Select(k => k.Item2));
            foreach (var n in _fallback.GetPropertyNames(ownerUdonType))
                names.Add(n);
            return names;
        }

        public List<ExternSignature> GetMethodOverloads(string ownerUdonType, string methodName)
        {
            if (_methods.TryGetValue((ownerUdonType, methodName), out var overloads))
                return overloads;
            if (ownerUdonType != "SystemObject" &&
                _methods.TryGetValue(("SystemObject", methodName), out overloads))
                return overloads;
            return _fallback.GetMethodOverloads(ownerUdonType, methodName);
        }

        public List<ExternSignature> GetStaticMethodOverloads(string typeUdonName, string methodName)
        {
            if (_staticMethods.TryGetValue((typeUdonName, methodName), out var overloads))
                return overloads;
            return _fallback.GetStaticMethodOverloads(typeUdonName, methodName);
        }

        public EnumTypeInfo ResolveEnum(string udonType)
        {
            _enums.TryGetValue(udonType, out var info);
            return info;
        }

        public bool IsEnumType(string udonType) => _enums.ContainsKey(udonType);

        public CatalogTypeInfo GetTypeInfo(string udonType)
        {
            _typeInfos.TryGetValue(udonType, out var info);
            return info;
        }

        public ImplicitConversion GetImplicitConversion(string fromType, string toType)
        {
            return ImplicitConversion.Lookup(fromType, toType);
        }

        public IEnumerable<string> GetStaticTypeNames()
        {
            return _staticTypes;
        }

        /// <summary>
        /// Returns short name -> UdonType mappings for static type registration.
        /// </summary>
        public Dictionary<string, string> GetShortNameMappings()
        {
            return new Dictionary<string, string>(_shortNameToUdon);
        }

        // --- Overload Resolution ---

        private ExternSignature FindBestOverload(List<ExternSignature> overloads, string[] argTypes)
        {
            ExternSignature exactMatch = null;
            ExternSignature wideningMatch = null;
            int bestWideningScore = -1;
            int ambiguousCount = 0;

            foreach (var sig in overloads)
            {
                if (sig.ParamTypes.Length != argTypes.Length)
                    continue;

                int score = ScoreOverload(sig.ParamTypes, argTypes);
                if (score < 0)
                    continue;

                if (score == argTypes.Length * 2) // perfect exact match
                {
                    exactMatch = sig;
                    break;
                }

                if (score > bestWideningScore)
                {
                    bestWideningScore = score;
                    wideningMatch = sig;
                    ambiguousCount = 1;
                }
                else if (score == bestWideningScore)
                {
                    ambiguousCount++;
                }
            }

            if (exactMatch != null) return exactMatch;
            if (wideningMatch != null && ambiguousCount == 1) return wideningMatch;

            // Ambiguous or no match
            return ambiguousCount > 1 ? null : wideningMatch;
        }

        /// <summary>
        /// Scores an overload match. Returns -1 if incompatible.
        /// Higher score = better match. Each param scores:
        ///   2 = exact match
        ///   1 = widening (implicit conversion)
        ///   0 = object match (any → SystemObject)
        /// </summary>
        private static int ScoreOverload(string[] paramTypes, string[] argTypes)
        {
            int score = 0;
            for (int i = 0; i < paramTypes.Length; i++)
            {
                if (paramTypes[i] == argTypes[i])
                {
                    score += 2;
                }
                else if (paramTypes[i] == "SystemObject")
                {
                    score += 0; // object match — worst
                }
                else if (TypeSystem.IsAssignable(paramTypes[i], argTypes[i]))
                {
                    score += 1; // widening match
                }
                else
                {
                    return -1; // incompatible
                }
            }
            return score;
        }

        // --- Helpers ---

        private static string ExtractShortName(string udonType, string dotNetType)
        {
            // For known prefixes, extract short name
            // "UnityEngineTransform" -> "Transform"
            // "VRCSDKBaseVRCPlayerApi" -> keep as-is (VRC types use full name)
            // "SystemInt32" -> "Int32" (not typically needed as static)

            if (dotNetType != null)
            {
                int lastDot = dotNetType.LastIndexOf('.');
                int lastPlus = dotNetType.LastIndexOf('+');
                int sep = Math.Max(lastDot, lastPlus);
                if (sep >= 0)
                    return dotNetType.Substring(sep + 1);
            }

            // Fallback: try stripping common prefixes
            string[] prefixes = { "UnityEngine", "VRCSDKBase", "VRCUdon", "System" };
            foreach (var prefix in prefixes)
            {
                if (udonType.StartsWith(prefix) && udonType.Length > prefix.Length)
                    return udonType.Substring(prefix.Length);
            }

            return null;
        }

        private static TokenKind? OpNameToTokenKind(string opName)
        {
            switch (opName)
            {
                case "Addition": return TokenKind.Plus;
                case "Subtraction": return TokenKind.Minus;
                case "Multiplication": return TokenKind.Star;
                case "Division": return TokenKind.Slash;
                case "Modulus": return TokenKind.Percent;
                case "Equality": return TokenKind.EqualsEquals;
                case "Inequality": return TokenKind.BangEquals;
                case "LessThan": return TokenKind.Less;
                case "LessThanOrEqual": return TokenKind.LessEquals;
                case "GreaterThan": return TokenKind.Greater;
                case "GreaterThanOrEqual": return TokenKind.GreaterEquals;
                case "UnaryNegation": return TokenKind.Minus;
                case "LogicalNot": return TokenKind.Bang;
                case "ConditionalAnd": return TokenKind.And;
                case "ConditionalOr": return TokenKind.Or;
                default: return null;
            }
        }

        private static bool IsUnaryOp(string opName)
        {
            return opName == "UnaryNegation" || opName == "LogicalNot" ||
                   opName == "op_UnaryNegation" || opName == "op_LogicalNot";
        }

        // --- Minimal JSON Parsing ---
        // These parse the specific schema without depending on Newtonsoft.

        private struct ExternEntryData
        {
            public string Extern;
            public string OwnerType;
            public string MethodName;
            public string Kind;
            public bool IsInstance;
            public string[] ParamTypes;
            public string[] ParamNames;
            public string ReturnType;
        }

        private struct EnumEntryData
        {
            public string UdonType;
            public string UnderlyingType;
            public Dictionary<string, int> Values;
        }

        private struct TypeEntryData
        {
            public string UdonType;
            public string DotNetType;
            public string BaseType;
            public bool IsEnum;
        }

        private List<ExternEntryData> ParseExternsArray(string json)
        {
            var result = new List<ExternEntryData>();
            int idx = json.IndexOf("\"externs\"", StringComparison.Ordinal);
            if (idx < 0) return result;

            idx = json.IndexOf('[', idx);
            if (idx < 0) return result;

            int depth = 0;
            int objStart = -1;
            for (int i = idx; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '{')
                {
                    if (depth == 0) objStart = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        string obj = json.Substring(objStart, i - objStart + 1);
                        result.Add(ParseExternObject(obj));
                        objStart = -1;
                    }
                }
                else if (c == ']' && depth == 0)
                {
                    break;
                }
            }
            return result;
        }

        private ExternEntryData ParseExternObject(string obj)
        {
            return new ExternEntryData
            {
                Extern = ExtractString(obj, "extern"),
                OwnerType = ExtractString(obj, "owner"),
                MethodName = ExtractString(obj, "method"),
                Kind = ExtractString(obj, "kind"),
                IsInstance = ExtractBool(obj, "instance"),
                ParamTypes = ExtractStringArray(obj, "paramTypes"),
                ParamNames = ExtractStringArray(obj, "paramNames"),
                ReturnType = ExtractString(obj, "returnType")
            };
        }

        private List<EnumEntryData> ParseEnumsArray(string json)
        {
            var result = new List<EnumEntryData>();
            int idx = json.IndexOf("\"enums\"", StringComparison.Ordinal);
            if (idx < 0) return result;

            idx = json.IndexOf('[', idx);
            if (idx < 0) return result;

            int depth = 0;
            int objStart = -1;
            for (int i = idx; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '{')
                {
                    if (depth == 0) objStart = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        string obj = json.Substring(objStart, i - objStart + 1);
                        result.Add(ParseEnumObject(obj));
                        objStart = -1;
                    }
                }
                else if (c == ']' && depth == 0)
                {
                    break;
                }
            }
            return result;
        }

        private EnumEntryData ParseEnumObject(string obj)
        {
            var entry = new EnumEntryData
            {
                UdonType = ExtractString(obj, "udonType"),
                UnderlyingType = ExtractString(obj, "underlyingType"),
                Values = new Dictionary<string, int>()
            };

            // Parse "values":{...}
            int valIdx = obj.IndexOf("\"values\"", StringComparison.Ordinal);
            if (valIdx >= 0)
            {
                int braceStart = obj.IndexOf('{', valIdx + 8);
                if (braceStart >= 0)
                {
                    int braceEnd = obj.IndexOf('}', braceStart);
                    if (braceEnd >= 0)
                    {
                        string valStr = obj.Substring(braceStart + 1, braceEnd - braceStart - 1);
                        // Parse "key":value pairs
                        var pairs = SplitJsonPairs(valStr);
                        foreach (var pair in pairs)
                        {
                            int colon = pair.IndexOf(':');
                            if (colon > 0)
                            {
                                string key = pair.Substring(0, colon).Trim().Trim('"');
                                string val = pair.Substring(colon + 1).Trim();
                                if (int.TryParse(val, out int intVal))
                                    entry.Values[key] = intVal;
                            }
                        }
                    }
                }
            }

            return entry;
        }

        private List<TypeEntryData> ParseTypesArray(string json)
        {
            var result = new List<TypeEntryData>();
            int idx = json.IndexOf("\"types\"", StringComparison.Ordinal);
            if (idx < 0) return result;

            idx = json.IndexOf('[', idx);
            if (idx < 0) return result;

            int depth = 0;
            int objStart = -1;
            for (int i = idx; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '{')
                {
                    if (depth == 0) objStart = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        string obj = json.Substring(objStart, i - objStart + 1);
                        result.Add(new TypeEntryData
                        {
                            UdonType = ExtractString(obj, "udonType"),
                            DotNetType = ExtractString(obj, "dotNetType"),
                            BaseType = ExtractString(obj, "baseType"),
                            IsEnum = ExtractBool(obj, "isEnum")
                        });
                        objStart = -1;
                    }
                }
                else if (c == ']' && depth == 0)
                {
                    break;
                }
            }
            return result;
        }

        // --- JSON extraction helpers ---

        private static string ExtractString(string json, string key)
        {
            string search = $"\"{key}\":\"";
            int idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return "";
            int start = idx + search.Length;
            int end = start;
            while (end < json.Length)
            {
                if (json[end] == '\\') { end += 2; continue; }
                if (json[end] == '"') break;
                end++;
            }
            return json.Substring(start, end - start)
                .Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static bool ExtractBool(string json, string key)
        {
            string search = $"\"{key}\":";
            int idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return false;
            int start = idx + search.Length;
            return json.Substring(start).TrimStart().StartsWith("true");
        }

        private static string[] ExtractStringArray(string json, string key)
        {
            string search = $"\"{key}\":[";
            int idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return Array.Empty<string>();
            int start = idx + search.Length;
            int end = json.IndexOf(']', start);
            if (end < 0) return Array.Empty<string>();

            string content = json.Substring(start, end - start).Trim();
            if (string.IsNullOrEmpty(content)) return Array.Empty<string>();

            var result = new List<string>();
            bool inString = false;
            int strStart = -1;
            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                if (c == '\\' && inString) { i++; continue; }
                if (c == '"')
                {
                    if (!inString)
                    {
                        inString = true;
                        strStart = i + 1;
                    }
                    else
                    {
                        result.Add(content.Substring(strStart, i - strStart)
                            .Replace("\\\"", "\"").Replace("\\\\", "\\"));
                        inString = false;
                    }
                }
            }
            return result.ToArray();
        }

        private static List<string> SplitJsonPairs(string content)
        {
            var result = new List<string>();
            bool inString = false;
            int start = 0;
            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                if (c == '\\' && inString) { i++; continue; }
                if (c == '"') inString = !inString;
                if (c == ',' && !inString)
                {
                    result.Add(content.Substring(start, i - start));
                    start = i + 1;
                }
            }
            if (start < content.Length)
                result.Add(content.Substring(start));
            return result;
        }
    }
}
