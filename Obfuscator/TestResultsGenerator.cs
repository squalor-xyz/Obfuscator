// SPDX-License-Identifier: MPL-2.0
/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Squalor.Obfuscator;

// Writes a wide production-test results file: optional "Key   :,value" preamble lines, optional annotation rows,
// a header, then one fixed-width row per device. Bytes go straight to the stream (no quoting, raw bytes allowed)
// and rows are streamed, so the file is never held in memory. Everything comes from one seeded Random in a fixed
// order: per device the bin first, then each test cell in column order.
internal sealed class TestResultsGenerator
{
    private const int MaxFormatPrecision = 20;
    private const int OutputBufferSize = 1 << 16;
    private const int ScratchSize = 512;
    private const double PercentMax = 100.0;
    private const double NormalSpreadBound = 9.0;
    private const double OutsideDeltaMinFraction = 0.01;
    private const double OutsideDeltaSpreadFraction = 0.09;
    // A value this many last places beyond a limit still prints beyond it after rounding.
    private const double OutsideDeltaRoundingUlps = 1.5;

    private static readonly Regex FormatPattern = new(@"^%\.([0-9]{1,2})([fe])$", RegexOptions.CultureInvariant);

    private readonly int _devices;
    private readonly Random _random;
    private readonly byte[] _lineEnding;
    private readonly List<byte[]> _preambleLines = new();
    private readonly List<AnnotationRow> _annotationRows = new();
    private readonly List<DeviceColumn> _deviceColumns = new();
    private readonly List<TestColumn> _testColumns = new();

    public TestResultsGenerator(DataGenConfig config)
    {
        var spec = config.TestResults ?? throw Fail("rowMode 'test-results' requires a testResults object.");

        if (config.Columns.Count > 0)
            throw Fail("rowMode 'test-results' cannot be used with columns. Describe the file with testResults instead.");

        if (config.SweepAxes.Count > 0)
            throw Fail("rowMode 'test-results' cannot be used with sweepAxes.");

        // Rows are streamed, but the count is still an int and shares the other modes' limit.
        if (config.NRows < 0 || config.NRows > Array.MaxLength)
            throw Fail($"NRows is the device count and must be between 0 and {Array.MaxLength}, not {config.NRows}.");

        _devices = config.NRows;
        _random = config.Seed.HasValue ? new Random(config.Seed.Value) : new Random();

        if (spec.TestColumnPadding < 0)
            throw Fail("testColumnPadding must not be negative.");

        _lineEnding = Normalize(spec.LineEnding) switch
        {
            "crlf" => "\r\n"u8.ToArray(),
            "lf" => "\n"u8.ToArray(),
            _ => throw Fail($"lineEnding '{spec.LineEnding}' is not supported. Expected one of: crlf, lf.")
        };

        if (spec.Preamble is not null)
            BuildPreamble(spec.Preamble);

        BuildDeviceColumns(spec.DeviceColumns);
        BuildTestColumns(spec);
        if (_testColumns.Count == 0)
            throw Fail("at least one test column is required: conditionSets must expand to at least one test.");

        BuildAnnotationRows(spec);
        RejectDuplicateColumnNames();
    }

    internal static void Validate(DataGenConfig config) => _ = new TestResultsGenerator(config);

    public void Write(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var writer = new OutputBuffer(output, _lineEnding);
        Span<byte> scratch = stackalloc byte[ScratchSize];

        foreach (var line in _preambleLines)
        {
            writer.Raw(line);
            writer.EndLine();
        }

        foreach (var row in _annotationRows)
            WriteAnnotationRow(writer, row, scratch);

        WriteHeader(writer);

        for (var device = 0; device < _devices; device++)
            WriteDeviceRow(writer, device, scratch);

        writer.Flush();
    }

    // ---- writing ----

