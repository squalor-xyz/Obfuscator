// SPDX-License-Identifier: MPL-2.0
/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/
using System.Globalization;
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

    public void GenerateCsvFromConfig(string jsonFilePath, string outputCsvPath, bool force = false)
    {
        var config = GetDataGenParametersFromConfig(jsonFilePath);
        GenerateCsv(config, outputCsvPath, force);
    }

    public void GenerateCsv(DataGenConfig config, string outputCsvPath, bool force = false)
    {
        ValidateConfig(config);

        var generator = new DataGenerator(config.Seed);

        FileWrite.RefuseOverwriteUnlessForce(outputCsvPath, force);
        FileWrite.WriteAtomically(outputCsvPath, tmp =>
        {
            using var writer = new StreamWriter(tmp, false, new UTF8Encoding(false));
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
        });
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
        string? deterministicKey = null,
        bool allowMismatchedSource = false,
        bool force = false)
    {
        if (!File.Exists(obfuscatedCsvPath))
            throw new FileNotFoundException("Obfuscated CSV file not found.", obfuscatedCsvPath);

        if (!File.Exists(obfPath))
            throw new FileNotFoundException("Obfuscation manifest file not found.", obfPath);

        var engine = new ObfuscationEngine(deterministicKey: deterministicKey);
        engine.Deobfuscate(obfuscatedCsvPath, obfPath, outputCsvPath, passphrase, allowMismatchedSource, force);
    }

    private const double PercentageMax = 100.0;

    // Rounded integer values must land in [min, maxExclusive) so the cast in
    // DataGenerator.FormatNumericByType cannot overflow.
    private static readonly Dictionary<string, (double Min, double MaxExclusive)> IntegerTypeBounds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["byte"] = (byte.MinValue, byte.MaxValue + 1.0),
            ["short"] = (short.MinValue, short.MaxValue + 1.0),
            ["int"] = (int.MinValue, int.MaxValue + 1.0),
            ["long"] = (long.MinValue, -(double)long.MinValue),
            ["bigint"] = (long.MinValue, -(double)long.MinValue)
        };

    private static void ValidateConfig(DataGenConfig config)
    {
        var rowMode = config.RowMode?.Trim().ToLowerInvariant() ?? "fixed";
        if (rowMode is not ("fixed" or "sweep" or "max"))
            throw new InvalidOperationException($"Unsupported rowMode '{config.RowMode}'. Expected one of: fixed, sweep, max.");

        if (rowMode is "fixed" or "max" && config.NRows <= 0)
            throw new InvalidOperationException($"NRows must be > 0 when rowMode is '{rowMode}'.");

        // Rows are indexed with int and held in a List, so Array.MaxLength is the
        // largest row count the generator can represent.
        if (rowMode is "fixed" or "max" && config.NRows > Array.MaxLength)
            throw new InvalidOperationException($"NRows {config.NRows} exceeds the limit of {Array.MaxLength} rows.");

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

        var columnIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < config.Columns.Count; i++)
            columnIndexByName[config.Columns[i].Name] = i;

        for (var i = 0; i < config.Columns.Count; i++)
        {
            var col = config.Columns[i];
            if (string.IsNullOrWhiteSpace(col.Name))
                throw new InvalidOperationException("Column name cannot be empty.");

            if (string.IsNullOrWhiteSpace(col.DataType))
                throw new InvalidOperationException($"Column '{col.Name}' is missing DataType.");

            if (col.IsLinear && col.IsLogarithmic)
                throw new InvalidOperationException($"Column '{col.Name}' cannot be both linear and logarithmic.");

            if (col.IsNumeric && (!col.TotalRangeMin.HasValue || !col.TotalRangeMax.HasValue))
                throw new InvalidOperationException($"Numeric column '{col.Name}' must have TotalRangeMin and TotalRangeMax.");

            ValidateRanges(col);
            ValidatePercentage(col, col.TruePercentage, nameof(col.TruePercentage));
            ValidatePercentage(col, col.PercentageInIdealRange, nameof(col.PercentageInIdealRange));

            if (col.RandomStringLength < 0)
                throw new InvalidOperationException($"Column '{col.Name}' RandomStringLength cannot be negative.");

            if (col.IsDateTime)
                ValidateDates(col);

            ValidateTrackedColumns(config, i, col.TracksWith, nameof(col.TracksWith), columnIndexByName);
            ValidateTrackedColumns(config, i, col.TracksInverselyWith, nameof(col.TracksInverselyWith), columnIndexByName);

            if (!string.IsNullOrWhiteSpace(col.GeneratedIdMode))
            {
                if (!col.IsInteger)
                    throw new InvalidOperationException($"Sweep-generated ID column '{col.Name}' must use a numeric integer type.");

                if (!col.GeneratedIdMode.Equals("outer-group", StringComparison.OrdinalIgnoreCase) &&
                    !col.GeneratedIdMode.Equals("inner-step", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Column '{col.Name}' has unsupported generatedIdMode '{col.GeneratedIdMode}'.");
                }

                if (rowMode == "fixed")
                    throw new InvalidOperationException($"generatedIdMode column '{col.Name}' requires rowMode 'sweep' or 'max'.");
            }
        }

        if (config.SweepAxes.Count == 0)
            return;

        var byName = config.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var seenAxes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Checked long arithmetic so a huge product fails here instead of wrapping.
        long sweepRowCount = 1;
        var sweepRowCountOverflows = false;
        foreach (var axis in config.SweepAxes)
        {
            if (string.IsNullOrWhiteSpace(axis.Name))
                throw new InvalidOperationException("Sweep axis name cannot be empty.");

            if (!byName.TryGetValue(axis.Name, out var column))
                throw new InvalidOperationException($"Sweep axis '{axis.Name}' does not match any configured column.");

            if (!seenAxes.Add(axis.Name))
                throw new InvalidOperationException($"Duplicate sweep axis '{axis.Name}'.");

            if (column.Values.Count == 0)
                throw new InvalidOperationException($"Sweep axis '{axis.Name}' must define at least one value.");

            if (!string.IsNullOrWhiteSpace(column.GeneratedIdMode))
                throw new InvalidOperationException($"Column '{column.Name}' cannot be both a sweep axis and a generatedIdMode column.");

            ValidateSweepValues(column);

            if (!sweepRowCountOverflows)
            {
                try
                {
                    sweepRowCount = checked(sweepRowCount * column.Values.Count);
                }
                catch (OverflowException)
                {
                    sweepRowCountOverflows = true;
                }
            }
        }

        if (sweepRowCountOverflows)
            throw new InvalidOperationException($"Sweep expansion overflows: the product of sweep axis value counts exceeds the limit of {Array.MaxLength} rows.");

        if (sweepRowCount > Array.MaxLength)
            throw new InvalidOperationException($"Sweep expansion of {sweepRowCount} rows exceeds the limit of {Array.MaxLength} rows.");

        ValidateGeneratedIdRanges(config, byName);
    }

    // outer-group needs one ID per combination of the outer axes; inner-step needs one per innermost value.
    // Called after the sweep expansion check, so the group count fits in a long.
    private static void ValidateGeneratedIdRanges(
        DataGenConfig config,
        IReadOnlyDictionary<string, ColumnGenerationSpec> byName)
    {
        long outerGroupCount = 1;
        foreach (var axis in config.SweepAxes.Take(config.SweepAxes.Count - 1))
            outerGroupCount *= byName[axis.Name].Values.Count;
        long innerStepCount = byName[config.SweepAxes[^1].Name].Values.Count;

        foreach (var col in config.Columns.Where(c => !string.IsNullOrWhiteSpace(c.GeneratedIdMode)))
        {
            var needed = col.GeneratedIdMode!.Equals("outer-group", StringComparison.OrdinalIgnoreCase)
                ? outerGroupCount
                : innerStepCount;
            var min = col.TotalRangeMin!.Value;
            var max = col.TotalRangeMax!.Value;
            if (min + needed - 1 > max)
            {
                throw new InvalidOperationException(
                    $"generatedIdMode column '{col.Name}' needs {needed} IDs but its range " +
                    $"{min.ToString(CultureInfo.InvariantCulture)}..{max.ToString(CultureInfo.InvariantCulture)} " +
                    $"holds {(max - min + 1).ToString(CultureInfo.InvariantCulture)}.");
            }
        }
    }

    private static void ValidateRanges(ColumnGenerationSpec col)
    {
        if (col.TotalRangeMin.HasValue && !double.IsFinite(col.TotalRangeMin.Value) ||
            col.TotalRangeMax.HasValue && !double.IsFinite(col.TotalRangeMax.Value) ||
            col.IdealRangeMin.HasValue && !double.IsFinite(col.IdealRangeMin.Value) ||
            col.IdealRangeMax.HasValue && !double.IsFinite(col.IdealRangeMax.Value))
        {
            throw new InvalidOperationException($"Column '{col.Name}' range values must be finite.");
        }

        if (col.TotalRangeMin > col.TotalRangeMax)
            throw new InvalidOperationException($"Column '{col.Name}' has TotalRangeMin greater than TotalRangeMax.");

        if (col.IdealRangeMin.HasValue != col.IdealRangeMax.HasValue)
            throw new InvalidOperationException($"Column '{col.Name}' must set both IdealRangeMin and IdealRangeMax, or neither.");

        if (col.IdealRangeMin > col.IdealRangeMax)
            throw new InvalidOperationException($"Column '{col.Name}' has IdealRangeMin greater than IdealRangeMax.");

        if (col.IsNumeric && col.IdealRangeMin.HasValue &&
            (col.IdealRangeMin < col.TotalRangeMin || col.IdealRangeMax > col.TotalRangeMax))
        {
            throw new InvalidOperationException($"Column '{col.Name}' ideal range must lie within its total range.");
        }

        if (col.IsNumeric && IntegerTypeBounds.TryGetValue(col.DataType.Trim(), out var bounds))
        {
            foreach (var value in new[] { col.TotalRangeMin!.Value, col.TotalRangeMax!.Value })
            {
                var rounded = Math.Round(value);
                if (rounded < bounds.Min || rounded >= bounds.MaxExclusive)
                    throw new InvalidOperationException($"Column '{col.Name}' range does not fit data type '{col.DataType}'.");
            }
        }
    }

    private static void ValidatePercentage(ColumnGenerationSpec col, double? value, string fieldName)
    {
        if (value.HasValue && (!double.IsFinite(value.Value) || value.Value < 0.0 || value.Value > PercentageMax))
            throw new InvalidOperationException($"Column '{col.Name}' {fieldName} must be between 0 and 100.");
    }

    private static void ValidateDates(ColumnGenerationSpec col)
    {
        var min = ParseConfigDate(col, col.DateMinUtc, nameof(col.DateMinUtc)) ?? DataGenerator.DefaultDateMinUtc;
        var max = ParseConfigDate(col, col.DateMaxUtc, nameof(col.DateMaxUtc)) ?? DataGenerator.DefaultDateMaxUtc;
        if (min > max)
            throw new InvalidOperationException($"Column '{col.Name}' DateMinUtc is after DateMaxUtc.");
    }

    private static DateTimeOffset? ParseConfigDate(ColumnGenerationSpec col, string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!DataGenerator.TryParseDate(value, out var parsed))
            throw new InvalidOperationException($"Column '{col.Name}' {fieldName} '{value}' is not a valid date.");

        return parsed;
    }

    private static void ValidateTrackedColumns(
        DataGenConfig config,
        int columnIndex,
        IReadOnlyList<string> names,
        string fieldName,
        IReadOnlyDictionary<string, int> columnIndexByName)
    {
        var col = config.Columns[columnIndex];
        foreach (var name in names)
        {
            if (!columnIndexByName.TryGetValue(name, out var referencedIndex))
                throw new InvalidOperationException($"Column '{col.Name}' {fieldName} references unknown column '{name}'.");

            if (referencedIndex == columnIndex)
                throw new InvalidOperationException($"Column '{col.Name}' cannot track itself.");

            if (!config.Columns[referencedIndex].IsNumeric)
                throw new InvalidOperationException($"Column '{col.Name}' {fieldName} references non-numeric column '{name}'.");

            // Generation fills columns in order, so a later column has no value yet to track.
            if (referencedIndex > columnIndex)
                throw new InvalidOperationException($"Column '{col.Name}' {fieldName} references column '{name}', which must appear before it.");
        }
    }

    private static void ValidateSweepValues(ColumnGenerationSpec column)
    {
        if (!column.IsNumeric)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in column.Values)
            {
                if (!seen.Add(value))
                    throw new InvalidOperationException($"Sweep axis '{column.Name}' has duplicate value '{value}'.");
            }

            return;
        }

        var seenNumbers = new HashSet<double>();
        foreach (var value in column.Values)
        {
            if (!double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number) ||
                !double.IsFinite(number))
            {
                throw new InvalidOperationException($"Sweep axis '{column.Name}' value '{value}' is not a valid number.");
            }

            if (number < column.TotalRangeMin || number > column.TotalRangeMax)
                throw new InvalidOperationException($"Sweep axis '{column.Name}' value '{value}' is outside its total range.");

            if (!seenNumbers.Add(number))
                throw new InvalidOperationException($"Sweep axis '{column.Name}' has duplicate value '{value}'.");
        }
    }
}
