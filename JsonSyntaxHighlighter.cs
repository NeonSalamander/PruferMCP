using System.Text.RegularExpressions;

namespace PruferMCP;

/// <summary>
/// Splits pretty-printed JSON into colored tokens for syntax highlighting.
/// </summary>
internal static class JsonSyntaxHighlighter
{
    private static readonly Regex TokenRegex = new Regex(
        """"(?:[^"\\]|\\.)*"""" +          // string
        """|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?""" + // number
        """|\b(?:true|false|null)\b""" +    // literal
        """|[{}\[\],:]""" +                // punctuation
        """|[^\S\r\n]+""",                  // whitespace
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<JsonSegment> Highlight(string json)
    {
        var segments = new List<JsonSegment>();
        int position = 0;

        foreach (Match match in TokenRegex.Matches(json))
        {
            if (match.Index > position)
            {
                segments.Add(new JsonSegment
                {
                    Text = json.Substring(position, match.Index - position),
                    Category = "text",
                });
            }

            var token = match.Value;
            segments.Add(new JsonSegment
            {
                Text = token,
                Category = Classify(token),
            });

            position = match.Index + match.Length;
        }

        if (position < json.Length)
        {
            segments.Add(new JsonSegment
            {
                Text = json.Substring(position),
                Category = "text",
            });
        }

        return segments;
    }

    private static string Classify(string token)
    {
        if (string.IsNullOrEmpty(token))
            return "text";

        var c = token[0];

        if (c == '"')
            return "string";

        if (char.IsDigit(c) || c == '-' || c == '+' || c == '.')
            return "number";

        if (c == '{' || c == '}' || c == '[' || c == ']' || c == ',' || c == ':')
            return "punctuation";

        if (token == "true" || token == "false" || token == "null")
            return "keyword";

        if (char.IsWhiteSpace(c))
            return "text";

        return "text";
    }
}
