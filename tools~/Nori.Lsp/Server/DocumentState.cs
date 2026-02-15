using System.Collections.Generic;
using Nori.Compiler;

namespace Nori.Lsp.Server
{
    /// <summary>
    /// Holds the current state of an open .nori document — text, analysis results.
    /// </summary>
    public class DocumentState
    {
        public string Uri { get; }
        public string Text { get; set; }
        public int Version { get; set; }

        // Analysis results (updated after each successful analysis)
        public List<Token> Tokens { get; set; }
        public ModuleDecl Ast { get; set; }
        public DiagnosticBag Diagnostics { get; set; }
        public Dictionary<AstNode, string> TypeMap { get; set; }
        public Dictionary<AstNode, Scope> ScopeMap { get; set; }

        private int _pendingVersion;
        private readonly object _lock = new object();

        public DocumentState(string uri, string text)
        {
            Uri = uri;
            Text = text;
        }

        public int IncrementPendingVersion()
        {
            lock (_lock)
            {
                return ++_pendingVersion;
            }
        }

        public int PendingVersion
        {
            get { lock (_lock) { return _pendingVersion; } }
        }
    }
}
