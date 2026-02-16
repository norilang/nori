using System.Collections.Generic;

namespace Nori.Compiler
{
    public class CopyPropagationPass : IIrPass
    {
        public string Name => "CopyPropagation";

        public void Run(IrModule module)
        {
            var refCounts = BuildRefCounts(module);

            foreach (var block in module.Blocks)
                OptimizeBlock(block, refCounts);
        }

        private static Dictionary<string, int> BuildRefCounts(IrModule module)
        {
            var counts = new Dictionary<string, int>();

            foreach (var block in module.Blocks)
            {
                foreach (var instr in block.Instructions)
                {
                    switch (instr)
                    {
                        case IrPush push:
                            Increment(counts, push.VarName);
                            break;
                        case IrCopy copy:
                            Increment(counts, copy.Source);
                            Increment(counts, copy.Dest);
                            break;
                        case IrJumpIfFalse jif:
                            Increment(counts, jif.ConditionVar);
                            break;
                        case IrJumpIndirect ji:
                            Increment(counts, ji.AddressVar);
                            break;
                    }
                }
            }

            return counts;
        }

        private static void Increment(Dictionary<string, int> counts, string name)
        {
            if (name == null) return;
            if (counts.TryGetValue(name, out int c))
                counts[name] = c + 1;
            else
                counts[name] = 1;
        }

        private static void OptimizeBlock(IrBlock block, Dictionary<string, int> refCounts)
        {
            var instrs = block.Instructions;

            // Forward scan for IrCopy(tmp, dest) where tmp is a single-use __tmp_
            for (int i = instrs.Count - 1; i >= 0; i--)
            {
                if (!(instrs[i] is IrCopy copy)) continue;

                string tmp = copy.Source;
                string dest = copy.Dest;

                // Only optimize __tmp_ vars with exactly 2 references (the write + this read)
                if (!tmp.StartsWith("__tmp_")) continue;
                if (!refCounts.TryGetValue(tmp, out int rc) || rc != 2) continue;

                // Scan backwards in the same block to find the instruction that writes to tmp
                for (int j = i - 1; j >= 0; j--)
                {
                    // Pattern A: IrPush(tmp) immediately before IrExtern (extern result slot)
                    if (instrs[j] is IrPush push && push.VarName == tmp)
                    {
                        // The push must be immediately followed by an IrExtern
                        if (j + 1 < instrs.Count && instrs[j + 1] is IrExtern)
                        {
                            // Check dest is not referenced between the push and the copy
                            if (!IsVarReferencedBetween(instrs, j, i, dest))
                            {
                                instrs[j] = new IrPush(dest);
                                instrs.RemoveAt(i);
                                refCounts[tmp] = 0;
                            }
                        }
                        break; // tmp was referenced here, stop scanning
                    }

                    // Pattern B: IrCopy(src, tmp) — copy chain
                    if (instrs[j] is IrCopy prevCopy && prevCopy.Dest == tmp)
                    {
                        // Check dest is not referenced between the def and the copy
                        if (!IsVarReferencedBetween(instrs, j, i, dest))
                        {
                            instrs[j] = new IrCopy(prevCopy.Source, dest);
                            instrs.RemoveAt(i);
                            refCounts[tmp] = 0;
                        }
                        break; // tmp was written here, stop scanning
                    }

                    // tmp is read/referenced in some other way (e.g., as an argument) — bail out
                    if (IsVarRead(instrs[j], tmp))
                        break;

                    // dest is referenced between the potential def and the copy — bail out
                    if (IsVarReferenced(instrs[j], dest))
                        break;
                }
            }
        }

        /// <summary>
        /// Returns true if the instruction reads the given variable (uses it as input).
        /// </summary>
        private static bool IsVarRead(IrInstruction instr, string varName)
        {
            switch (instr)
            {
                case IrPush push:
                    return push.VarName == varName;
                case IrCopy copy:
                    return copy.Source == varName;
                case IrJumpIfFalse jif:
                    return jif.ConditionVar == varName;
                case IrJumpIndirect ji:
                    return ji.AddressVar == varName;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns true if the instruction references the given variable in any position.
        /// </summary>
        private static bool IsVarReferenced(IrInstruction instr, string varName)
        {
            switch (instr)
            {
                case IrPush push:
                    return push.VarName == varName;
                case IrCopy copy:
                    return copy.Source == varName || copy.Dest == varName;
                case IrJumpIfFalse jif:
                    return jif.ConditionVar == varName;
                case IrJumpIndirect ji:
                    return ji.AddressVar == varName;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns true if varName is referenced in any instruction in (startExclusive, endExclusive).
        /// </summary>
        private static bool IsVarReferencedBetween(List<IrInstruction> instrs,
            int startExclusive, int endExclusive, string varName)
        {
            for (int k = startExclusive + 1; k < endExclusive; k++)
            {
                if (IsVarReferenced(instrs[k], varName))
                    return true;
            }
            return false;
        }
    }
}
