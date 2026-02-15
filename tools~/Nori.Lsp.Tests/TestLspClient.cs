using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Nori.Compiler;
using Nori.Lsp.Server;
using LspDiag = Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic;

namespace Nori.Lsp.Tests
{
    /// <summary>
    /// In-process test harness that directly drives the DocumentManager and handlers
    /// without the JSON-RPC transport layer.
    /// </summary>
    public class TestLspClient
    {
        private readonly DocumentManager _documents;
        private readonly IExternCatalog _catalog;
        private readonly Dictionary<string, LspDiag[]> _publishedDiagnostics = new();

        public TestLspClient(IExternCatalog catalog = null)
        {
            _catalog = catalog ?? BuiltinCatalog.Instance;
            _documents = new DocumentManager(
                _catalog,
                (uri, diags) => _publishedDiagnostics[uri] = diags,
                msg => { }); // swallow logs in tests
        }

        public DocumentManager Documents => _documents;
        public IExternCatalog Catalog => _catalog;

        public void OpenDocument(string filename, string text)
        {
            var uri = $"file:///{filename}";
            _documents.OnDocumentOpened(uri, text);
            // Force immediate analysis (skip debounce)
            _documents.AnalyzeImmediate(uri);
        }

        public void ChangeDocument(string filename, string newText)
        {
            var uri = $"file:///{filename}";
            _documents.OnDocumentChanged(uri, newText);
            _documents.AnalyzeImmediate(uri);
        }

        public void CloseDocument(string filename)
        {
            var uri = $"file:///{filename}";
            _documents.OnDocumentClosed(uri);
        }

        public LspDiag[] GetDiagnostics(string filename)
        {
            var uri = $"file:///{filename}";
            return _publishedDiagnostics.TryGetValue(uri, out var diags) ? diags : Array.Empty<LspDiag>();
        }

        public DocumentState GetDocumentState(string filename)
        {
            var uri = $"file:///{filename}";
            return _documents.GetDocument(uri);
        }
    }
}
