// SPDX-License-Identifier: MPL-2.0
/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/
using System.Globalization;
using System.Text;

namespace Squalor.Obfuscator;

internal sealed class DataGenerator
{
    private const double BooleanTruePercentageDefault = 50.0;
    private const double PercentageScale = 100.0;
    private static readonly DateTimeOffset DefaultDateMinUtc = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DefaultDateMaxUtc = new(2020, 1, 31, 0, 0, 0, TimeSpan.Zero);
    private const double CorrelatedNoiseStdDev = 0.08;
    private const double IdealRangePercentageDefault = 70.0;
    private const double IdealRangeCenter = 0.5;
    private const double IdealRangeCenteringStdDev = 0.12;
    private const double ExistingAnchorWeight = 0.4;
    private const double IdealCenterWeight = 0.6;
    private const double IdealRangeCoverage = 0.8;
    private const double OuterRangeCoverage = 1.0 - IdealRangeCoverage;
    private readonly Random _random;
    private long _sequenceCounter = 1;

    public DataGenerator(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public IEnumerable<Dictionary<string, string>> GenerateRows(DataGenConfig config)
    {
        var columnsByName = config.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var numericNormalizedByColumn = config.Columns
            .Where(c => c.IsNumeric)
            .ToDictionary(c => c.Name, _ => new List<double>(), StringComparer.OrdinalIgnoreCase);

        var rowContexts = ResolveRowContexts(config, columnsByName);

        var sweepIdsByKey = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var rowContext in rowContexts)
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var col in config.Columns)
            {
                var value = GenerateValue(col, rowContext, numericNormalizedByColumn, sweepIdsByKey);
                row[col.Name] = value;
            }

