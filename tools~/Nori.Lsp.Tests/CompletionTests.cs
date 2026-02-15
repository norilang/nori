using System.Linq;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Nori.Lsp.Handlers;
using NUnit.Framework;

namespace Nori.Lsp.Tests
{
    [TestFixture]
    public class CompletionTests
    {
        [Test]
        public void Completion_Keywords_InStatementContext()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "on Start {\n    \n}");

            var handler = new CompletionHandler(client.Documents, client.Catalog);
            var result = handler.Handle(new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new System.Uri("file:///test.nori") },
                Position = new Position(1, 4)
            });

            Assert.That(result.Items, Is.Not.Empty);
            var labels = result.Items.Select(i => i.Label).ToArray();
            Assert.That(labels, Does.Contain("if"));
            Assert.That(labels, Does.Contain("let"));
            Assert.That(labels, Does.Contain("return"));
        }

        [Test]
        public void Completion_InScopeVariables()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "let score: int = 0\non Start {\n    \n}");

            var handler = new CompletionHandler(client.Documents, client.Catalog);
            var result = handler.Handle(new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new System.Uri("file:///test.nori") },
                Position = new Position(2, 4)
            });

            var labels = result.Items.Select(i => i.Label).ToArray();
            Assert.That(labels, Does.Contain("score"));
        }

        [Test]
        public void Completion_TypesAfterColon()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "let x: ");

            var handler = new CompletionHandler(client.Documents, client.Catalog);
            var result = handler.Handle(new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new System.Uri("file:///test.nori") },
                Position = new Position(0, 7)
            });

            var labels = result.Items.Select(i => i.Label).ToArray();
            Assert.That(labels, Does.Contain("int"));
            Assert.That(labels, Does.Contain("float"));
            Assert.That(labels, Does.Contain("string"));
            Assert.That(labels, Does.Contain("Vector3"));
        }

        [Test]
        public void Completion_SyncModes()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "sync ");

            var handler = new CompletionHandler(client.Documents, client.Catalog);
            var result = handler.Handle(new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new System.Uri("file:///test.nori") },
                Position = new Position(0, 5)
            });

            var labels = result.Items.Select(i => i.Label).ToArray();
            Assert.That(labels, Does.Contain("none"));
            Assert.That(labels, Does.Contain("linear"));
            Assert.That(labels, Does.Contain("smooth"));
        }

        [Test]
        public void Completion_EventNames()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "on ");

            var handler = new CompletionHandler(client.Documents, client.Catalog);
            var result = handler.Handle(new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new System.Uri("file:///test.nori") },
                Position = new Position(0, 3)
            });

            var labels = result.Items.Select(i => i.Label).ToArray();
            Assert.That(labels, Does.Contain("Start"));
            Assert.That(labels, Does.Contain("Update"));
            Assert.That(labels, Does.Contain("Interact"));
            Assert.That(labels, Does.Contain("PlayerJoined"));
        }
    }
}
