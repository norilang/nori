using System.Collections.Generic;

namespace Nori.Compiler
{
    public class IrModule
    {
        public List<IrHeapVar> HeapVars { get; } = new List<IrHeapVar>();
        public List<IrBlock> Blocks { get; } = new List<IrBlock>();

        public IrHeapVar FindVar(string name)
        {
            foreach (var v in HeapVars)
                if (v.Name == name) return v;
            return null;
        }

        public IrBlock FindBlock(string label)
        {
            foreach (var b in Blocks)
                if (b.Label == label) return b;
            return null;
        }
    }

    public class IrHeapVar
    {
        public string Name { get; set; }
        public string UdonType { get; set; }
        public string InitialValue { get; set; }
        public bool IsExport { get; set; }
        public SyncMode SyncMode { get; set; }
        public bool IsThis { get; set; }

        public IrHeapVar(string name, string udonType, string initialValue = null)
        {
            Name = name;
            UdonType = udonType;
            InitialValue = initialValue;
        }
    }

    public class IrBlock
    {
        public string Label { get; set; }
        public bool IsExport { get; set; }
        public List<IrInstruction> Instructions { get; } = new List<IrInstruction>();

        public IrBlock(string label, bool isExport = false)
        {
            Label = label;
            IsExport = isExport;
        }
    }

    // --- Instructions ---

    public abstract class IrInstruction { }

    public class IrPush : IrInstruction
    {
        public string VarName { get; }
        public IrPush(string varName) { VarName = varName; }
        public override string ToString() => $"PUSH, {VarName}";
    }

    public class IrPop : IrInstruction
    {
        public override string ToString() => "POP";
    }

    public class IrExtern : IrInstruction
    {
        public string Signature { get; }
        public IrExtern(string signature) { Signature = signature; }
        public override string ToString() => $"EXTERN, \"{Signature}\"";
    }

    public class IrJump : IrInstruction
    {
        public string TargetLabel { get; set; }
        public uint? AbsoluteAddress { get; set; } // for halt sentinel

        public IrJump(string targetLabel) { TargetLabel = targetLabel; }

        public static IrJump Halt() => new IrJump(null) { AbsoluteAddress = 0xFFFFFFFC };

        public override string ToString() =>
            AbsoluteAddress.HasValue ? $"JUMP, 0x{AbsoluteAddress:X8}"
                : $"JUMP, {TargetLabel}";
    }

    public class IrJumpIfFalse : IrInstruction
    {
        public string ConditionVar { get; }
        public string TargetLabel { get; set; }

        public IrJumpIfFalse(string conditionVar, string targetLabel)
        {
            ConditionVar = conditionVar;
            TargetLabel = targetLabel;
        }

        public override string ToString() => $"JUMP_IF_FALSE, {ConditionVar}, {TargetLabel}";
    }

    public class IrJumpIndirect : IrInstruction
    {
        public string AddressVar { get; }

        public IrJumpIndirect(string addressVar)
        {
            AddressVar = addressVar;
        }

        public override string ToString() => $"JUMP_INDIRECT, {AddressVar}";
    }

    public class IrCopy : IrInstruction
    {
        public string Source { get; }
        public string Dest { get; }

        public IrCopy(string source, string dest)
        {
            Source = source;
            Dest = dest;
        }

        public override string ToString() => $"COPY, {Source}, {Dest}";
    }

    public class IrComment : IrInstruction
    {
        public string Text { get; }
        public IrComment(string text) { Text = text; }
        public override string ToString() => $"# {Text}";
    }
}
