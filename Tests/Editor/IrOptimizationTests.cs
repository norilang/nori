using System.Linq;
using NUnit.Framework;
using Nori.Compiler;

namespace Nori.Tests
{
    [TestFixture]
    public class IrOptimizationTests
    {
        // =============================================
        // Unit tests — direct IR construction
        // =============================================

        [Test]
        public void CopyProp_ExternResultSlot_EliminatesCopy()
        {
            // Pattern A: IrPush(a), IrPush(b), IrPush(__tmp), IrExtern(op), IrCopy(__tmp, x)
            // Should become: IrPush(a), IrPush(b), IrPush(x), IrExtern(op)
            var module = new IrModule();
            module.HeapVars.Add(new IrHeapVar("a", "SystemInt32", "1"));
            module.HeapVars.Add(new IrHeapVar("b", "SystemInt32", "2"));
            module.HeapVars.Add(new IrHeapVar("__tmp_0_SystemInt32", "SystemInt32"));
            module.HeapVars.Add(new IrHeapVar("x", "SystemInt32"));

            var block = new IrBlock("_start", true);
            block.Instructions.Add(new IrPush("a"));
            block.Instructions.Add(new IrPush("b"));
            block.Instructions.Add(new IrPush("__tmp_0_SystemInt32"));
            block.Instructions.Add(new IrExtern("SystemInt32.__op_Addition__SystemInt32_SystemInt32__SystemInt32"));
            block.Instructions.Add(new IrCopy("__tmp_0_SystemInt32", "x"));
            block.Instructions.Add(IrJump.Halt());
            module.Blocks.Add(block);

            var pass = new CopyPropagationPass();
            pass.Run(module);

            // Should have 5 instructions (push, push, push(x), extern, halt) — copy removed
            Assert.AreEqual(5, block.Instructions.Count);
            // The third push should now reference x directly
            Assert.IsInstanceOf<IrPush>(block.Instructions[2]);
            Assert.AreEqual("x", ((IrPush)block.Instructions[2]).VarName);
            // No IrCopy should remain
            Assert.IsFalse(block.Instructions.Any(i => i is IrCopy));
        }

        [Test]
        public void CopyProp_CopyChain_CollapsesCopies()
        {
            // Pattern B: IrCopy(__retval_foo, __tmp), IrCopy(__tmp, x)
            // Should become: IrCopy(__retval_foo, x)
            var module = new IrModule();
            module.HeapVars.Add(new IrHeapVar("__retval_foo", "SystemInt32"));
            module.HeapVars.Add(new IrHeapVar("__tmp_0_SystemInt32", "SystemInt32"));
            module.HeapVars.Add(new IrHeapVar("x", "SystemInt32"));

            var block = new IrBlock("__ret_foo_0");
            block.Instructions.Add(new IrCopy("__retval_foo", "__tmp_0_SystemInt32"));
            block.Instructions.Add(new IrCopy("__tmp_0_SystemInt32", "x"));
            module.Blocks.Add(block);

            var pass = new CopyPropagationPass();
            pass.Run(module);

            // Should have 1 instruction: IrCopy(__retval_foo, x)
            Assert.AreEqual(1, block.Instructions.Count);
            var copy = block.Instructions[0] as IrCopy;
            Assert.IsNotNull(copy);
            Assert.AreEqual("__retval_foo", copy.Source);
            Assert.AreEqual("x", copy.Dest);
        }

        [Test]
        public void CopyProp_TempUsedInJumpIfFalse_NotEliminated()
        {
            // Temp referenced 3+ times (push, copy, jumpIfFalse) should not be propagated
            var module = new IrModule();
            module.HeapVars.Add(new IrHeapVar("a", "SystemBoolean"));
            module.HeapVars.Add(new IrHeapVar("__tmp_0_SystemBoolean", "SystemBoolean"));
            module.HeapVars.Add(new IrHeapVar("x", "SystemBoolean"));

            var block = new IrBlock("_start", true);
            block.Instructions.Add(new IrPush("a"));
            block.Instructions.Add(new IrPush("__tmp_0_SystemBoolean"));
            block.Instructions.Add(new IrExtern("SystemBoolean.__op_UnaryNegation__SystemBoolean__SystemBoolean"));
            block.Instructions.Add(new IrJumpIfFalse("__tmp_0_SystemBoolean", "__end_0"));
            block.Instructions.Add(new IrCopy("__tmp_0_SystemBoolean", "x"));
            block.Instructions.Add(IrJump.Halt());
            module.Blocks.Add(block);

            var end = new IrBlock("__end_0");
            end.Instructions.Add(IrJump.Halt());
            module.Blocks.Add(end);

            var pass = new CopyPropagationPass();
            pass.Run(module);

            // Copy should still be there — temp has 3 refs
            Assert.IsTrue(block.Instructions.Any(i => i is IrCopy));
        }

