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

        /// <summary>
        /// Check if a 1-based line/column position falls within this span.
        /// </summary>
        public bool Contains(int line, int column)
        {
            if (Start.Line == 0 && End.Line == 0) return false; // SourceSpan.None
            if (line < Start.Line || line > End.Line) return false;
            if (line == Start.Line && column < Start.Column) return false;
            if (line == End.Line && column > End.Column) return false;
            return true;
        }

        public override string ToString() => $"{File}{Start}..{End}";
    }
}
