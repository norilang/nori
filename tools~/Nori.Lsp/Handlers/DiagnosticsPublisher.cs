using System;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Nori.Lsp.Server;

namespace Nori.Lsp.Handlers
{
    public class DiagnosticsPublisher
    {
        private readonly NoriLanguageServer _server;

        public DiagnosticsPublisher(NoriLanguageServer server)
        {
            _server = server;
        }

        public void Publish(string uri, Diagnostic[] diagnostics)
        {
            _server.SendNotification("textDocument/publishDiagnostics",
                new PublishDiagnosticParams
                {
                    Uri = new Uri(uri.StartsWith("file://") ? uri : "file:///" + uri.Replace("\\", "/")),
                    Diagnostics = diagnostics
                });
        }
    }
}