    private void WriteAnnotationRow(OutputBuffer writer, AnnotationRow row, Span<byte> scratch)
    {
        var first = _deviceColumns[0];
        // The label and its colon fill the first device column; the other device columns stay blank.
        writer.CheckFits(row.Label.Length + 1, first.Width, first.Name);
        writer.Raw(row.Label);
        writer.Spaces(first.Width - row.Label.Length - 1);
        writer.Byte((byte)':');

        for (var i = 1; i < _deviceColumns.Count; i++)
        {
            writer.Byte((byte)',');
            writer.Spaces(_deviceColumns[i].Width);
        }

        for (var i = 0; i < _testColumns.Count; i++)
        {
            writer.Byte((byte)',');
            var column = _testColumns[i];
            switch (row.Source)
            {
                case AnnotationSource.ProgramLine:
                    writer.Cell(FormatLong(row.ProgramLineStart + row.ProgramLineStep * i, scratch), column.Width, column.Header);
                    break;
                case AnnotationSource.UpperLimit:
                    WriteNumberOrBlank(writer, column, column.Upper, scratch);
                    break;
                case AnnotationSource.LowerLimit:
                    WriteNumberOrBlank(writer, column, column.Lower, scratch);
                    break;
                default:
                    writer.Cell(column.Unit, column.Width, column.Header);
                    break;
            }
        }

        writer.EndLine();
    }

    private static void WriteNumberOrBlank(OutputBuffer writer, TestColumn column, double? value, Span<byte> scratch)
    {
        var length = value.HasValue ? column.FormatNumber(value.Value, scratch) : 0;
        writer.Cell(scratch[..length], column.Width, column.Header);
    }

    private void WriteHeader(OutputBuffer writer)
    {
        var first = true;
        foreach (var column in _deviceColumns)
        {
            if (!first)
                writer.Byte((byte)',');
            first = false;
            writer.Cell(column.NameBytes, column.Width, column.Name);
        }

        foreach (var column in _testColumns)
        {
            if (!first)
                writer.Byte((byte)',');
            first = false;
            writer.Cell(column.HeaderBytes, column.Width, column.Header);
        }

        writer.EndLine();
    }

    private void WriteDeviceRow(OutputBuffer writer, int device, Span<byte> scratch)
    {
        var first = true;
        foreach (var column in _deviceColumns)
        {
            if (!first)
                writer.Byte((byte)',');
            first = false;

            switch (column.Kind)
            {
                case DeviceKind.Serial:
                    writer.Cell(FormatLong(column.Start + column.Step * device, scratch), column.Width, column.Name);
                    break;
                case DeviceKind.Bin:
                    writer.Cell(DrawBin(column), column.Width, column.Name);
                    break;
                default:
                    writer.Cell(column.Fixed, column.Width, column.Name);
                    break;
            }
        }

        foreach (var column in _testColumns)
        {
            if (!first)
                writer.Byte((byte)',');
            first = false;
            WriteTestCell(writer, column, scratch);
        }

        writer.EndLine();
    }

    private byte[] DrawBin(DeviceColumn column)
    {
        var target = _random.NextDouble() * column.TotalWeight;
        for (var i = 0; i < column.CumulativeWeights.Length; i++)
        {
            if (target < column.CumulativeWeights[i])
                return column.BinValues[i];
        }

        return column.BinValues[^1];
    }

    private void WriteTestCell(OutputBuffer writer, TestColumn column, Span<byte> scratch)
    {
        // The same draws happen for every cell, whatever the rates, so a rate change moves no other value.
        var nullDraw = _random.NextDouble() * PercentMax;
        var outOfLimitDraw = _random.NextDouble() * PercentMax;
        var deltaDraw = _random.NextDouble();
        var raw = column.IsNormal
            ? column.Mean + column.StdDev * NextStandardNormal()
            : column.Min + (column.Max - column.Min) * _random.NextDouble();

        if (nullDraw < column.NullRate)
        {
            writer.Cell(column.NullToken, column.Width, column.Header);
            return;
        }

        var value = outOfLimitDraw < column.OutOfLimitRate
            ? JustOutsideNearerLimit(column, raw, deltaDraw)
            : ClampToLimits(column, raw);
        if (!double.IsFinite(value))
            throw new InvalidOperationException($"testResults: a value for column '{column.Header}' is not finite.");

        writer.Cell(scratch[..column.FormatNumber(value, scratch)], column.Width, column.Header);
    }