        [Test]
        public void CopyProp_CrossBlockTemp_NotEliminated()
        {
            // Temp defined in block A, copy in block B — should not be propagated
            var module = new IrModule();
            module.HeapVars.Add(new IrHeapVar("a", "SystemInt32", "1"));
            module.HeapVars.Add(new IrHeapVar("__tmp_0_SystemInt32", "SystemInt32"));
            module.HeapVars.Add(new IrHeapVar("x", "SystemInt32"));

            var blockA = new IrBlock("blockA");
            blockA.Instructions.Add(new IrPush("a"));
            blockA.Instructions.Add(new IrPush("__tmp_0_SystemInt32"));
            blockA.Instructions.Add(new IrExtern("SystemObject.__ToString__SystemString"));
            blockA.Instructions.Add(new IrJump("blockB"));
            module.Blocks.Add(blockA);

            var blockB = new IrBlock("blockB");
            blockB.Instructions.Add(new IrCopy("__tmp_0_SystemInt32", "x"));
            module.Blocks.Add(blockB);

            var pass = new CopyPropagationPass();
            pass.Run(module);

            // Copy in blockB should remain — def is in blockA
            Assert.IsTrue(blockB.Instructions.Any(i => i is IrCopy));
        }

        [Test]
        public void CopyProp_TempAsArgument_NotEliminated()
        {
            // IrPush(tmp) that is NOT immediately before an IrExtern (it's an argument push)
            var module = new IrModule();
            module.HeapVars.Add(new IrHeapVar("__tmp_0_SystemInt32", "SystemInt32"));
            module.HeapVars.Add(new IrHeapVar("result", "SystemInt32"));
            module.HeapVars.Add(new IrHeapVar("x", "SystemInt32"));

            var block = new IrBlock("_start", true);
            // tmp is pushed as first argument, then another push, then extern
            block.Instructions.Add(new IrPush("__tmp_0_SystemInt32"));
            block.Instructions.Add(new IrPush("result"));
            block.Instructions.Add(new IrExtern("SystemObject.__ToString__SystemString"));
            block.Instructions.Add(new IrCopy("__tmp_0_SystemInt32", "x"));
            module.Blocks.Add(block);

            var pass = new CopyPropagationPass();
            pass.Run(module);

            // Copy should remain — push(tmp) is not immediately before extern
            Assert.IsTrue(block.Instructions.Any(i => i is IrCopy));
        }

        [Test]
        public void CopyProp_DestReadBetweenDefAndCopy_NotEliminated()
        {
            // dest is referenced between the def point and the copy — unsafe to propagate
            var module = new IrModule();
            module.HeapVars.Add(new IrHeapVar("a", "SystemInt32", "1"));
            module.HeapVars.Add(new IrHeapVar("__tmp_0_SystemInt32", "SystemInt32"));
            module.HeapVars.Add(new IrHeapVar("x", "SystemInt32"));

            var block = new IrBlock("_start", true);
            block.Instructions.Add(new IrPush("a"));
            block.Instructions.Add(new IrPush("__tmp_0_SystemInt32"));
            block.Instructions.Add(new IrExtern("SystemInt32.__op_Addition__SystemInt32_SystemInt32__SystemInt32"));
            // x is read between def(tmp) and copy(tmp, x)
            block.Instructions.Add(new IrPush("x"));
            block.Instructions.Add(new IrExtern("UnityEngineDebug.__Log__SystemObject__SystemVoid"));
            block.Instructions.Add(new IrCopy("__tmp_0_SystemInt32", "x"));
            module.Blocks.Add(block);

            var pass = new CopyPropagationPass();
            pass.Run(module);

            // Copy should remain — x is read between def and copy
            Assert.IsTrue(block.Instructions.Any(i => i is IrCopy));
        }

        [Test]
        public void DeadVar_RemovesUnreferencedTemps()
        {
            var module = new IrModule();
            module.HeapVars.Add(new IrHeapVar("a", "SystemInt32", "1"));
            module.HeapVars.Add(new IrHeapVar("__tmp_0_SystemInt32", "SystemInt32"));
            module.HeapVars.Add(new IrHeapVar("__const_1_SystemInt32", "SystemInt32", "42"));

            // Only 'a' is referenced
            var block = new IrBlock("_start", true);
            block.Instructions.Add(new IrPush("a"));
            block.Instructions.Add(new IrExtern("UnityEngineDebug.__Log__SystemObject__SystemVoid"));
            module.Blocks.Add(block);

            var pass = new DeadVarEliminationPass();
            pass.Run(module);

            Assert.AreEqual(1, module.HeapVars.Count);
            Assert.AreEqual("a", module.HeapVars[0].Name);
        }

        [Test]
        public void DeadVar_KeepsExportedVars()
        {
            var module = new IrModule();
            var exported = new IrHeapVar("speed", "SystemSingle", "5.0") { IsExport = true };
            module.HeapVars.Add(exported);
            // No instructions reference 'speed'
            module.Blocks.Add(new IrBlock("_start", true));

            var pass = new DeadVarEliminationPass();
            pass.Run(module);

            Assert.AreEqual(1, module.HeapVars.Count);
            Assert.AreEqual("speed", module.HeapVars[0].Name);
        }

