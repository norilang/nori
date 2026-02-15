using System.Collections.Generic;
using System.Text;

namespace Nori.Compiler
{
    public class UdonEmitter
    {
        private readonly IrModule _ir;
        private readonly Dictionary<string, uint> _labelAddresses = new Dictionary<string, uint>();
        private readonly Dictionary<string, string> _labelConstants = new Dictionary<string, string>();

        public UdonEmitter(IrModule ir)
        {
            _ir = ir;
        }

        public string Emit()
        {
            // Pass 1: compute label addresses and resolve return-address constants
            ComputeAddresses();

            // Pass 2: emit text
            return EmitText();
        }

        private void ComputeAddresses()
        {
            uint offset = 0;
            foreach (var block in _ir.Blocks)
            {
                _labelAddresses[block.Label] = offset;

                foreach (var instr in block.Instructions)
                {
                    offset += GetInstructionSize(instr);
                }
            }

            // Resolve label constants for return addresses
            foreach (var heapVar in _ir.HeapVars)
            {
                if (heapVar.InitialValue != null && heapVar.InitialValue.StartsWith("__label__"))
                {
                    string label = heapVar.InitialValue.Substring(9);
                    if (_labelAddresses.TryGetValue(label, out var addr))
                    {
                        heapVar.InitialValue = addr.ToString();
                    }
                }
            }
        }

        private uint GetInstructionSize(IrInstruction instr)
        {
            // Sizes reflect how many Udon instructions each IR instruction emits:
            // PUSH=8, POP=4, EXTERN=8, JUMP=8, JUMP_IF_FALSE=8, COPY=4, NOP=4
            switch (instr)
            {
                case IrPush _: return 8;           // PUSH
                case IrPop _: return 4;             // POP
                case IrExtern _: return 8;          // EXTERN
                case IrJump _: return 8;            // JUMP
                case IrJumpIfFalse _: return 16;    // PUSH + JUMP_IF_FALSE
                case IrJumpIndirect _: return 16;   // PUSH + JUMP_INDIRECT
                case IrCopy _: return 20;           // PUSH + PUSH + COPY
                case IrComment _: return 0;
                default: return 4;
            }
        }

        private string EmitText()
        {
            var sb = new StringBuilder();

            // Data section
            sb.AppendLine(".data_start");
            sb.AppendLine();

            // Emit exports first
            foreach (var v in _ir.HeapVars)
            {
                if (v.IsExport)
                    sb.AppendLine($"    .export {v.Name}");
            }

            // Emit syncs
            foreach (var v in _ir.HeapVars)
            {
                if (v.SyncMode != SyncMode.NotSynced)
                {
                    string mode = v.SyncMode.ToString().ToLowerInvariant();
                    if (mode == "none")
                        sb.AppendLine($"    .sync {v.Name}, none");
                    else
                        sb.AppendLine($"    .sync {v.Name}, {mode}");
                }
            }

            sb.AppendLine();

            // Emit all variable declarations
            foreach (var v in _ir.HeapVars)
            {
                string init = FormatInitialValue(v);
                if (v.IsThis)
                    sb.AppendLine($"    {v.Name}: %{v.UdonType}, this");
                else
                    sb.AppendLine($"    {v.Name}: %{v.UdonType}, {init}");
            }

            sb.AppendLine();
            sb.AppendLine(".data_end");
            sb.AppendLine();

            // Code section
            sb.AppendLine(".code_start");
            sb.AppendLine();

            // Emit code exports
            foreach (var block in _ir.Blocks)
            {
                if (block.IsExport)
                    sb.AppendLine($"    .export {block.Label}");
            }

            sb.AppendLine();

            // Emit blocks
            foreach (var block in _ir.Blocks)
            {
                sb.AppendLine($"    {block.Label}:");

                foreach (var instr in block.Instructions)
                {
                    EmitInstruction(sb, instr);
                }

                sb.AppendLine();
            }

            sb.AppendLine(".code_end");

            return sb.ToString();
        }

        private void EmitInstruction(StringBuilder sb, IrInstruction instr)
        {
            switch (instr)
            {
                case IrPush push:
                    sb.AppendLine($"        PUSH, {push.VarName}");
                    break;

                case IrPop _:
                    sb.AppendLine("        POP");
                    break;

                case IrExtern ext:
                    sb.AppendLine($"        EXTERN, \"{ext.Signature}\"");
                    break;

                case IrJump jump:
                    if (jump.AbsoluteAddress.HasValue)
                    {
                        sb.AppendLine($"        JUMP, 0x{jump.AbsoluteAddress.Value:X8}");
                    }
                    else if (jump.TargetLabel != null && _labelAddresses.TryGetValue(jump.TargetLabel, out var addr))
                    {
                        sb.AppendLine($"        JUMP, 0x{addr:X8}");
                    }
                    else
                    {
                        sb.AppendLine($"        JUMP, {jump.TargetLabel}");
                    }
                    break;

                case IrJumpIfFalse jif:
                    sb.Append($"        PUSH, {jif.ConditionVar}");
                    sb.AppendLine();
                    if (_labelAddresses.TryGetValue(jif.TargetLabel, out var jifAddr))
                    {
                        sb.AppendLine($"        JUMP_IF_FALSE, 0x{jifAddr:X8}");
                    }
                    else
                    {
                        sb.AppendLine($"        JUMP_IF_FALSE, {jif.TargetLabel}");
                    }
                    break;

                case IrJumpIndirect ji:
                    sb.AppendLine($"        PUSH, {ji.AddressVar}");
                    sb.AppendLine($"        JUMP_INDIRECT, {ji.AddressVar}");
                    break;

                case IrCopy copy:
                    sb.AppendLine($"        PUSH, {copy.Source}");
                    sb.AppendLine($"        PUSH, {copy.Dest}");
                    sb.AppendLine("        COPY");
                    break;

                case IrComment comment:
                    sb.AppendLine($"        # {comment.Text}");
                    break;
            }
        }

        private string FormatInitialValue(IrHeapVar v)
        {
            if (v.InitialValue == null)
                return "null";

            return v.InitialValue;
        }
    }
}
