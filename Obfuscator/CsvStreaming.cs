// SPDX-License-Identifier: MPL-2.0
/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace Squalor.Obfuscator;

internal static class CsvStreaming
{
    public static string[] ReadHeaders(string path)
    {
        using var reader = new StreamReader(path);
        using var csv = CreateReader(reader);
        csv.Read();
        csv.ReadHeader();
        return csv.HeaderRecord ?? Array.Empty<string>();
    }

    public static string DetectDelimiter(string path)
    {
        using var reader = new StreamReader(path);
        using var csv = CreateReader(reader);
        csv.Read();
        csv.ReadHeader();
        return csv.Parser.Delimiter;
    }

    public static void TransformCsv(
        string inputPath,
        string outputPath,
        Func<string[], IDictionary<string, string>, IDictionary<string, string>> transformRow,
        bool force = false,
        string? outputDelimiter = null)
    {
        if (FileWrite.SamePath(inputPath, outputPath))
            throw new InvalidOperationException("Output path is the input path. Pass -o explicitly.");

        FileWrite.RefuseOverwriteUnlessForce(outputPath, force);
        FileWrite.WriteAtomically(outputPath, tmp =>
        {
            using var reader = new StreamReader(inputPath);
            using var writer = new StreamWriter(tmp, false, new System.Text.UTF8Encoding(false));

            using var csvReader = CreateReader(reader);
            csvReader.Read();
            csvReader.ReadHeader();
            var headers = csvReader.HeaderRecord ?? Array.Empty<string>();
            var delimiter = string.IsNullOrEmpty(outputDelimiter) ? csvReader.Parser.Delimiter : outputDelimiter;
            using var csvWriter = CreateWriter(writer, delimiter);

            foreach (var h in headers)
                csvWriter.WriteField(h);
            csvWriter.NextRecord();

            while (csvReader.Read())
            {
                var inputRow = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var h in headers)
                    inputRow[h] = csvReader.GetField(h) ?? string.Empty;

                var outputRow = transformRow(headers, inputRow);

                foreach (var h in headers)
                {
                    outputRow.TryGetValue(h, out var value);
                    csvWriter.WriteField(value ?? string.Empty);
                }

                csvWriter.NextRecord();
            }
        });
    }

    public static IEnumerable<IDictionary<string, string>> ReadRows(string path)
    {
        using var reader = new StreamReader(path);
        using var csv = CreateReader(reader);

        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? Array.Empty<string>();

        while (csv.Read())
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in headers)
                row[h] = csv.GetField(h) ?? string.Empty;

            yield return row;
        }
    }

    private static CsvReader CreateReader(TextReader reader)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            DetectDelimiter = true,
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null,
            IgnoreBlankLines = false
        };

        return new CsvReader(reader, config);
    }

    private static CsvWriter CreateWriter(TextWriter writer, string delimiter)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = string.IsNullOrEmpty(delimiter) ? "," : delimiter
        };
        return new CsvWriter(writer, config);
    }
}
