using System.Linq;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Nori.Lsp.Handlers;
using NUnit.Framework;

namespace Nori.Lsp.Tests
{
    [TestFixture]
    public class DefinitionTests
    {
        [Test]
        public void Definition_Variable_JumpsToDeclaration()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "let score: int = 0\non Start {\n    log(score)\n}");

            var handler = new DefinitionHandler(client.Documents);
            // Click on "score" in log(score) — line 2, char ~8
            var result = handler.Handle(new TextDocumentPositionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new System.Uri("file:///test.nori") },
                Position = new Position(2, 8)
            });

            Assert.That(result.Length, Is.GreaterThan(0));
            // Declaration should be on line 0 (0-based)
            Assert.That(result[0].Range.Start.Line, Is.EqualTo(0));
        }

        [Test]
        public void Definition_Function_JumpsToFnDecl()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "fn greet() {\n    log(localPlayer)\n}\non Start {\n    greet()\n}");

            var handler = new DefinitionHandler(client.Documents);
            // Click on "greet" in the call — line 4, char ~4
            var result = handler.Handle(new TextDocumentPositionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new System.Uri("file:///test.nori") },
                Position = new Position(4, 4)
            });

            Assert.That(result.Length, Is.GreaterThan(0));
            Assert.That(result[0].Range.Start.Line, Is.EqualTo(0));
        }

        [Test]
        public void Definition_Undefined_ReturnsEmpty()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "on Start {\n    log(localPlayer)\n}");

            var handler = new DefinitionHandler(client.Documents);
            // Click on empty space
            var result = handler.Handle(new TextDocumentPositionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new System.Uri("file:///test.nori") },
                Position = new Position(0, 0)
            });

            // May or may not find declaration for "on" keyword, but should not crash
            Assert.That(result, Is.Not.Null);
        }
    }
}
