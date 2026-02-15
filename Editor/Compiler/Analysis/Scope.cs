using System;
using System.Collections.Generic;
using System.Linq;

namespace Nori.Compiler
{
    public enum SymbolKind
    {
        Variable,
        Function,
        Parameter,
        Builtin,
        StaticType,
        CustomEvent,
        EnumType,
    }

    public class Symbol
    {
        public string Name { get; }
        public string UdonType { get; }
        public SourceSpan DeclSpan { get; }
        public SymbolKind Kind { get; }
        public bool IsPublic { get; set; }
        public SyncMode SyncMode { get; set; }
        public bool IsArray { get; set; }

        public Symbol(string name, string udonType, SourceSpan declSpan, SymbolKind kind)
        {
            Name = name;
            UdonType = udonType;
            DeclSpan = declSpan;
            Kind = kind;
        }
    }

    public class Scope
    {
        private readonly Dictionary<string, Symbol> _symbols = new Dictionary<string, Symbol>();
        private readonly Scope _parent;

        public Scope(Scope parent = null)
        {
            _parent = parent;
        }

        public bool Define(Symbol symbol)
        {
            if (_symbols.ContainsKey(symbol.Name))
                return false;
            _symbols[symbol.Name] = symbol;
            return true;
        }

        public Symbol Lookup(string name)
        {
            if (_symbols.TryGetValue(name, out var symbol))
                return symbol;
            return _parent?.Lookup(name);
        }

        public IEnumerable<string> AllNames()
        {
            var names = new HashSet<string>(_symbols.Keys);
            if (_parent != null)
            {
                foreach (var name in _parent.AllNames())
                    names.Add(name);
            }
            return names;
        }

        public string FindClosest(string name)
        {
            string best = null;
            int bestDist = int.MaxValue;

            foreach (var candidate in AllNames())
            {
                int dist = LevenshteinDistance(name, candidate);
                if (dist < bestDist && dist <= 3) // threshold of 3
                {
                    bestDist = dist;
                    best = candidate;
                }
            }
            return best;
        }

        private static int LevenshteinDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
            if (string.IsNullOrEmpty(b)) return a.Length;

            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[a.Length, b.Length];
        }
    }
}
