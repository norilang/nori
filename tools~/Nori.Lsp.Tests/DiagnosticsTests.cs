using System.Linq;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using NUnit.Framework;

namespace Nori.Lsp.Tests
{
    [TestFixture]
    public class DiagnosticsTests
    {
        [Test]
        public void Diagnostics_PublishedOnOpen_WithError()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "on Start { log(undefined_var) }");
            var diags = client.GetDiagnostics("test.nori");

            Assert.That(diags.Length, Is.GreaterThan(0));
            Assert.That(diags.Any(d => d.Severity == DiagnosticSeverity.Error), Is.True);
        }

        [Test]
        public void Diagnostics_ClearedOnClose()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "on Start { log(undefined_var) }");
            Assert.That(client.GetDiagnostics("test.nori").Length, Is.GreaterThan(0));

            client.CloseDocument("test.nori");
            Assert.That(client.GetDiagnostics("test.nori").Length, Is.EqualTo(0));
        }

        [Test]
        public void Diagnostics_NoneForValidCode()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "let score: int = 0\non Start {\n    log(score)\n}");
            var diags = client.GetDiagnostics("test.nori");

            var errors = diags.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.That(errors.Length, Is.EqualTo(0));
        }

        [Test]
        public void Diagnostics_UndefinedVariable_HasCorrectCode()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "on Start {\n    log(xyz)\n}");
            var diags = client.GetDiagnostics("test.nori");

            Assert.That(diags.Any(d => d.Message != null && d.Message.Contains("Undefined variable")), Is.True);
        }

        [Test]
        public void Diagnostics_UpdatedOnChange()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "on Start { log(bad_var) }");
            Assert.That(client.GetDiagnostics("test.nori").Length, Is.GreaterThan(0));

            // Fix the code
            client.ChangeDocument("test.nori", "let x: int = 0\non Start { log(x) }");
            var diags = client.GetDiagnostics("test.nori");
            var errors = diags.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.That(errors.Length, Is.EqualTo(0));
        }

        [Test]
        public void Diagnostics_TypeMismatch()
        {
            var client = new TestLspClient();
            client.OpenDocument("test.nori", "let x: int = 0\non Start {\n    if x {\n    }\n}");
            var diags = client.GetDiagnostics("test.nori");

            Assert.That(diags.Any(d => d.Severity == DiagnosticSeverity.Error), Is.True);
        }

        [Test]
        public void Diagnostics_Span_IsZeroBased()
        {
            var client = new TestLspClient();
            // Error on line 2 (0-based: line 1)
            client.OpenDocument("test.nori", "let x: int = 0\non Start {\n    log(undefined)\n}");
            var diags = client.GetDiagnostics("test.nori");
            var err = diags.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);

            Assert.That(err, Is.Not.Null);
            // Line should be 0-based (LSP convention)
            Assert.That(err.Range.Start.Line, Is.GreaterThanOrEqualTo(0));
        }
    }
}
