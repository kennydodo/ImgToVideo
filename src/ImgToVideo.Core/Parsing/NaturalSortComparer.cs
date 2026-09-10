namespace ImgToVideo.Core.Parsing;

public sealed class NaturalSortComparer : IComparer<string>
{
    public static readonly NaturalSortComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
            {
                var nx = ReadNumber(x, ref ix);
                var ny = ReadNumber(y, ref iy);
                var cmp = nx.CompareTo(ny);
                if (cmp != 0)
                {
                    return cmp;
                }
            }
            else
            {
                var cmp = char.ToUpperInvariant(x[ix]).CompareTo(char.ToUpperInvariant(y[iy]));
                if (cmp != 0)
                {
                    return cmp;
                }

                ix++;
                iy++;
            }
        }

        return (x.Length - ix).CompareTo(y.Length - iy);
    }

    private static long ReadNumber(string s, ref int i)
    {
        long value = 0;
        while (i < s.Length && char.IsDigit(s[i]))
        {
            value = value * 10 + (s[i] - '0');
            i++;
        }

        return value;
    }
}
