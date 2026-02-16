using System.Collections.Generic;

namespace Nori.Compiler
{
    public class DeadVarEliminationPass : IIrPass
    {
        public string Name => "DeadVarElimination";

        public void Run(IrModule module)
        {
            // Collect all variable names referenced by any instruction
            var referenced = new HashSet<string>();

            foreach (var block in module.Blocks)
            {
                foreach (var instr in block.Instructions)
                {
                    switch (instr)
                    {
                        case IrPush push:
                            referenced.Add(push.VarName);
                            break;
                        case IrCopy copy:
                            referenced.Add(copy.Source);
                            referenced.Add(copy.Dest);
                            break;
                        case IrJumpIfFalse jif:
                            referenced.Add(jif.ConditionVar);
                            break;
                        case IrJumpIndirect ji:
                            referenced.Add(ji.AddressVar);
                            break;
                    }
                }
            }

            // Remove heap vars that are unreferenced and safe to remove
            module.HeapVars.RemoveAll(v =>
                !referenced.Contains(v.Name) && IsSafeToRemove(v));
        }

        private static bool IsSafeToRemove(IrHeapVar v)
        {
            // Never remove exported, synced, or this-reference vars
            if (v.IsExport) return false;
            if (v.SyncMode != SyncMode.NotSynced) return false;
            if (v.IsThis) return false;

            // Only remove compiler-generated temporaries and constants
            return v.Name.StartsWith("__tmp_") || v.Name.StartsWith("__const_");
        }
    }
}
