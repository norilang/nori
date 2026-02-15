using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Nori.Compiler;
using Nori.Lsp.Utilities;

namespace Nori.Lsp.Server
{
    /// <summary>
    /// Manages open document state and triggers debounced analysis.
    /// </summary>
    public class DocumentManager
    {
        private readonly ConcurrentDictionary<string, DocumentState> _documents = new();
        private readonly IExternCatalog _catalog;
        private readonly Action<string, Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic[]> _publishDiagnostics;
        private readonly Action<string> _log;

        private const int DebounceDelayMs = 200;

        public DocumentManager(
            IExternCatalog catalog,
            Action<string, Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic[]> publishDiagnostics,
            Action<string> log)
        {
            _catalog = catalog;
            _publishDiagnostics = publishDiagnostics;
            _log = log;
        }

        public void OnDocumentOpened(string uri, string text)
        {
            var state = new DocumentState(uri, text);
            _documents[uri] = state;
            AnalyzeDebounced(uri);
        }

        public void OnDocumentChanged(string uri, string newText)
        {
            if (_documents.TryGetValue(uri, out var state))
            {
                state.Text = newText;
                AnalyzeDebounced(uri);
            }
        }

        public void OnDocumentClosed(string uri)
        {
            _documents.TryRemove(uri, out _);
            // Clear diagnostics when file is closed
            _publishDiagnostics(uri, Array.Empty<Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic>());
        }

        public DocumentState GetDocument(string uri)
        {
            _documents.TryGetValue(uri, out var state);
            return state;
        }

        private async void AnalyzeDebounced(string uri)
        {
            if (!_documents.TryGetValue(uri, out var state)) return;

            var version = state.IncrementPendingVersion();
            await Task.Delay(DebounceDelayMs);

            if (state.PendingVersion != version) return; // Superseded by newer edit

            try
            {
                Analyze(state);
            }
            catch (Exception ex)
            {
                _log($"Analysis error for {uri}: {ex.Message}");
            }
        }

        private void Analyze(DocumentState state)
        {
            var filePath = UriToFilePath(state.Uri);
            var result = NoriCompiler.AnalyzeForLsp(state.Text, filePath, _catalog);

            state.Tokens = result.Tokens;
            state.Ast = result.Ast;
            state.Diagnostics = result.Diagnostics;
            state.TypeMap = result.TypeMap;
            state.ScopeMap = result.ScopeMap;

            var lspDiagnostics = LspDiagnosticConverter.Convert(result.Diagnostics.All);
            _publishDiagnostics(state.Uri, lspDiagnostics);
        }

        /// <summary>Force immediate (non-debounced) analysis. Used by tests.</summary>
        public void AnalyzeImmediate(string uri)
        {
            if (_documents.TryGetValue(uri, out var state))
                Analyze(state);
        }

        private static string UriToFilePath(string uri)
        {
            if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile)
                return parsed.LocalPath;
            return uri;
        }
    }
}
