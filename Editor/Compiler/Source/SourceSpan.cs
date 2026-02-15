namespace Nori.Compiler
{
    public readonly struct SourcePos
    {
        public readonly int Line;   // 1-indexed
        public readonly int Column; // 1-indexed

        public SourcePos(int line, int column)
        {
            Line = line;
            Column = column;
        }

        public override string ToString() => $"({Line},{Column})";
    }

    public readonly struct SourceSpan
    {
        public readonly string File;
        public readonly SourcePos Start;
        public readonly SourcePos End;

        public static readonly SourceSpan None = new SourceSpan(null, new SourcePos(0, 0), new SourcePos(0, 0));

        public SourceSpan(string file, SourcePos start, SourcePos end)
        {
            File = file;
            Start = start;
            End = end;
        }

        public SourceSpan Merge(SourceSpan other)
        {
            var start = Start.Line < other.Start.Line ||
                        (Start.Line == other.Start.Line && Start.Column <= other.Start.Column)
                ? Start : other.Start;
            var end = End.Line > other.End.Line ||
                      (End.Line == other.End.Line && End.Column >= other.End.Column)
                ? End : other.End;
            return new SourceSpan(File ?? other.File, start, end);
        }

        public override string ToString() => $"{File}{Start}..{End}";
    }
}