            yield return row;
        }
    }

    private List<RowContext> ResolveRowContexts(
        DataGenConfig config,
        IReadOnlyDictionary<string, ColumnGenerationSpec> columnsByName)
    {
        var rowMode = config.RowMode.Trim().ToLowerInvariant();
        if (rowMode == "fixed")
        {
            return Enumerable.Range(0, config.NRows)
                .Select(i => new RowContext(i, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), string.Empty, 0))
                .ToList();
        }

        var sweepRows = GenerateSweepRowContexts(config, columnsByName).ToList();
        if (rowMode == "sweep" || sweepRows.Count >= config.NRows)
            return sweepRows;

        // rowMode=max keeps the sweep order intact and repeats the expanded
        // pattern until the requested row count is reached.
        var expandedRows = new List<RowContext>(config.NRows);
        for (var i = 0; i < config.NRows; i++)
        {
            var template = sweepRows[i % sweepRows.Count];
            expandedRows.Add(template with { RowIndex = i });
        }

        return expandedRows;
    }

    private string GenerateValue(
        ColumnGenerationSpec col,
        RowContext rowContext,
        Dictionary<string, List<double>> numericNormalizedByColumn,
        Dictionary<string, Dictionary<string, string>> sweepIdsByKey)
    {
        // Sweep IDs come from the row context, not the random generator.
        if (!string.IsNullOrWhiteSpace(col.GeneratedIdMode))
        {
            if (col.GeneratedIdMode.Equals("outer-group", StringComparison.OrdinalIgnoreCase))
                return GetOrCreateSweepIdValue(col, rowContext.SweepGroupKey, sweepIdsByKey);

            if (col.GeneratedIdMode.Equals("inner-step", StringComparison.OrdinalIgnoreCase))
                return FormatNumericByType(rowContext.InnermostStepIndex + (col.TotalRangeMin ?? 1.0), col.DataType);
        }

        // Preset values come from sweep expansion and should win before synthetic generation runs.
        if (rowContext.PresetValues.TryGetValue(col.Name, out var presetValue))
        {
            if (col.IsNumeric && double.TryParse(presetValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var numericPreset))
                numericNormalizedByColumn[col.Name].Add(Normalize(col, numericPreset));

            return presetValue;
        }

        if (col.IsNumeric)
        {
            var normalized = GenerateNormalizedNumericValue(col, rowContext.RowIndex, numericNormalizedByColumn);
            numericNormalizedByColumn[col.Name].Add(normalized);
            var numeric = Denormalize(col, normalized);
            return FormatNumericByType(numeric, col.DataType);
        }

        if (col.IsBoolean)
        {
            var truePct = Math.Clamp(col.TruePercentage ?? BooleanTruePercentageDefault, 0.0, PercentageScale);
            return (_random.NextDouble() * PercentageScale < truePct) ? "true" : "false";
        }

        if (col.IsDateTime)
        {
            var min = ParseDate(col.DateMinUtc) ?? DefaultDateMinUtc;
            var max = ParseDate(col.DateMaxUtc) ?? DefaultDateMaxUtc;
            if (max < min) (min, max) = (max, min);

            var delta = max - min;
            var ticks = (long)(_random.NextDouble() * delta.Ticks);
            var value = min.AddTicks(ticks);
            var format = string.IsNullOrWhiteSpace(col.DateFormat) ? "O" : col.DateFormat!;
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        if (col.IsGuid)
            return NextGuid().ToString();

        if (col.IsString)
        {
            if (!string.IsNullOrWhiteSpace(col.StaticValue))
                return col.StaticValue!;

            if (col.Values.Count > 0)
                return col.Values[_random.Next(col.Values.Count)];

            if (col.RandomString)
            {
                var prefix = col.StringPrefix ?? $"{col.Name}_";
                return prefix + RandomAlphaNumeric(col.RandomStringLength);
            }

            return string.Empty;
        }

        return string.Empty;
    }

    private double GenerateNormalizedNumericValue(
        ColumnGenerationSpec col,
        int rowIndex,
        Dictionary<string, List<double>> numericNormalizedByColumn)
    {
        var trackWith = AverageReferencedValues(col.TracksWith, rowIndex, numericNormalizedByColumn);
        var trackInverse = AverageReferencedValues(col.TracksInverselyWith, rowIndex, numericNormalizedByColumn);

        double anchor;
        if (trackWith.HasValue || trackInverse.HasValue)
        {
            var sum = 0.0;
            var count = 0;

            if (trackWith.HasValue) { sum += trackWith.Value; count++; }
            if (trackInverse.HasValue) { sum += (1.0 - trackInverse.Value); count++; }

            anchor = count == 0 ? _random.NextDouble() : sum / count;
            anchor = Math.Clamp(anchor + NextGaussian(0.0, CorrelatedNoiseStdDev), 0.0, 1.0);
        }
        else
        {
            anchor = _random.NextDouble();
        }

        var idealPct = Math.Clamp(col.PercentageInIdealRange ?? IdealRangePercentageDefault, 0.0, PercentageScale);
        if (_random.NextDouble() * PercentageScale < idealPct)
        {
            var centered = IdealRangeCenter + NextGaussian(0.0, IdealRangeCenteringStdDev);
            anchor = Math.Clamp((anchor * ExistingAnchorWeight) + (centered * IdealCenterWeight), 0.0, 1.0);
        }

        if (col.IsLogarithmic)
            anchor = Math.Pow(anchor, 2.2);

        return anchor;
    }

    private static double? AverageReferencedValues(
        IReadOnlyList<string> names,
        int rowIndex,
        Dictionary<string, List<double>> valuesByColumn)
    {
        if (names.Count == 0) return null;

        var values = new List<double>();
        foreach (var name in names)
        {
            if (valuesByColumn.TryGetValue(name, out var list) && rowIndex < list.Count)
                values.Add(list[rowIndex]);
        }

        return values.Count == 0 ? null : values.Average();
    }

    private double Denormalize(ColumnGenerationSpec col, double normalized)
    {
        var totalMin = col.TotalRangeMin ?? 0.0;
        var totalMax = col.TotalRangeMax ?? 1.0;
        if (totalMax < totalMin) (totalMin, totalMax) = (totalMax, totalMin);

        if (col.IdealRangeMin.HasValue && col.IdealRangeMax.HasValue)
        {
            var iMin = Math.Clamp(col.IdealRangeMin.Value, totalMin, totalMax);
            var iMax = Math.Clamp(col.IdealRangeMax.Value, totalMin, totalMax);
            if (iMax < iMin) (iMin, iMax) = (iMax, iMin);

            if (normalized <= IdealRangeCoverage)
                return Lerp(iMin, iMax, normalized / IdealRangeCoverage);

            var outerN = (normalized - IdealRangeCoverage) / OuterRangeCoverage;
            return _random.NextDouble() < 0.5
                ? Lerp(totalMin, iMin, outerN)
                : Lerp(iMax, totalMax, outerN);
        }

        return Lerp(totalMin, totalMax, normalized);
    }

    private static double Normalize(ColumnGenerationSpec col, double value)
    {
        var totalMin = col.TotalRangeMin ?? 0.0;
        var totalMax = col.TotalRangeMax ?? 1.0;
        if (Math.Abs(totalMax - totalMin) < double.Epsilon)
            return 0.0;

        if (totalMax < totalMin) (totalMin, totalMax) = (totalMax, totalMin);
        return Math.Clamp((value - totalMin) / (totalMax - totalMin), 0.0, 1.0);
    }

    private string GetOrCreateSweepIdValue(
        ColumnGenerationSpec col,
        string sweepGroupKey,
        Dictionary<string, Dictionary<string, string>> sweepIdsByKey)
    {
        if (!sweepIdsByKey.TryGetValue(sweepGroupKey, out var valuesByColumn))
        {
            valuesByColumn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            sweepIdsByKey[sweepGroupKey] = valuesByColumn;
        }

        if (valuesByColumn.TryGetValue(col.Name, out var existing))
            return existing;

        var next = NextSequenceValue(col);
        var formatted = FormatNumericByType(next, col.DataType);
        valuesByColumn[col.Name] = formatted;
        return formatted;
    }

    private double NextSequenceValue(ColumnGenerationSpec col)
    {
        var baseValue = col.TotalRangeMin ?? 1.0;
        var value = baseValue + (_sequenceCounter - 1);
        _sequenceCounter++;

        if (col.TotalRangeMax.HasValue)
            value = Math.Min(value, col.TotalRangeMax.Value);

        return value;
    }

    private IEnumerable<RowContext> GenerateSweepRowContexts(
        DataGenConfig config,
        IReadOnlyDictionary<string, ColumnGenerationSpec> columnsByName)
    {
        // The innermost sweep axis drives the step counter; the outer axes make up
        // the group identity used by generatedIdMode=outer-group.
        var groupAxisNames = config.SweepAxes.Count > 1
            ? config.SweepAxes.Take(config.SweepAxes.Count - 1).Select(a => a.Name).ToList()
            : [];
        var presetRows = ExpandSweepAxes(
                config.SweepAxes,
                0,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                columnsByName,
                groupAxisNames)
            .ToList();

        for (var i = 0; i < presetRows.Count; i++)
            yield return presetRows[i] with { RowIndex = i };
    }

    private IEnumerable<RowContext> ExpandSweepAxes(
        IReadOnlyList<SweepAxisSpec> axes,
        int axisIndex,
        Dictionary<string, string> currentValues,
        IReadOnlyDictionary<string, ColumnGenerationSpec> columnsByName,
        IReadOnlyList<string> groupAxisNames)
    {
        if (axisIndex >= axes.Count)
        {
            var presetValues = new Dictionary<string, string>(currentValues, StringComparer.OrdinalIgnoreCase);
            var sweepGroupKey = groupAxisNames.Count == 0
                ? string.Empty
                : string.Join("|", groupAxisNames.Select(name => $"{name}={presetValues[name]}"));
            var innermostAxisName = axes[^1].Name;
            // The raw index of the innermost configured value is the basis for generatedIdMode=inner-step.
            var innermostStepIndex = columnsByName[innermostAxisName].Values.FindIndex(v => StringComparer.Ordinal.Equals(v, presetValues[innermostAxisName]));
            yield return new RowContext(0, presetValues, sweepGroupKey, innermostStepIndex);
            yield break;
        }

        var axis = axes[axisIndex];
        var column = columnsByName[axis.Name];

        foreach (var value in column.Values)
        {
            currentValues[axis.Name] = value;

            foreach (var expanded in ExpandSweepAxes(axes, axisIndex + 1, currentValues, columnsByName, groupAxisNames))
                yield return expanded;
        }

        currentValues.Remove(axis.Name);
    }

    private static string FormatNumericByType(double value, string dataType)
    {
        return dataType.Trim().ToLowerInvariant() switch
        {
            "double" => value.ToString("G17", CultureInfo.InvariantCulture),
            "float" => ((float)value).ToString("G9", CultureInfo.InvariantCulture),
            "decimal" => ((decimal)value).ToString(CultureInfo.InvariantCulture),
            "int" => ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture),
            "long" => ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture),
            "bigint" => ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture),
            "short" => ((short)Math.Round(value)).ToString(CultureInfo.InvariantCulture),
            "byte" => ((byte)Math.Clamp(Math.Round(value), byte.MinValue, byte.MaxValue)).ToString(CultureInfo.InvariantCulture),
            _ => value.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private Guid NextGuid()
    {
        var bytes = new byte[16];
        _random.NextBytes(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private string RandomAlphaNumeric(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
            sb.Append(chars[_random.Next(chars.Length)]);
        return sb.ToString();
    }

    private double NextGaussian(double mean, double stdDev)
    {
        var u1 = 1.0 - _random.NextDouble();
        var u2 = 1.0 - _random.NextDouble();
        var randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + stdDev * randStdNormal;
    }

    private static double Lerp(double min, double max, double t) => min + ((max - min) * t);

    private sealed record RowContext(
        int RowIndex,
        Dictionary<string, string> PresetValues,
        string SweepGroupKey,
        int InnermostStepIndex);
}
