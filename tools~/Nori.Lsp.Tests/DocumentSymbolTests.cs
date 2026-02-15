using System.Linq;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Nori.Lsp.Handlers;
using NUnit.Framework;
using LspSymbolKind = Microsoft.VisualStudio.LanguageServer.Protocol.SymbolKind;

namespace Nori.Lsp.Tests
{
    [TestFixture]
    public class DocumentSymbolTests
    {
        [Test]
        public void DocumentSymbols_Variables()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "let score: int = 0\npub let name: string = \"test\"");

            var handler = new DocumentSymbolHandler(client.Documents);
            var result = handler.Handle(new DocumentSymbolParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new System.Uri("file:///test.nori") }
            });

            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result[0].Name, Is.EqualTo("score"));
            Assert.That(result[0].Kind, Is.EqualTo(LspSymbolKind.Variable));
            Assert.That(result[1].Name, Is.EqualTo("name"));
        }

        [Test]
        public void DocumentSymbols_Functions()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "fn greet(name: string) -> string {\n    return name\n}");

            var handler = new DocumentSymbolHandler(client.Documents);
            var result = handler.Handle(new DocumentSymbolParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new System.Uri("file:///test.nori") }
            });

            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0].Name, Is.EqualTo("greet"));
            Assert.That(result[0].Kind, Is.EqualTo(LspSymbolKind.Function));
            Assert.That(result[0].Detail, Does.Contain("name: string"));
        }

        [Test]
        public void DocumentSymbols_Events()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "on Start {\n    log(localPlayer)\n}");

            var handler = new DocumentSymbolHandler(client.Documents);
            var result = handler.Handle(new DocumentSymbolParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new System.Uri("file:///test.nori") }
            });

            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0].Name, Is.EqualTo("on Start"));
            Assert.That(result[0].Kind, Is.EqualTo(LspSymbolKind.Event));
        }

        [Test]
        public void DocumentSymbols_CustomEvents()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "event Reset {\n    log(localPlayer)\n}");

            var handler = new DocumentSymbolHandler(client.Documents);
            var result = handler.Handle(new DocumentSymbolParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new System.Uri("file:///test.nori") }
            });

            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0].Name, Is.EqualTo("event Reset"));
            Assert.That(result[0].Kind, Is.EqualTo(LspSymbolKind.Event));
        }

        [Test]
        public void DocumentSymbols_MixedDeclarations()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori",
                "let score: int = 0\nfn add(a: int, b: int) -> int {\n    return a\n}\non Start {\n    log(score)\n}\nevent Reset {\n    log(score)\n}");

            var handler = new DocumentSymbolHandler(client.Documents);
            var result = handler.Handle(new DocumentSymbolParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new System.Uri("file:///test.nori") }
            });

            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(result.Select(s => s.Name),
                Does.Contain("score")
                .And.Contain("add")
                .And.Contain("on Start")
                .And.Contain("event Reset"));
        }
    }
}