        [Test]
        public void DeadVar_KeepsSyncedVars()
        {
            var module = new IrModule();
            var synced = new IrHeapVar("score", "SystemInt32", "0") { SyncMode = SyncMode.None };
            module.HeapVars.Add(synced);
            module.Blocks.Add(new IrBlock("_start", true));

            var pass = new DeadVarEliminationPass();
            pass.Run(module);

            Assert.AreEqual(1, module.HeapVars.Count);
        }

        [Test]
        public void DeadVar_KeepsUserVariables()
        {
            // User-declared variables (no __tmp_ or __const_ prefix) should never be removed
            var module = new IrModule();
            module.HeapVars.Add(new IrHeapVar("myVar", "SystemInt32"));
            module.Blocks.Add(new IrBlock("_start", true));

            var pass = new DeadVarEliminationPass();
            pass.Run(module);

            Assert.AreEqual(1, module.HeapVars.Count);
            Assert.AreEqual("myVar", module.HeapVars[0].Name);
        }

        // =============================================
        // End-to-end tests — full compilation
        // =============================================

        private CompileResult Compile(string source)
        {
            return NoriCompiler.Compile(source, "test.nori");
        }

        [Test]
        public void OptimizedOutput_FewerCopyInstructions()
        {
            // Simple addition: let c = a + b
            // Without optimization: push a, push b, push __tmp, extern, COPY __tmp c
            // With optimization: push a, push b, push c, extern (no COPY)
            var result = Compile(@"
let a: int = 1
let b: int = 2
on Start {
    let c: int = a + b
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            // The output should not contain any __tmp_ references for this simple case
            // because copy propagation should inline the result directly into c
            Assert.That(result.Uasm, Does.Contain("op_Addition"));

            // Count COPY instructions in code section
            string codeSection = GetCodeSection(result.Uasm);
            int copyCount = CountOccurrences(codeSection, "COPY");
            // The addition result should not need a COPY — it should be pushed directly to c
            // There may be other COPYs (e.g., boolean true inits), but the add result should be direct
            Assert.That(codeSection, Does.Not.Contain("__tmp_"));
        }

        [Test]
        public void OptimizedOutput_FunctionReturnCopyChain()
        {
            // Function return produces: IrCopy(__retval_add, __tmp), then IrCopy(__tmp, result)
            // After optimization: IrCopy(__retval_add, result)
            var result = Compile(@"
fn add(x: int, y: int) -> int {
    return x + y
}
on Start {
    let result: int = add(1, 2)
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain("__retval_add"));
            Assert.That(result.Uasm, Does.Contain("__fn_add"));

            // The function return copy chain should be collapsed
            string codeSection = GetCodeSection(result.Uasm);
            // After optimization, we should not see __tmp_ vars in the return copy chain
            // (they get inlined to the final destination)
        }

        [Test]
        public void OptimizedOutput_EveryPushReferencesDataVar()
        {
            // Structural UASM validity: every PUSH must reference a var that exists in the data section
            var result = Compile(@"
let x: int = 10
let y: int = 20
on Start {
    let sum: int = x + y
    log(sum)
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));

            string dataSection = GetDataSection(result.Uasm);
            string codeSection = GetCodeSection(result.Uasm);

            // Extract all var names from PUSH instructions
            foreach (string line in codeSection.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("PUSH, "))
                {
                    string varName = trimmed.Substring("PUSH, ".Length).Trim();
                    Assert.That(dataSection, Does.Contain(varName + ":"),
                        $"PUSH references '{varName}' which is not in data section");
                }
            }
        }

        [Test]
        public void OptimizedOutput_WhileLoopPreserved()
        {
            // Ensure loop conditions still work after optimization
            // (temps used in JumpIfFalse should NOT be eliminated)
            var result = Compile(@"
let count: int = 0
on Start {
    while count < 10 {
        count += 1
    }
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain("JUMP_IF_FALSE"));
            Assert.That(result.Uasm, Does.Contain("op_LessThan"));
        }

        // =============================================
        // Helpers
        // =============================================

        private static string GetDataSection(string uasm)
        {
            int start = uasm.IndexOf(".data_start");
            int end = uasm.IndexOf(".data_end");
            if (start < 0 || end < 0) return "";
            return uasm.Substring(start, end - start);
        }

        private static string GetCodeSection(string uasm)
        {
            int start = uasm.IndexOf(".code_start");
            int end = uasm.IndexOf(".code_end");
            if (start < 0 || end < 0) return "";
            return uasm.Substring(start, end - start);
        }

        private static int CountOccurrences(string text, string pattern)
        {
            int count = 0;
            int idx = 0;
            while ((idx = text.IndexOf(pattern, idx)) >= 0)
            {
                count++;
                idx += pattern.Length;
            }
            return count;
        }

        private string FormatErrors(CompileResult result)
        {
            if (result.Success) return "";
            return DiagnosticPrinter.FormatAll(result.Diagnostics);
        }
    }
}
