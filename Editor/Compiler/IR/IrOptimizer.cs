using System.Collections.Generic;

namespace Nori.Compiler
{
    public class IrOptimizer
    {
        private readonly List<IIrPass> _passes = new List<IIrPass>();

        public static IrOptimizer CreateDefault()
        {
            var optimizer = new IrOptimizer();
            optimizer.AddPass(new CopyPropagationPass());
            optimizer.AddPass(new DeadVarEliminationPass());
            return optimizer;
        }

        public void AddPass(IIrPass pass)
        {
            _passes.Add(pass);
        }

        public void Optimize(IrModule module)
        {
            foreach (var pass in _passes)
                pass.Run(module);
        }
    }
}
