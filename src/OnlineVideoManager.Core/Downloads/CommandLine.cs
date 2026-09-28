using System.Text;

namespace OnlineVideoManager.Core.Downloads;

public static class CommandLine
{
    /// <summary>Split a command-line style string into arguments, honouring double quotes.
    /// <c>--foo "a b" c ""</c> → <c>--foo</c>, <c>a b</c>, <c>c</c>, empty string.</summary>
    public static List<string> Split(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (hasToken)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
