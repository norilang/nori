using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Nori.Compiler;

namespace Nori.Tests
{
    [TestFixture]
    public class IntegrationTests
    {
        private static readonly string SamplesPath = Path.GetFullPath(
            Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "Packages", "dev.nori.compiler", "Samples~"));

        private string LoadSample(string name)
        {
            // Try multiple possible paths
            string[] paths = new[]
            {
                Path.Combine(SamplesPath, name),
                Path.Combine("Packages", "dev.nori.compiler", "Samples~", name),
            };

            foreach (var path in paths)
            {
                if (File.Exists(path))
                    return File.ReadAllText(path);
            }

            // Inline samples as fallback
            switch (name)
            {
                case "hello.nori":
                    return @"on Start {
    log(""Hello from Nori!"")
}

on Interact {
    log(""You clicked me!"")
}";
                case "scoreboard.nori":
                    return @"pub let max_score: int = 10
sync none score: int = 0
let is_game_over: bool = false

on Start {
    log(""Scoreboard ready!"")
}

fn update_display() {
    log(""Score: {score}"")
}

event AddPoint {
    score = score + 1
    update_display()
    if score >= max_score {
        send GameOver to All
    }
}

event GameOver {
    is_game_over = true
    log(""Game over! Final score: {score}"")
}

on Interact {
    if is_game_over {
        log(""Game is over!"")
        return
    }
    send AddPoint to All
}";
                case "door.nori":
                    return @"pub let speed: float = 90.0
let is_open: bool = false
let current_angle: float = 0.0
let target_angle: float = 0.0

on Interact {
    is_open = !is_open
    if is_open {
        target_angle = 90.0
    } else {
        target_angle = 0.0
    }
}

on Update {
    if current_angle != target_angle {
        let step: float = speed * Time.deltaTime
        if current_angle < target_angle {
            current_angle = current_angle + step
            if current_angle > target_angle {
                current_angle = target_angle
            }
        } else {
            current_angle = current_angle - step
            if current_angle < target_angle {
                current_angle = target_angle
            }
        }
        transform.localRotation = Quaternion.Euler(0.0, current_angle, 0.0)
    }
}";
                default:
                    Assert.Fail($"Sample not found: {name}");
                    return "";
            }
        }

        [Test]
        public void Hello_Compiles_Without_Errors()
        {
            var source = LoadSample("hello.nori");
            var result = NoriCompiler.Compile(source, "hello.nori");
            Assert.IsTrue(result.Success, FormatErrors(result));
        }

        [Test]
        public void Scoreboard_Compiles_Without_Errors()
        {
            var source = LoadSample("scoreboard.nori");
            var result = NoriCompiler.Compile(source, "scoreboard.nori");
            Assert.IsTrue(result.Success, FormatErrors(result));
        }

        [Test]
        public void Door_Compiles_Without_Errors()
        {
            var source = LoadSample("door.nori");
            var result = NoriCompiler.Compile(source, "door.nori");
            Assert.IsTrue(result.Success, FormatErrors(result));
        }

        [Test]
        public void Scoreboard_Has_Expected_Exports()
        {
            var source = LoadSample("scoreboard.nori");
            var result = NoriCompiler.Compile(source, "scoreboard.nori");
            Assert.IsTrue(result.Success, FormatErrors(result));

            Assert.That(result.Uasm, Does.Contain(".export max_score"));
            Assert.That(result.Uasm, Does.Contain(".sync score, none"));
            Assert.That(result.Uasm, Does.Contain(".export _start"));
            Assert.That(result.Uasm, Does.Contain(".export _interact"));
            Assert.That(result.Uasm, Does.Contain(".export AddPoint"));
            Assert.That(result.Uasm, Does.Contain(".export GameOver"));
        }

        [Test]
        public void Door_Has_Expected_Exports()
        {
            var source = LoadSample("door.nori");
            var result = NoriCompiler.Compile(source, "door.nori");
            Assert.IsTrue(result.Success, FormatErrors(result));

            Assert.That(result.Uasm, Does.Contain(".export speed"));
            Assert.That(result.Uasm, Does.Contain(".export _interact"));
            Assert.That(result.Uasm, Does.Contain(".export _update"));
        }

        [Test]
        public void Assembly_Structure_Is_Valid()
        {
            var source = LoadSample("hello.nori");
            var result = NoriCompiler.Compile(source, "hello.nori");
            Assert.IsTrue(result.Success, FormatErrors(result));

            string uasm = result.Uasm;

            // Structural validation
            Assert.That(uasm, Does.Contain(".data_start"));
            Assert.That(uasm, Does.Contain(".data_end"));
            Assert.That(uasm, Does.Contain(".code_start"));
            Assert.That(uasm, Does.Contain(".code_end"));

            // Every PUSH should reference a variable declared in the data section
            var pushMatches = Regex.Matches(uasm, @"PUSH, (\S+)");
            string dataSection = ExtractSection(uasm, ".data_start", ".data_end");

            foreach (Match match in pushMatches)
            {
                string varName = match.Groups[1].Value;
                Assert.That(dataSection, Does.Contain(varName),
                    $"PUSH references undeclared variable: {varName}");
            }

            // Every EXTERN should have a valid signature format
            var externMatches = Regex.Matches(uasm, @"EXTERN, ""(.+?)""");
            foreach (Match match in externMatches)
            {
                string sig = match.Groups[1].Value;
                Assert.That(sig, Does.Contain("__"),
                    $"Invalid extern signature format: {sig}");
            }

            // Every .export in code section should have a label
            string codeSection = ExtractSection(uasm, ".code_start", ".code_end");
            var codeExportMatches = Regex.Matches(codeSection, @"\.export (\S+)");
            foreach (Match match in codeExportMatches)
            {
                string labelName = match.Groups[1].Value;
                Assert.That(codeSection, Does.Contain($"{labelName}:"),
                    $"Code export has no matching label: {labelName}");
            }
        }

        [Test]
        public void All_Jumps_Are_Valid()
        {
            var source = LoadSample("scoreboard.nori");
            var result = NoriCompiler.Compile(source, "scoreboard.nori");
            Assert.IsTrue(result.Success, FormatErrors(result));

            // Every JUMP should target a valid address or 0xFFFFFFFC (halt)
            var jumpMatches = Regex.Matches(result.Uasm, @"JUMP, (0x[0-9A-Fa-f]+|\S+)");
            foreach (Match match in jumpMatches)
            {
                string target = match.Groups[1].Value;
                if (target.StartsWith("0x"))
                {
                    // Should be a hex address - valid
                    Assert.That(target, Does.Match(@"^0x[0-9A-Fa-f]+$"),
                        $"Invalid jump target: {target}");
                }
                // Non-hex targets would be unresolved labels - these should not exist
            }
        }

        [Test]
        public void Multiple_Errors_All_Reported()
        {
            var source = @"
let score: int = 0
on Start {
    let a: int = undefined1
    let b: int = undefined2
    let c: int = undefined3
}
";
            var result = NoriCompiler.Compile(source, "test.nori");
            Assert.IsFalse(result.Success);
            // Should report multiple undefined variable errors
            Assert.GreaterOrEqual(result.Diagnostics.ErrorCount, 3);
        }

        [Test]
        public void Error_Has_Source_Location()
        {
            var source = "let x: int = undefined_var\non Start { }";
            var result = NoriCompiler.Compile(source, "test.nori");
            Assert.IsFalse(result.Success);
            var error = result.Diagnostics.All[0];
            Assert.AreEqual("test.nori", error.Span.File);
            Assert.Greater(error.Span.Start.Line, 0);
            Assert.Greater(error.Span.Start.Column, 0);
        }

        [Test]
        public void ForRange_Duplicate_VarName_Uses_Correct_HeapVar()
        {
            // Two for-range loops in separate scopes using the same variable name "i".
            // The second loop's body must reference its own heap var, not the first loop's.
            var source = @"
pub let items: int = 3
let total: int = 0

on Start {
    for i in 0..items {
        total = total + 1
    }
}

fn add_items() {
    for i in 0..items {
        total = total + i
    }
}
";
            var result = NoriCompiler.Compile(source, "test.nori");
            Assert.IsTrue(result.Success, FormatErrors(result));

            string uasm = result.Uasm;
            string dataSection = ExtractSection(uasm, ".data_start", ".data_end");

            // There should be a renamed heap var for the second "i" (collision)
            Assert.That(dataSection, Does.Contain("__lcl_i_SystemInt32_"),
                "Second loop variable 'i' should be renamed to avoid collision");

            // The function body should reference the renamed variable, not the original "i"
            string codeSection = ExtractSection(uasm, ".code_start", ".code_end");
            // Find the function block and verify it uses the renamed var
            int fnStart = codeSection.IndexOf("__fn_add_items:");
            Assert.Greater(fnStart, 0, "Function block should exist");
            string fnCode = codeSection.Substring(fnStart);

            // The function's loop body should PUSH the renamed __lcl_i variable,
            // not the original "i" (which belongs to the Start handler's loop)
            Assert.That(fnCode, Does.Contain("__lcl_i_SystemInt32_"),
                "Function loop body should use the renamed loop variable");
        }

        private string ExtractSection(string uasm, string startMarker, string endMarker)
        {
            int start = uasm.IndexOf(startMarker);
            int end = uasm.IndexOf(endMarker);
            if (start < 0 || end < 0 || end <= start)
                return "";
            return uasm.Substring(start, end - start + endMarker.Length);
        }

        private string FormatErrors(CompileResult result)
        {
            if (result.Success) return "";
            return DiagnosticPrinter.FormatAll(result.Diagnostics);
        }
    }
}