    private double NextStandardNormal()
    {
        var u1 = 1.0 - _random.NextDouble();
        var u2 = _random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static double ClampToLimits(TestColumn column, double value)
    {
        if (column.Upper is { } upper && value > upper)
            value = upper;
        if (column.Lower is { } lower && value < lower)
            value = lower;
        return value;
    }

    private static double JustOutsideNearerLimit(TestColumn column, double raw, double deltaDraw)
    {
        var useUpper = column.Upper.HasValue &&
            (!column.Lower.HasValue || Math.Abs(raw - column.Upper.Value) <= Math.Abs(raw - column.Lower.Value));
        var limit = useUpper ? column.Upper!.Value : column.Lower!.Value;
        var scale = column.Upper.HasValue && column.Lower.HasValue && column.Upper > column.Lower
            ? column.Upper.Value - column.Lower.Value
            : Math.Max(Math.Abs(limit), 1.0);
        var delta = Math.Max(
            scale * (OutsideDeltaMinFraction + OutsideDeltaSpreadFraction * deltaDraw),
            column.MinOutsideDelta(limit));
        return useUpper ? limit + delta : limit - delta;
    }

    private static ReadOnlySpan<byte> FormatLong(long value, Span<byte> scratch)
    {
        value.TryFormat(scratch, out var written, default, CultureInfo.InvariantCulture);
        return scratch[..written];
    }

    // ---- building and validation ----

    private void BuildPreamble(PreambleSpec preamble)
    {
        if (preamble.KeyWidth < 0)
            throw Fail("preamble keyWidth must not be negative.");

        var counters = 0;
        var keyBytes = new List<byte[]>();
        var valueBytes = new List<byte[]>();
        foreach (var entry in preamble.Entries)
        {
            var key = entry.Key ?? string.Empty;
            keyBytes.Add(PadKey(Encoding.UTF8.GetBytes(key), preamble.KeyWidth));
            valueBytes.Add(entry.Kind is null ? throw Fail($"preamble entry '{key}' needs a kind.") : PreambleValue(entry, key, counters));
            if (Normalize(entry.Kind) == "counter")
                counters++;
        }

        foreach (var inject in preamble.Inject)
        {
            var matches = Enumerable.Range(0, preamble.Entries.Count)
                .Where(i => string.Equals(preamble.Entries[i].Key, inject.Key, StringComparison.Ordinal))
                .ToList();
            if (matches.Count == 0)
                throw Fail($"inject key '{inject.Key}' does not match any preamble entry.");

            if (inject.Bytes.Any(b => b is < 0 or > 255))
                throw Fail($"inject for key '{inject.Key}' has a byte value outside 0..255.");

            foreach (var index in matches)
            {
                if (inject.Offset < 0 || inject.Offset + inject.Bytes.Count > keyBytes[index].Length)
                    throw Fail($"inject offset {inject.Offset} with {inject.Bytes.Count} byte(s) does not fit inside key '{inject.Key}' ({keyBytes[index].Length} bytes).");

                for (var i = 0; i < inject.Bytes.Count; i++)
                    keyBytes[index][inject.Offset + i] = (byte)inject.Bytes[i];
            }
        }

        for (var i = 0; i < keyBytes.Count; i++)
        {
            var line = new byte[keyBytes[i].Length + 2 + valueBytes[i].Length];
            keyBytes[i].CopyTo(line, 0);
            line[keyBytes[i].Length] = (byte)':';
            line[keyBytes[i].Length + 1] = (byte)',';
            valueBytes[i].CopyTo(line, keyBytes[i].Length + 2);
            _preambleLines.Add(line);
        }
    }

    private static byte[] PadKey(byte[] key, int width)
    {
        if (key.Length >= width)
            return key;

        var padded = new byte[width];
        key.CopyTo(padded, 0);
        Array.Fill(padded, (byte)' ', key.Length, width - key.Length);
        return padded;
    }

    private static byte[] PreambleValue(PreambleEntrySpec entry, string key, int countersBefore)
    {
        switch (Normalize(entry.Kind))
        {
            case "static":
                return Encoding.UTF8.GetBytes(entry.Value ?? string.Empty);
            case "date":
                if (entry.Value is null || !DataGenerator.TryParseDate(entry.Value, out var date))
                    throw Fail($"preamble entry '{key}' needs a valid date as its value (ISO 8601), not '{entry.Value}'.");

                try
                {
                    var format = string.IsNullOrEmpty(entry.Format) ? "yyyy-MM-dd HH:mm:ss" : entry.Format;
                    return Encoding.UTF8.GetBytes(date.ToString(format, CultureInfo.InvariantCulture));
                }
                catch (FormatException)
                {
                    throw Fail($"preamble entry '{key}' has an invalid date format '{entry.Format}'.");
                }
            case "counter":
                return Encoding.UTF8.GetBytes((entry.Start + entry.Step * countersBefore).ToString(CultureInfo.InvariantCulture));
            default:
                throw Fail($"preamble entry '{key}' kind '{entry.Kind}' is not supported. Expected one of: static, date, counter.");
        }
    }

    private void BuildDeviceColumns(DeviceColumnsSpec devices)
    {
        if (devices.Serial is { } serial)
        {
            _deviceColumns.Add(new DeviceColumn(DeviceKind.Serial, serial.Name, CheckWidth(serial.Name, serial.Width))
            {
                Start = serial.Start,
                Step = serial.Step
            });
        }

        foreach (var fixedColumn in new[] { devices.Site, devices.ProberX, devices.ProberY })
        {
            if (fixedColumn is null)
                continue;

            _deviceColumns.Add(new DeviceColumn(DeviceKind.Fixed, fixedColumn.Name, CheckWidth(fixedColumn.Name, fixedColumn.Width))
            {
                Fixed = Encoding.UTF8.GetBytes(fixedColumn.Value ?? string.Empty)
            });
        }

        if (devices.Bin is { } bin)
            _deviceColumns.Add(BuildBinColumn(bin));
    }

    private static int CheckWidth(string name, int width)
    {
        if (width < 0)
            throw Fail($"device column '{name}' width must not be negative.");
        return width;
    }

    private static DeviceColumn BuildBinColumn(BinColumnSpec bin)
    {
        var width = CheckWidth(bin.Name, bin.Width);
        if (bin.Weights.Count == 0)
            throw Fail($"bin column '{bin.Name}' weights must not be empty.");

        // Ordinal key order, so the draw does not depend on how the JSON listed the bins.
        var keys = bin.Weights.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var cumulative = new double[keys.Length];
        var total = 0.0;
        for (var i = 0; i < keys.Length; i++)
        {
            var weight = bin.Weights[keys[i]];
            if (!double.IsFinite(weight) || weight < 0)
                throw Fail($"bin column '{bin.Name}' weight for bin '{keys[i]}' must be a finite number that is not negative.");

            total += weight;
            cumulative[i] = total;
        }

        if (total <= 0)
            throw Fail($"bin column '{bin.Name}' weights must add up to more than 0.");

        return new DeviceColumn(DeviceKind.Bin, bin.Name, width)
        {
            BinValues = keys.Select(k => Encoding.UTF8.GetBytes(k)).ToArray(),
            CumulativeWeights = cumulative,
            TotalWeight = total
        };
    }

    private void BuildTestColumns(TestResultsSpec spec)
    {
        long columnCount = 0;
        for (var setIndex = 0; setIndex < spec.ConditionSets.Count; setIndex++)
        {
            var set = spec.ConditionSets[setIndex];
            var tests = set.Tests.Select(BuildTestTemplate).ToList();
            var conditionSets = ExpandConditionSet(set);

            columnCount = checked(columnCount + (long)conditionSets.Count * tests.Count);
            if (columnCount > Array.MaxLength)
                throw Fail($"the condition sets expand to more than {Array.MaxLength} test columns.");

            foreach (var conditions in conditionSets)
            {
                foreach (var test in tests)
                    _testColumns.Add(test.ToColumn(conditions, spec.TestColumnPadding));
            }
        }
    }

    private static List<List<(string Key, string Value)>> ExpandConditionSet(ConditionSetSpec set)
    {
        if (set.Conditions is not null && set.Axes is not null)
            throw Fail("a condition set must define either conditions or axes, not both.");

        if (set.Conditions is not null)
        {
            var pairs = new List<(string, string)>();
            foreach (var pair in set.Conditions)
            {
                if (pair is null || pair.Count != 2)
                    throw Fail("each entry of conditions must be a [key, value] pair.");
                pairs.Add((pair[0], pair[1]));
            }

            return [pairs];
        }

        if (set.Axes is null)
            throw Fail("a condition set must define conditions or axes.");

        if (set.Axes.Count == 0)
            throw Fail("a condition set's axes must not be empty.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        long combinations = 1;
        foreach (var axis in set.Axes)
        {
            if (string.IsNullOrEmpty(axis.Key))
                throw Fail("an axis key must not be empty.");

            if (!seen.Add(axis.Key))
                throw Fail($"duplicate axis key '{axis.Key}'.");

            if (axis.Values.Count == 0)
                throw Fail($"axis '{axis.Key}' has no values.");

            combinations *= axis.Values.Count;
            if (combinations > Array.MaxLength)
                throw Fail($"the axes expand to more than {Array.MaxLength} condition sets.");
        }

        var order = Enumerable.Range(0, set.Axes.Count).ToArray();
        if (set.KeyOrder is not null)
        {
            var indexByKey = set.Axes.Select((a, i) => (a.Key, i)).ToDictionary(t => t.Key, t => t.i, StringComparer.Ordinal);
            if (set.KeyOrder.Count != set.Axes.Count ||
                set.KeyOrder.Distinct(StringComparer.Ordinal).Count() != set.KeyOrder.Count ||
                !set.KeyOrder.All(indexByKey.ContainsKey))
            {
                throw Fail("keyOrder must list each axis key exactly once.");
            }

            order = set.KeyOrder.Select(k => indexByKey[k]).ToArray();
        }

        // First axis is the outermost loop, whatever the key order inside the brackets.
        var result = new List<List<(string Key, string Value)>>((int)combinations);
        var counter = new int[set.Axes.Count];
        for (long n = 0; n < combinations; n++)
        {
            result.Add(order.Select(a => (set.Axes[a].Key, set.Axes[a].Values[counter[a]])).ToList());

            for (var a = set.Axes.Count - 1; a >= 0; a--)
            {
                if (++counter[a] < set.Axes[a].Values.Count)
                    break;
                counter[a] = 0;
            }
        }

        return result;
    }

    private static TestTemplate BuildTestTemplate(TestSpec test)
    {
        if (string.IsNullOrEmpty(test.Name))
            throw Fail("a test name must not be empty.");

        var name = test.Name;
        var quirk = Normalize(test.Quirk);
        if (quirk is not ("" or "casevariant" or "repeatunit"))
            throw Fail($"test '{name}' quirk '{test.Quirk}' is not supported. Expected one of: caseVariant, repeatUnit.");

        if (quirk == "repeatunit" && string.IsNullOrEmpty(test.Unit))
            throw Fail($"test '{name}' uses the repeatUnit quirk, which needs a unit.");

        foreach (var limit in new[] { test.UpperLimit, test.LowerLimit })
        {
            if (limit.HasValue && !double.IsFinite(limit.Value))
                throw Fail($"test '{name}' limits must be finite.");
        }

        if (test.UpperLimit < test.LowerLimit)
            throw Fail($"test '{name}' upperLimit is below its lowerLimit.");

        if (test.UpperLimit.HasValue && test.LowerLimit.HasValue && !double.IsFinite(test.UpperLimit.Value - test.LowerLimit.Value))
            throw Fail($"test '{name}' limits are so far apart that their span is not finite.");

        var template = new TestTemplate(test, quirk);
        switch (Normalize(test.Distribution))
        {
            case "uniform":
                if (!test.Min.HasValue || !test.Max.HasValue || !double.IsFinite(test.Min.Value) || !double.IsFinite(test.Max.Value))
                    throw Fail($"test '{name}' uniform distribution needs a finite min and max.");

                if (test.Min > test.Max)
                    throw Fail($"test '{name}' uniform min is above its max.");

                if (!double.IsFinite(test.Max.Value - test.Min.Value))
                    throw Fail($"test '{name}' uniform range is so wide that max - min is not finite.");
                break;
            case "normal":
                if (!test.Mean.HasValue || !test.StdDev.HasValue || !double.IsFinite(test.Mean.Value) || !double.IsFinite(test.StdDev.Value))
                    throw Fail($"test '{name}' normal distribution needs a finite mean and stdDev.");

                if (test.StdDev < 0)
                    throw Fail($"test '{name}' stdDev must not be negative.");

                // A standard normal draw stays within 9 standard deviations.
                if (!double.IsFinite(Math.Abs(test.Mean.Value) + NormalSpreadBound * test.StdDev.Value))
                    throw Fail($"test '{name}' mean and stdDev are so large that values would not be finite.");
                break;
            default:
                throw Fail($"test '{name}' distribution '{test.Distribution}' is not supported. Expected one of: uniform, normal.");
        }

        var match = FormatPattern.Match(test.Format ?? string.Empty);
        if (!match.Success || int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) > MaxFormatPrecision)
            throw Fail($"test '{name}' format '{test.Format}' is not supported. Only %.Nf and %.Ne (N from 0 to {MaxFormatPrecision}) are supported.");

        foreach (var (field, rate) in new[] { ("nullRate", test.NullRate), ("outOfLimitRate", test.OutOfLimitRate) })
        {
            if (!double.IsFinite(rate) || rate < 0 || rate > PercentMax)
                throw Fail($"test '{name}' {field} must be between 0 and 100.");
        }

        if (test.OutOfLimitRate > 0 && !test.UpperLimit.HasValue && !test.LowerLimit.HasValue)
            throw Fail($"test '{name}' outOfLimitRate needs an upperLimit or a lowerLimit.");

        template.Format = new NumberFormat(match.Groups[2].Value == "e", int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        return template;
    }

    private void BuildAnnotationRows(TestResultsSpec spec)
    {
        if (spec.Annotations.Count == 0)
            return;

        if (_deviceColumns.Count == 0)
            throw Fail("annotations need at least one device column to hold their labels.");

        var first = _deviceColumns[0];
        foreach (var annotation in spec.Annotations)
        {
            var source = Normalize(annotation.Source) switch
            {
                "programline" => AnnotationSource.ProgramLine,
                "upperlimit" => AnnotationSource.UpperLimit,
                "lowerlimit" => AnnotationSource.LowerLimit,
                "unit" => AnnotationSource.Unit,
                _ => throw Fail($"annotation '{annotation.Label}' source '{annotation.Source}' is not supported. Expected one of: programLine, upperLimit, lowerLimit, unit.")
            };

            var label = Encoding.UTF8.GetBytes(annotation.Label ?? string.Empty);
            if (label.Length + 1 > first.Width)
                throw Fail($"annotation label '{annotation.Label}' with its colon does not fit in the first device column '{first.Name}' (width {first.Width}).");

            _annotationRows.Add(new AnnotationRow(label, source, spec.ProgramLineStart, spec.ProgramLineStep));
        }
    }

    private void RejectDuplicateColumnNames()
    {
        var names = _deviceColumns.Select(c => c.Name).Concat(_testColumns.Select(c => c.Header));
        var duplicates = names
            .GroupBy(n => n, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
            throw Fail("Duplicate column names: " + string.Join(", ", duplicates));
    }

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static InvalidOperationException Fail(string message) => new("testResults: " + message);

    // ---- types ----

    private enum DeviceKind
    {
        Serial,
        Fixed,
        Bin
    }

    private enum AnnotationSource
    {
        ProgramLine,
        UpperLimit,
        LowerLimit,
        Unit
    }

    private sealed record AnnotationRow(byte[] Label, AnnotationSource Source, long ProgramLineStart, long ProgramLineStep);

    private sealed class DeviceColumn
    {
        public DeviceColumn(DeviceKind kind, string? name, int width)
        {
            Kind = kind;
            Name = name ?? string.Empty;
            NameBytes = Encoding.UTF8.GetBytes(Name);
            Width = width;
        }

        public DeviceKind Kind { get; }
        public string Name { get; }
        public byte[] NameBytes { get; }
        public int Width { get; }
        public long Start { get; init; }
        public long Step { get; init; }
        public byte[] Fixed { get; init; } = [];
        public byte[][] BinValues { get; init; } = [];
        public double[] CumulativeWeights { get; init; } = [];
        public double TotalWeight { get; init; }
    }

    private readonly struct NumberFormat(bool exponent, int precision)
    {
        private readonly string _format = (exponent ? "e" : "F") + precision.ToString(CultureInfo.InvariantCulture);

        public bool Exponent { get; } = exponent;
        public int Precision { get; } = precision;

        public int Format(double value, Span<byte> destination)
        {
            if (!value.TryFormat(destination, out var written, _format, CultureInfo.InvariantCulture))
                throw new InvalidOperationException("testResults: a formatted value does not fit the scratch buffer.");

            return Exponent ? TrimExponent(destination, written) : written;
        }

        // .NET writes at least three exponent digits (1.5e+003); printf writes at least two (1.5e+03).
        private static int TrimExponent(Span<byte> text, int length)
        {
            var e = text[..length].IndexOf((byte)'e');
            var digits = e + 2;
            var skip = 0;
            while (length - digits - skip > 2 && text[digits + skip] == (byte)'0')
                skip++;

            if (skip > 0)
                text[(digits + skip)..length].CopyTo(text[digits..]);

            return length - skip;
        }
    }

    // A test as configured, before it is combined with a condition set.
    private sealed class TestTemplate(TestSpec spec, string quirk)
    {
        public NumberFormat Format { get; set; }

        public TestColumn ToColumn(IReadOnlyList<(string Key, string Value)> conditions, int padding)
        {
            var header = new StringBuilder();
            if (conditions.Count > 0)
                header.Append('[').AppendJoin('|', conditions.Select(c => c.Key + "=" + c.Value)).Append(']');

            header.Append(quirk == "casevariant" ? OppositeCase(spec.Name) : spec.Name);
            if (!string.IsNullOrEmpty(spec.Unit))
            {
                header.Append('(').Append(spec.Unit).Append(')');
                if (quirk == "repeatunit")
                    header.Append('(').Append(spec.Unit).Append(')');
            }

            return new TestColumn(spec, header.ToString(), padding, Format);
        }

        private static string OppositeCase(string name)
            => name == name.ToLowerInvariant() ? name.ToUpperInvariant() : name.ToLowerInvariant();
    }

    private sealed class TestColumn
    {
        private readonly NumberFormat _format;
        private readonly double _ulp;

        public TestColumn(TestSpec spec, string header, int padding, NumberFormat format)
        {
            _format = format;
            _ulp = Math.Pow(10, -format.Precision);
            Header = header;
            HeaderBytes = Encoding.UTF8.GetBytes(header);
            Width = HeaderBytes.Length + padding;
            Unit = Encoding.UTF8.GetBytes(spec.Unit ?? string.Empty);
            NullToken = Encoding.UTF8.GetBytes(spec.NullToken ?? string.Empty);
            Upper = spec.UpperLimit;
            Lower = spec.LowerLimit;
            NullRate = spec.NullRate;
            OutOfLimitRate = spec.OutOfLimitRate;
            IsNormal = Normalize(spec.Distribution) == "normal";
            Min = spec.Min ?? 0;
            Max = spec.Max ?? 0;
            Mean = spec.Mean ?? 0;
            StdDev = spec.StdDev ?? 0;
        }

        public string Header { get; }
        public byte[] HeaderBytes { get; }
        public int Width { get; }
        public byte[] Unit { get; }
        public byte[] NullToken { get; }
        public double? Upper { get; }
        public double? Lower { get; }
        public double NullRate { get; }
        public double OutOfLimitRate { get; }
        public bool IsNormal { get; }
        public double Min { get; }
        public double Max { get; }
        public double Mean { get; }
        public double StdDev { get; }

        public int FormatNumber(double value, Span<byte> destination) => _format.Format(value, destination);

        // Far enough past a limit that rounding to the print format cannot pull the value back inside.
        public double MinOutsideDelta(double limit)
            => OutsideDeltaRoundingUlps * _ulp * (_format.Exponent ? Math.Abs(limit) : 1.0);
    }

    // Buffers output in 64 KB chunks and checks every cell against its column width.
    private sealed class OutputBuffer(Stream output, byte[] lineEnding)
    {
        private readonly byte[] _buffer = new byte[OutputBufferSize];
        private int _length;

        public void Raw(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length > _buffer.Length - _length)
            {
                Flush();
                if (bytes.Length > _buffer.Length)
                {
                    output.Write(bytes);
                    return;
                }
            }

            bytes.CopyTo(_buffer.AsSpan(_length));
            _length += bytes.Length;
        }

        public void Byte(byte value)
        {
            if (_length == _buffer.Length)
                Flush();
            _buffer[_length++] = value;
        }

        public void Spaces(int count)
        {
            while (count > 0)
            {
                if (_length == _buffer.Length)
                    Flush();

                var chunk = Math.Min(count, _buffer.Length - _length);
                _buffer.AsSpan(_length, chunk).Fill((byte)' ');
                _length += chunk;
                count -= chunk;
            }
        }

        public void Cell(ReadOnlySpan<byte> content, int width, string column)
        {
            CheckFits(content.Length, width, column);
            Raw(content);
            Spaces(width - content.Length);
        }

        public void CheckFits(int length, int width, string column)
        {
            if (length > width)
                throw new InvalidOperationException($"testResults: a cell of {length} bytes in column '{column}' exceeds its width of {width}.");
        }

        public void EndLine() => Raw(lineEnding);

        public void Flush()
        {
            if (_length == 0)
                return;

            output.Write(_buffer, 0, _length);
            _length = 0;
        }
    }
}
