using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Newtonsoft.Json.Linq;
using Nori.Compiler;
using Nori.Lsp.Handlers;
using StreamJsonRpc;

namespace Nori.Lsp.Server
{
    public class NoriLanguageServer
    {
        private readonly DocumentManager _documents;
        private readonly IExternCatalog _catalog;
        private readonly TaskCompletionSource<bool> _shutdownTcs = new();
        private JsonRpc _rpc;

        // Handlers
        private readonly DiagnosticsPublisher _diagnosticsPublisher;
        private CompletionHandler _completionHandler;
        private HoverHandler _hoverHandler;
        private DefinitionHandler _definitionHandler;
        private SignatureHelpHandler _signatureHelpHandler;
        private DocumentSymbolHandler _documentSymbolHandler;

        public NoriLanguageServer(string catalogPath)
        {
            // Load catalog
            if (!string.IsNullOrEmpty(catalogPath) && File.Exists(catalogPath))
            {
                try
                {
                    var json = File.ReadAllText(catalogPath);
                    _catalog = FullCatalog.LoadFromJson(json);
                    Log($"Loaded catalog from {catalogPath}");
                }
                catch (Exception ex)
                {
                    Log($"Failed to load catalog: {ex.Message}");
                    _catalog = BuiltinCatalog.Instance;
                }
            }
            else
            {
                _catalog = BuiltinCatalog.Instance;
            }

            _diagnosticsPublisher = new DiagnosticsPublisher(this);
            _documents = new DocumentManager(
                _catalog,
                (uri, diags) => _diagnosticsPublisher.Publish(uri, diags),
                msg => Log(msg));
        }

        public void SetRpc(JsonRpc rpc)
        {
            _rpc = rpc;
        }

        public Task WaitForShutdown() => _shutdownTcs.Task;

        public void Log(string message)
        {
            Console.Error.WriteLine($"[nori-lsp] {message}");
        }

        public void SendNotification<T>(string method, T @params)
        {
            _rpc?.NotifyWithParameterObjectAsync(method, @params);
        }

        public DocumentManager Documents => _documents;

        // ===== Lifecycle =====

        [JsonRpcMethod("initialize")]
        public InitializeResult Initialize(JToken @params)
        {
            _completionHandler = new CompletionHandler(_documents, _catalog);
            _hoverHandler = new HoverHandler(_documents, _catalog);
            _definitionHandler = new DefinitionHandler(_documents);
            _signatureHelpHandler = new SignatureHelpHandler(_documents, _catalog);
            _documentSymbolHandler = new DocumentSymbolHandler(_documents);

            return new InitializeResult
            {
                Capabilities = new ServerCapabilities
                {
                    TextDocumentSync = new TextDocumentSyncOptions
                    {
                        OpenClose = true,
                        Change = TextDocumentSyncKind.Full,
                        Save = new SaveOptions { IncludeText = true }
                    },
                    CompletionProvider = new CompletionOptions
                    {
                        TriggerCharacters = new[] { ".", ":" },
                        ResolveProvider = false
                    },
                    HoverProvider = new HoverOptions(),
                    DefinitionProvider = new DefinitionOptions(),
                    SignatureHelpProvider = new SignatureHelpOptions
                    {
                        TriggerCharacters = new[] { "(", "," }
                    },
                    DocumentSymbolProvider = new DocumentSymbolOptions(),
                }
            };
        }

        [JsonRpcMethod("initialized")]
        public void Initialized()
        {
            Log("Client initialized.");
        }

        [JsonRpcMethod("shutdown")]
        public object Shutdown()
        {
            Log("Shutdown requested.");
            return null;
        }

        [JsonRpcMethod("exit")]
        public void Exit()
        {
            Log("Exit requested.");
            _shutdownTcs.TrySetResult(true);
        }

        // ===== Text Document Sync =====

        [JsonRpcMethod("textDocument/didOpen")]
        public void DidOpen(JToken @params)
        {
            var p = @params.ToObject<DidOpenTextDocumentParams>();
            _documents.OnDocumentOpened(p.TextDocument.Uri.ToString(), p.TextDocument.Text);
        }

        [JsonRpcMethod("textDocument/didChange")]
        public void DidChange(JToken @params)
        {
            var p = @params.ToObject<DidChangeTextDocumentParams>();
            if (p.ContentChanges != null && p.ContentChanges.Length > 0)
            {
                var lastChange = p.ContentChanges[p.ContentChanges.Length - 1];
                _documents.OnDocumentChanged(p.TextDocument.Uri.ToString(), lastChange.Text);
            }
        }

        [JsonRpcMethod("textDocument/didClose")]
        public void DidClose(JToken @params)
        {
            var p = @params.ToObject<DidCloseTextDocumentParams>();
            _documents.OnDocumentClosed(p.TextDocument.Uri.ToString());
        }

        [JsonRpcMethod("textDocument/didSave")]
        public void DidSave(JToken @params)
        {
            // Diagnostics are already published on change
        }

        // ===== Features =====

        [JsonRpcMethod("textDocument/completion")]
        public CompletionList Completion(JToken @params)
        {
            var p = @params.ToObject<CompletionParams>();
            return _completionHandler.Handle(p);
        }

        [JsonRpcMethod("textDocument/hover")]
        public Hover Hover(JToken @params)
        {
            var p = @params.ToObject<TextDocumentPositionParams>();
            return _hoverHandler.Handle(p);
        }

        [JsonRpcMethod("textDocument/definition")]
        public Location[] Definition(JToken @params)
        {
            var p = @params.ToObject<TextDocumentPositionParams>();
            return _definitionHandler.Handle(p);
        }

        [JsonRpcMethod("textDocument/signatureHelp")]
        public SignatureHelp SignatureHelp(JToken @params)
        {
            var p = @params.ToObject<SignatureHelpParams>();
            return _signatureHelpHandler.Handle(p);
        }

        [JsonRpcMethod("textDocument/documentSymbol")]
        public DocumentSymbol[] DocumentSymbol(JToken @params)
        {
            var p = @params.ToObject<DocumentSymbolParams>();
            return _documentSymbolHandler.Handle(p);
        }
    }
}
