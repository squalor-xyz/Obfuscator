using System.Text;
using System.Text.Json;

namespace Squalor.Obfuscator;

public sealed class Obfuscator
{
    public DataGenConfig GetDataGenParametersFromConfig(string jsonFilePath)
    {
        if (string.IsNullOrWhiteSpace(jsonFilePath))
            throw new ArgumentException("JSON config path is required.", nameof(jsonFilePath));

        var json = File.ReadAllText(jsonFilePath, Encoding.UTF8);
        var config = JsonSerializer.Deserialize<DataGenConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Failed to deserialize data generation config.");

        ValidateConfig(config);
        return config;
    }

    public void GenerateCsvFromConfig(string jsonFilePath, string outputCsvPath)
    {
        var config = GetDataGenParametersFromConfig(jsonFilePath);
        GenerateCsv(config, outputCsvPath);
    }

    public void GenerateCsv(DataGenConfig config, string outputCsvPath)
    {
        ValidateConfig(config);

        var generator = new DataGenerator(config.Seed);

        using var writer = new StreamWriter(outputCsvPath, false, new UTF8Encoding(false));
        using var csv = new CsvHelper.CsvWriter(writer, System.Globalization.CultureInfo.InvariantCulture);

        foreach (var col in config.Columns)
            csv.WriteField(col.Name);
        csv.NextRecord();

        foreach (var row in generator.GenerateRows(config))
        {
            foreach (var col in config.Columns)
                csv.WriteField(row.TryGetValue(col.Name, out var v) ? v : string.Empty);

            csv.NextRecord();
        }
    }

    public ObfuscationManifest ObfuscateCsv(
        string inputCsvPath,
        string outputCsvPath,
        string obfPath,
        ObfuscationOptions? options = null)
    {
        if (!File.Exists(inputCsvPath))
            throw new FileNotFoundException("Input CSV file not found.", inputCsvPath);

        options ??= new ObfuscationOptions();

        var engine = new ObfuscationEngine(options.Seed, options.DeterministicKey);
        return engine.Obfuscate(inputCsvPath, outputCsvPath, obfPath, options);
    }

    public void DeobfuscateCsv(
        string obfuscatedCsvPath,
        string obfPath,
        string outputCsvPath,
        string? passphrase = null,
        string? deterministicKey = null)
    {
        if (!File.Exists(obfuscatedCsvPath))
            throw new FileNotFoundException("Obfuscated CSV file not found.", obfuscatedCsvPath);

        if (!File.Exists(obfPath))
            throw new FileNotFoundException("Obfuscation manifest file not found.", obfPath);

        var engine = new ObfuscationEngine(deterministicKey: deterministicKey);
        engine.Deobfuscate(obfuscatedCsvPath, obfPath, outputCsvPath, passphrase);
    }

    private static void ValidateConfig(DataGenConfig config)
    {
        var rowMode = config.RowMode?.Trim().ToLowerInvariant() ?? "fixed";
        if (rowMode is not ("fixed" or "sweep" or "max"))
            throw new InvalidOperationException($"Unsupported rowMode '{config.RowMode}'. Expected one of: fixed, sweep, max.");

        if (rowMode is "fixed" or "max" && config.NRows <= 0)
            throw new InvalidOperationException($"NRows must be > 0 when rowMode is '{rowMode}'.");

        if (rowMode is "sweep" or "max" && config.SweepAxes.Count == 0)
            throw new InvalidOperationException($"rowMode '{rowMode}' requires at least one sweep axis.");

        if (rowMode == "fixed" && config.NRows <= 0)
            throw new InvalidOperationException("NRows must be > 0 when rowMode is 'fixed'.");

        if (rowMode == "fixed" && config.SweepAxes.Count > 0)
            throw new InvalidOperationException("rowMode 'fixed' cannot be used with sweepAxes. Use 'sweep' or 'max' instead.");

        if (config.Columns.Count == 0)
            throw new InvalidOperationException("At least one column must be defined.");

        var dupes = config.Columns
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (dupes.Count > 0)
            throw new InvalidOperationException("Duplicate column names: " + string.Join(", ", dupes));

        foreach (var col in config.Columns)
        {
            if (string.IsNullOrWhiteSpace(col.Name))
                throw new InvalidOperationException("Column name cannot be empty.");

            if (string.IsNullOrWhiteSpace(col.DataType))
                throw new InvalidOperationException($"Column '{col.Name}' is missing DataType.");

            if (col.IsLinear && col.IsLogarithmic)
                throw new InvalidOperationException($"Column '{col.Name}' cannot be both linear and logarithmic.");

            if (col.IsNumeric && (!col.TotalRangeMin.HasValue || !col.TotalRangeMax.HasValue))
                throw new InvalidOperationException($"Numeric column '{col.Name}' must have TotalRangeMin and TotalRangeMax.");

            if (!string.IsNullOrWhiteSpace(col.GeneratedIdMode))
            {
                if (!col.IsInteger)
                    throw new InvalidOperationException($"Sweep-generated ID column '{col.Name}' must use a numeric integer type.");

                if (!col.GeneratedIdMode.Equals("outer-group", StringComparison.OrdinalIgnoreCase) &&
                    !col.GeneratedIdMode.Equals("inner-step", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Column '{col.Name}' has unsupported generatedIdMode '{col.GeneratedIdMode}'.");
                }
            }
        }

        if (config.SweepAxes.Count == 0)
            return;

        var byName = config.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var axis in config.SweepAxes)
        {
            if (string.IsNullOrWhiteSpace(axis.Name))
                throw new InvalidOperationException("Sweep axis name cannot be empty.");

            if (!byName.TryGetValue(axis.Name, out var column))
                throw new InvalidOperationException($"Sweep axis '{axis.Name}' does not match any configured column.");

            if (column.Values.Count == 0)
                throw new InvalidOperationException($"Sweep axis '{axis.Name}' must define at least one value.");
        }
    }
}
