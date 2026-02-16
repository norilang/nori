namespace Nori.Compiler
{
    public interface IIrPass
    {
        string Name { get; }
        void Run(IrModule module);
    }
}
