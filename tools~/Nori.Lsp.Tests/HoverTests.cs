using Microsoft.VisualStudio.LanguageServer.Protocol;
using Nori.Lsp.Handlers;
using NUnit.Framework;

namespace Nori.Lsp.Tests
{
    [TestFixture]
    public class HoverTests
    {
        private static string GetHoverText(Hover hover)
        {
            if (hover?.Contents.Value is MarkupContent mc)
                return mc.Value;
            if (hover?.Contents.Value is string s)
                return s;
            return hover?.Contents.Value?.ToString() ?? "";
        }

        [Test]
        public void Hover_Variable_ShowsType()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "let score: int = 0\non Start {\n    log(score)\n}");

            var handler = new HoverHandler(client.Documents, client.Catalog);
            var hover = handler.Handle(new TextDocumentPositionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new System.Uri("file:///test.nori") },
                Position = new Position(2, 8)
            });

            Assert.That(hover, Is.Not.Null);
            var text = GetHoverText(hover);
            Assert.That(text, Does.Contain("int").Or.Contain("score"));
        }

        [Test]
        public void Hover_FunctionDecl_ShowsSignature()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "fn add(a: int, b: int) -> int {\n    return a\n}");

            var handler = new HoverHandler(client.Documents, client.Catalog);
            var hover = handler.Handle(new TextDocumentPositionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new System.Uri("file:///test.nori") },
                Position = new Position(0, 4)
            });

            Assert.That(hover, Is.Not.Null);
            var text = GetHoverText(hover);
            Assert.That(text, Does.Contain("fn add"));
        }

        [Test]
        public void Hover_EventHandler_ShowsName()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "on Start {\n    log(localPlayer)\n}");

            var handler = new HoverHandler(client.Documents, client.Catalog);
            var hover = handler.Handle(new TextDocumentPositionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new System.Uri("file:///test.nori") },
                Position = new Position(0, 3)
            });

            Assert.That(hover, Is.Not.Null);
            var text = GetHoverText(hover);
            Assert.That(text, Does.Contain("Start"));
        }

        [Test]
        public void Hover_BuiltinVariable_ShowsInfo()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "on Start {\n    log(localPlayer)\n}");

            var handler = new HoverHandler(client.Documents, client.Catalog);
            var hover = handler.Handle(new TextDocumentPositionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new System.Uri("file:///test.nori") },
                Position = new Position(1, 8)
            });

            Assert.That(hover, Is.Not.Null);
            var text = GetHoverText(hover);
            Assert.That(text, Does.Contain("localPlayer").Or.Contain("Player"));
        }

        [Test]
        public void Hover_ReturnsNull_ForEmptySpace()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "\n\n\n");

            var handler = new HoverHandler(client.Documents, client.Catalog);
            var hover = handler.Handle(new TextDocumentPositionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new System.Uri("file:///test.nori") },
                Position = new Position(1, 0)
            });

            Assert.That(hover, Is.Null);
        }
    }
}
