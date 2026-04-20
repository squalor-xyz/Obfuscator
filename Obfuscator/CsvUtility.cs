using System.Text;

namespace Squalor.Obfuscator;

internal static class CsvUtility
{
    public static (List<string> Headers, List<Dictionary<string, string>> Rows) ReadCsv(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8);
        var allLines = new List<string>();

        while (!reader.EndOfStream)
        {
            allLines.Add(reader.ReadLine() ?? string.Empty);
        }

        if (allLines.Count == 0)
            throw new InvalidOperationException("CSV file is empty.");

        var headers = ParseCsvLine(allLines[0]);
        var rows = new List<Dictionary<string, string>>();

        for (var i = 1; i < allLines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(allLines[i]))
                continue;

            var values = ParseCsvLine(allLines[i]);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var c = 0; c < headers.Count; c++)
            {
                row[headers[c]] = c < values.Count ? values[c] : string.Empty;
            }

            rows.Add(row);
        }

        return (headers, rows);
    }

    public static void WriteCsv(
        string path,
        IReadOnlyList<string> headers,
        IReadOnlyList<Dictionary<string, string>> rows)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine(string.Join(",", headers.Select(EscapeCsv)));

        foreach (var row in rows)
        {
            var values = headers.Select(h => row.TryGetValue(h, out var v) ? v : string.Empty);
            writer.WriteLine(string.Join(",", values.Select(EscapeCsv)));
        }
    }

    private static string EscapeCsv(string? value)
    {
        value ??= string.Empty;

        var mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!mustQuote)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }
            else
            {
                if (ch == ',')
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else if (ch == '"')
                {
                    inQuotes = true;
                }
                else
                {
                    sb.Append(ch);
                }
            }
        }

        result.Add(sb.ToString());
        return result;
    }
}
