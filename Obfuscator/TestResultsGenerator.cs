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
// order: per device the bin first, then each test cell in column order. A cell takes three fixed draws (null,
// out-of-limit, delta) and then one or more value draws: a value whose printed form falls outside the limits is drawn
// again, up to MaxRedraws times, rather than clamped onto a limit. An out-of-limit value with equal limits takes one
// more draw to pick its side. Prober ranges take no draws: they are computed from the device index.
internal sealed class TestResultsGenerator
{
    private const int MaxFormatPrecision = 20;
    private const int OutputBufferSize = 1 << 16;
    private const int ScratchSize = 512;
    private const double PercentMax = 100.0;
    private const double NormalSpreadBound = 9.0;
    private const int MaxRedraws = 10_000;
    // At 1% a cell exhausts MaxRedraws with probability about 1e-44.
    private const double MinInsideLimitsProbability = 0.01;
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

        BuildDeviceColumns(Required(spec.DeviceColumns, "deviceColumns"));
        BuildTestColumns(spec);
        if (_testColumns.Count == 0)
            throw Fail("at least one test column is required: conditionSets must expand to at least one test.");

        BuildAnnotationRows(spec);
        RejectDuplicateColumnNames();
        CheckFixedTestCellsFit();
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
                case DeviceKind.Range:
                    // Validation has bounded the value: it lies between the range's min and max.
                    var offset = (UInt128)device / column.RangeStride % column.RangeCount;
                    writer.Cell(FormatLong((long)((Int128)column.RangeMin + (Int128)offset), scratch), column.Width, column.Name);
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
        // Three draws happen for every cell, whatever the rates. Value redraws take a varying number of further draws,
        // so a rate change can shift the values of later cells.
        var nullDraw = _random.NextDouble() * PercentMax;
        var outOfLimitDraw = _random.NextDouble() * PercentMax;
        var deltaDraw = _random.NextDouble();
        var raw = DrawValue(column);

        if (nullDraw < column.NullRate)
        {
            writer.Cell(column.NullToken, column.Width, column.Header);
            return;
        }

        if (outOfLimitDraw < column.OutOfLimitRate)
        {
            writer.Cell(scratch[..FormatChecked(column, JustOutsideNearerLimit(column, raw, deltaDraw), scratch)], column.Width, column.Header);
            return;
        }

        var length = FormatChecked(column, raw, scratch);
        // Judge the printed value: that is what a reader of the file compares with the limits.
        for (var draws = 1; column.HasLimits && !PrintedInsideLimits(column, scratch[..length]); draws++)
        {
            if (draws >= MaxRedraws)
                throw new InvalidOperationException($"testResults: column '{column.Header}' could not draw a value whose printed form lies inside its limits in {MaxRedraws} attempts.");

            length = FormatChecked(column, DrawValue(column), scratch);
        }

        writer.Cell(scratch[..length], column.Width, column.Header);
    }

    private double DrawValue(TestColumn column)
        => column.IsNormal
            ? column.Mean + column.StdDev * NextStandardNormal()
            : column.Min + (column.Max - column.Min) * _random.NextDouble();

    private static int FormatChecked(TestColumn column, double value, Span<byte> scratch)
    {
        if (!double.IsFinite(value))
            throw new InvalidOperationException($"testResults: a value for column '{column.Header}' is not finite.");

        return column.FormatNumber(value, scratch);
    }

    // Limits are inclusive: a value may print exactly on a limit when rounding puts it there.
    private static bool PrintedInsideLimits(TestColumn column, ReadOnlySpan<byte> printed)
    {
        var value = double.Parse(printed, NumberStyles.Float, CultureInfo.InvariantCulture);
        return (!column.Lower.HasValue || value >= column.Lower.Value) && (!column.Upper.HasValue || value <= column.Upper.Value);
    }

    private double NextStandardNormal()
    {
        var u1 = 1.0 - _random.NextDouble();
        var u2 = _random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private double JustOutsideNearerLimit(TestColumn column, double raw, double deltaDraw)
    {
        // Equal limits have no nearer side, so one draw picks it. The draw happens only then: any other cell takes the
        // same draws as before.
        var useUpper = column.Upper.HasValue && column.Lower.HasValue && column.Upper == column.Lower
            ? _random.NextDouble() < 0.5
            : column.Upper.HasValue &&
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

        var entries = RequiredItems(preamble.Entries, "preamble.entries");
        var injects = RequiredItems(preamble.Inject, "preamble.inject");
        var keyBytes = new List<byte[]>();
        var valueBytes = new List<byte[]>();
        for (var e = 0; e < entries.Count; e++)
        {
            var entry = entries[e];
            var field = $"preamble.entries[{e}]";
            var key = entry.Key ?? string.Empty;
            RejectSeparators(key, field + ".key");
            keyBytes.Add(PadKey(Encoding.UTF8.GetBytes(key), preamble.KeyWidth));
            valueBytes.Add(entry.Kind is null ? throw Fail($"preamble entry '{key}' needs a kind.") : PreambleValue(entry, key, field));
        }

        for (var j = 0; j < injects.Count; j++)
        {
            var inject = injects[j];
            var injectBytes = Required(inject.Bytes, $"preamble.inject[{j}].bytes");
            var matches = Enumerable.Range(0, entries.Count)
                .Where(i => string.Equals(entries[i].Key, inject.Key, StringComparison.Ordinal))
                .ToList();
            if (matches.Count == 0)
                throw Fail($"inject key '{inject.Key}' does not match any preamble entry.");

            if (injectBytes.Any(b => b is < 0 or > 255))
                throw Fail($"inject for key '{inject.Key}' has a byte value outside 0..255.");

            foreach (var index in matches)
            {
                if (inject.Offset < 0 || inject.Offset + injectBytes.Count > keyBytes[index].Length)
                    throw Fail($"inject offset {inject.Offset} with {injectBytes.Count} byte(s) does not fit inside key '{inject.Key}' ({keyBytes[index].Length} bytes).");

                for (var i = 0; i < injectBytes.Count; i++)
                    keyBytes[index][inject.Offset + i] = (byte)injectBytes[i];
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

    private static byte[] PreambleValue(PreambleEntrySpec entry, string key, string field)
    {
        switch (Normalize(entry.Kind))
        {
            case "static":
                RejectSeparators(entry.Value, field + ".value");
                return Encoding.UTF8.GetBytes(entry.Value ?? string.Empty);
            case "date":
                if (entry.Value is null || !DataGenerator.TryParseDate(entry.Value, out var date))
                    throw Fail($"preamble entry '{key}' needs a valid date as its value (ISO 8601), not '{entry.Value}'.");

                try
                {
                    var format = string.IsNullOrEmpty(entry.Format) ? "yyyy-MM-dd HH:mm:ss" : entry.Format;
                    var printed = date.ToString(format, CultureInfo.InvariantCulture);
                    RejectSeparators(printed, field + ".format");
                    return Encoding.UTF8.GetBytes(printed);
                }
                catch (FormatException)
                {
                    throw Fail($"preamble entry '{key}' has an invalid date format '{entry.Format}'.");
                }
            case "counter":
                // Step is kept for future appended blocks; one preamble prints only the start.
                return Encoding.UTF8.GetBytes(entry.Start.ToString(CultureInfo.InvariantCulture));
            default:
                throw Fail($"preamble entry '{key}' kind '{entry.Kind}' is not supported. Expected one of: static, date, counter.");
        }
    }

    private void BuildDeviceColumns(DeviceColumnsSpec devices)
    {
        if (devices.Serial is { } serial)
        {
            RejectSeparators(serial.Name, "deviceColumns.serial.name");
            var column = new DeviceColumn(DeviceKind.Serial, serial.Name, CheckWidth(serial.Name, serial.Width))
            {
                Start = serial.Start,
                Step = serial.Step
            };
            CheckNameFits(column);
            // Nothing is printed under the header without devices, so only the header has to fit then.
            if (_devices > 0)
            {
                // Serials are linear in the device index, so the widest one is the first or the last.
                CheckFits($"the serial value {column.Start}", FormatLongLength(column.Start), column.Width, column.Name);
                // The sum can fit a long when the step times the index alone does not, so it is worked out wide.
                var wideLast = (Int128)column.Start + (Int128)column.Step * (_devices - 1);
                if (wideLast < long.MinValue || wideLast > long.MaxValue)
                    throw Fail($"deviceColumns.serial.start {column.Start} and deviceColumns.serial.step {column.Step} overflow a 64-bit integer by the last device (number {_devices}).");

                var last = (long)wideLast;
                CheckFits($"the serial value {last}", FormatLongLength(last), column.Width, column.Name);
            }

            _deviceColumns.Add(column);
        }

        var siteRange = RangeWidth("site", devices.Site);
        var xRange = RangeWidth("proberX", devices.ProberX);
        var yRange = RangeWidth("proberY", devices.ProberY);
        // With both prober columns ranged the devices walk a raster: X is the fast axis and Y steps once per X sweep.
        var raster = xRange.HasValue && yRange.HasValue;
        var columns = new[]
        {
            ("site", devices.Site, siteRange, UInt128.One),
            ("proberX", devices.ProberX, xRange, UInt128.One),
            ("proberY", devices.ProberY, yRange, raster ? xRange!.Value : UInt128.One)
        };

        foreach (var (key, fixedColumn, rangeWidth, stride) in columns)
        {
            if (fixedColumn is null)
                continue;

            RejectSeparators(fixedColumn.Name, $"deviceColumns.{key}.name");
            RejectSeparators(fixedColumn.Value, $"deviceColumns.{key}.value");
            if (rangeWidth.HasValue)
            {
                _deviceColumns.Add(BuildRangeColumn(key, fixedColumn, rangeWidth.Value, stride));
                continue;
            }

            var column = new DeviceColumn(DeviceKind.Fixed, fixedColumn.Name, CheckWidth(fixedColumn.Name, fixedColumn.Width))
            {
                Fixed = Encoding.UTF8.GetBytes(fixedColumn.Value ?? string.Empty)
            };
            CheckNameFits(column);
            CheckFits($"the fixed value '{fixedColumn.Value}'", column.Fixed.Length, column.Width, column.Name);
            _deviceColumns.Add(column);
        }

        if (devices.Bin is { } bin)
            _deviceColumns.Add(BuildBinColumn(bin));
    }

    // Validates the value/range choice of a fixed or prober column and returns the number of positions its range spans
    // (up to 2^64), or null when it has no range.
    private static UInt128? RangeWidth(string key, FixedColumnSpec? column)
    {
        if (column?.Range is not { } range)
            return null;

        var field = $"deviceColumns.{key}";
        if (key == "site")
            throw Fail($"{field}.range is not supported: only proberX and proberY take a range.");

        if (column.Value is not null)
            throw Fail($"{field} has both a value and a range; give one of them.");

        if (!range.Min.HasValue || !range.Max.HasValue)
            throw Fail($"{field}.range needs both a min and a max.");

        if (range.Min > range.Max)
            throw Fail($"{field}.range min {range.Min} is above its max {range.Max}.");

        return (UInt128)((Int128)range.Max.Value - range.Min.Value) + UInt128.One;
    }

    // A prober column that walks min..max. Stride is how many devices share one position: 1, or the width of the X range
    // for Y in a raster.
    private DeviceColumn BuildRangeColumn(string key, FixedColumnSpec spec, UInt128 count, UInt128 stride)
    {
        var min = spec.Range!.Min!.Value;
        var column = new DeviceColumn(DeviceKind.Range, spec.Name, CheckWidth(spec.Name, spec.Width))
        {
            RangeMin = min,
            RangeCount = count,
            RangeStride = stride
        };
        CheckNameFits(column);
        if (_devices > 0)
        {
            // Positions advance in order and wrap, so the printed values are min up to the last position reached.
            var reached = UInt128.Min((UInt128)(_devices - 1) / stride + UInt128.One, count);
            var last = (long)((Int128)min + (Int128)(reached - UInt128.One));
            CheckFits($"the {key} value {min}", FormatLongLength(min), column.Width, column.Name);
            CheckFits($"the {key} value {last}", FormatLongLength(last), column.Width, column.Name);
        }

        return column;
    }

    private static void CheckNameFits(DeviceColumn column)
        => CheckFits("the column name", column.NameBytes.Length, column.Width, column.Name);

    // Same width rule as OutputBuffer.CheckFits, applied at validation to content known before any row is drawn.
    private static void CheckFits(string what, int length, int width, string column)
    {
        if (length > width)
            throw Fail($"{what} ({length} bytes) in column '{column}' exceeds its width of {width}.");
    }

    private static int FormatLongLength(long value) => value.ToString(CultureInfo.InvariantCulture).Length;

    private static int CheckWidth(string name, int width)
    {
        if (width < 0)
            throw Fail($"device column '{name}' width must not be negative.");
        return width;
    }

    private static DeviceColumn BuildBinColumn(BinColumnSpec bin)
    {
        RejectSeparators(bin.Name, "deviceColumns.bin.name");
        var width = CheckWidth(bin.Name, bin.Width);
        var weights = Required(bin.Weights, "deviceColumns.bin.weights");
        if (weights.Count == 0)
            throw Fail($"bin column '{bin.Name}' weights must not be empty.");

        // Ordinal key order, so the draw does not depend on how the JSON listed the bins.
        var keys = weights.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var cumulative = new double[keys.Length];
        var total = 0.0;
        for (var i = 0; i < keys.Length; i++)
        {
            RejectSeparators(keys[i], "deviceColumns.bin.weights");
            var weight = weights[keys[i]];
            if (!double.IsFinite(weight) || weight < 0)
                throw Fail($"bin column '{bin.Name}' weight for bin '{keys[i]}' must be a finite number that is not negative.");

            total += weight;
            cumulative[i] = total;
        }

        if (total <= 0)
            throw Fail($"bin column '{bin.Name}' weights must add up to more than 0.");

        var column = new DeviceColumn(DeviceKind.Bin, bin.Name, width)
        {
            BinValues = keys.Select(k => Encoding.UTF8.GetBytes(k)).ToArray(),
            CumulativeWeights = cumulative,
            TotalWeight = total
        };
        CheckNameFits(column);
        for (var i = 0; i < keys.Length; i++)
            CheckFits($"the bin value '{keys[i]}'", column.BinValues[i].Length, width, column.Name);

        return column;
    }

    private void BuildTestColumns(TestResultsSpec spec)
    {
        long columnCount = 0;
        var sets = RequiredItems(spec.ConditionSets, "conditionSets");
        for (var setIndex = 0; setIndex < sets.Count; setIndex++)
        {
            var set = sets[setIndex];
            var setField = $"conditionSets[{setIndex}]";
            var setTests = RequiredItems(set.Tests, setField + ".tests");
            var tests = new List<TestTemplate>(setTests.Count);
            for (var t = 0; t < setTests.Count; t++)
                tests.Add(BuildTestTemplate(setTests[t], $"{setField}.tests[{t}]"));

            var conditionSets = ExpandConditionSet(set, setField);

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

    private static List<List<(string Key, string Value)>> ExpandConditionSet(ConditionSetSpec set, string field)
    {
        if (set.Conditions is not null && set.Axes is not null)
            throw Fail("a condition set must define either conditions or axes, not both.");

        if (set.Conditions is not null)
        {
            // keyOrder only orders the keys of axes; with explicit conditions it would silently do nothing.
            if (set.KeyOrder is not null)
                throw Fail($"{field} has explicit conditions and a keyOrder; keyOrder only orders axes, so remove it.");

            var pairs = new List<(string, string)>();
            var conditions = RequiredItems(set.Conditions, field + ".conditions");
            for (var p = 0; p < conditions.Count; p++)
            {
                var pair = RequiredItems(conditions[p], $"{field}.conditions[{p}]");
                if (pair.Count != 2)
                    throw Fail($"{field}.conditions[{p}] must be a [key, value] pair.");

                RejectSeparators(pair[0], $"{field}.conditions[{p}] key");
                RejectSeparators(pair[1], $"{field}.conditions[{p}] value");
                pairs.Add((pair[0], pair[1]));
            }

            return [pairs];
        }

        if (set.Axes is null)
            throw Fail("a condition set must define conditions or axes.");

        if (set.Axes.Count == 0)
            throw Fail("a condition set's axes must not be empty.");

        var axes = RequiredItems(set.Axes, field + ".axes");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long combinations = 1;
        for (var a = 0; a < axes.Count; a++)
        {
            var axis = axes[a];
            var axisField = $"{field}.axes[{a}]";
            if (string.IsNullOrEmpty(axis.Key))
                throw Fail("an axis key must not be empty.");

            RejectSeparators(axis.Key, axisField + ".key");
            if (!seen.Add(axis.Key))
                throw Fail($"duplicate axis key '{axis.Key}'.");

            var values = RequiredItems(axis.Values, axisField + ".values");
            if (values.Count == 0)
                throw Fail($"axis '{axis.Key}' has no values.");

            for (var v = 0; v < values.Count; v++)
                RejectSeparators(values[v], $"{axisField}.values[{v}]");

            combinations *= values.Count;
            if (combinations > Array.MaxLength)
                throw Fail($"the axes expand to more than {Array.MaxLength} condition sets.");
        }

        var order = Enumerable.Range(0, axes.Count).ToArray();
        if (set.KeyOrder is not null)
        {
            var keyOrder = RequiredItems(set.KeyOrder, field + ".keyOrder");
            var indexByKey = axes.Select((a, i) => (a.Key, i)).ToDictionary(t => t.Key, t => t.i, StringComparer.Ordinal);
            if (keyOrder.Count != axes.Count ||
                keyOrder.Distinct(StringComparer.Ordinal).Count() != keyOrder.Count ||
                !keyOrder.All(indexByKey.ContainsKey))
            {
                throw Fail("keyOrder must list each axis key exactly once.");
            }

            order = keyOrder.Select(k => indexByKey[k]).ToArray();
        }

        // First axis is the outermost loop, whatever the key order inside the brackets.
        var result = new List<List<(string Key, string Value)>>((int)combinations);
        var counter = new int[axes.Count];
        for (long n = 0; n < combinations; n++)
        {
            result.Add(order.Select(a => (axes[a].Key, axes[a].Values[counter[a]])).ToList());

            for (var a = axes.Count - 1; a >= 0; a--)
            {
                if (++counter[a] < axes[a].Values.Count)
                    break;
                counter[a] = 0;
            }
        }

        return result;
    }

    private static TestTemplate BuildTestTemplate(TestSpec test, string field)
    {
        if (string.IsNullOrEmpty(test.Name))
            throw Fail("a test name must not be empty.");

        var name = test.Name;
        RejectSeparators(name, field + ".name");
        RejectSeparators(test.Unit, field + ".unit");
        RejectSeparators(test.NullToken, field + ".nullToken");
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

        foreach (var (rateField, rate) in new[] { ("nullRate", test.NullRate), ("outOfLimitRate", test.OutOfLimitRate) })
        {
            if (!double.IsFinite(rate) || rate < 0 || rate > PercentMax)
                throw Fail($"test '{name}' {rateField} must be between 0 and 100.");
        }

        if (test.OutOfLimitRate > 0 && !test.UpperLimit.HasValue && !test.LowerLimit.HasValue)
            throw Fail($"test '{name}' outOfLimitRate needs an upperLimit or a lowerLimit.");

        var format = new NumberFormat(match.Groups[2].Value == "e", int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));

        // At a rate of 100 no cell needs a value inside the limits, so neither check applies.
        var someCellInsideLimits = test.NullRate < PercentMax && test.OutOfLimitRate < PercentMax;
        if (someCellInsideLimits && test.UpperLimit.HasValue && test.LowerLimit.HasValue && !format.CanPrintInside(test.LowerLimit.Value, test.UpperLimit.Value))
            throw Fail($"test '{name}' format '{test.Format}' cannot print any value inside its limits {test.LowerLimit.Value.ToString("R", CultureInfo.InvariantCulture)}..{test.UpperLimit.Value.ToString("R", CultureInfo.InvariantCulture)}. Use more digits.");

        if (someCellInsideLimits && (test.UpperLimit.HasValue || test.LowerLimit.HasValue) && ProbabilityPrintsInsideLimits(test, format) < MinInsideLimitsProbability)
            throw Fail($"test '{name}' distribution prints inside its limits less than {MinInsideLimitsProbability:P0} of the time under format '{test.Format}', so most values would be drawn again and again.");

        template.Format = format;
        return template;
    }

    // Chance that one draw prints inside the limits: the mass of the raw values that round onto a printed value inside
    // them, which is not the mass inside the limits themselves. Distribution fields are valid here.
    private static double ProbabilityPrintsInsideLimits(TestSpec test, NumberFormat format)
    {
        var lower = test.LowerLimit ?? double.NegativeInfinity;
        var upper = test.UpperLimit ?? double.PositiveInfinity;
        var uniform = Normalize(test.Distribution) == "uniform";
        // Every draw is the same value, so print it and look.
        var point = uniform
            ? test.Min == test.Max ? test.Min : null
            : test.StdDev == 0 ? test.Mean : null;
        if (point.HasValue)
        {
            var printed = format.Rounded(point.Value);
            return printed >= lower && printed <= upper ? 1.0 : 0.0;
        }

        format.PrintedEdges(test.LowerLimit, test.UpperLimit, out var low, out var high);
        if (uniform)
        {
            var min = test.Min!.Value;
            var max = test.Max!.Value;
            return Math.Max(0.0, Math.Min(max, high) - Math.Max(min, low)) / (max - min);
        }

        var mean = test.Mean!.Value;
        var stdDev = test.StdDev!.Value;
        return Math.Max(0.0, NormalCdf((high - mean) / stdDev) - NormalCdf((low - mean) / stdDev));
    }

    private static double NormalCdf(double z)
    {
        if (double.IsPositiveInfinity(z))
            return 1.0;
        if (double.IsNegativeInfinity(z))
            return 0.0;

        // Abramowitz and Stegun 7.1.26, absolute error below 1.5e-7: plenty for a threshold check.
        var x = Math.Abs(z) / Math.Sqrt(2.0);
        var t = 1.0 / (1.0 + 0.3275911 * x);
        var poly = t * (0.254829592 + t * (-0.284496736 + t * (1.421413741 + t * (-1.453152027 + t * 1.061405429))));
        var erf = 1.0 - poly * Math.Exp(-x * x);
        return 0.5 * (1.0 + (z < 0 ? -erf : erf));
    }

    private void BuildAnnotationRows(TestResultsSpec spec)
    {
        var annotations = RequiredItems(spec.Annotations, "annotations");
        if (annotations.Count == 0)
            return;

        if (_deviceColumns.Count == 0)
            throw Fail("annotations need at least one device column to hold their labels.");

        var first = _deviceColumns[0];
        for (var a = 0; a < annotations.Count; a++)
        {
            var annotation = annotations[a];
            RejectSeparators(annotation.Label, $"annotations[{a}].label");
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

            if (source == AnnotationSource.ProgramLine)
                CheckProgramLineFits(spec);

            _annotationRows.Add(new AnnotationRow(label, source, spec.ProgramLineStart, spec.ProgramLineStep));
        }
    }

    // Program lines are linear in the column index, so if the last one fits a long, they all do.
    private void CheckProgramLineFits(TestResultsSpec spec)
    {
        var last = (Int128)spec.ProgramLineStart + (Int128)spec.ProgramLineStep * (_testColumns.Count - 1);
        if (last < long.MinValue || last > long.MaxValue)
            throw Fail($"programLineStart {spec.ProgramLineStart} and programLineStep {spec.ProgramLineStep} overflow a 64-bit integer by the last test column (number {_testColumns.Count}).");
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

    // Content that is fixed once the config is read: a value drawn at generation time is covered by OutputBuffer.CheckFits.
    private void CheckFixedTestCellsFit()
    {
        Span<byte> scratch = stackalloc byte[ScratchSize];
        for (var i = 0; i < _testColumns.Count; i++)
        {
            var column = _testColumns[i];
            // The null token is only written when a cell can be null.
            if (column.NullRate > 0)
                CheckFits($"the null token '{Encoding.UTF8.GetString(column.NullToken)}'", column.NullToken.Length, column.Width, column.Header);

            foreach (var row in _annotationRows)
            {
                switch (row.Source)
                {
                    case AnnotationSource.ProgramLine:
                        var line = FormatLongLength(row.ProgramLineStart + row.ProgramLineStep * i);
                        CheckFits($"the programLine value of annotation '{row.LabelText}'", line, column.Width, column.Header);
                        break;
                    case AnnotationSource.UpperLimit:
                        CheckLimitFits(column, row, column.Upper, scratch);
                        break;
                    case AnnotationSource.LowerLimit:
                        CheckLimitFits(column, row, column.Lower, scratch);
                        break;
                }
            }
        }
    }

    private static void CheckLimitFits(TestColumn column, AnnotationRow row, double? limit, Span<byte> scratch)
    {
        if (limit.HasValue)
            CheckFits($"the printed limit of annotation '{row.LabelText}'", column.FormatNumber(limit.Value, scratch), column.Width, column.Header);
    }

    // A JSON null for a list or object that is not optional. Optional objects and lists (preamble, a device column,
    // conditions, axes, keyOrder) treat null as absent and do not go through here.
    private static T Required<T>(T? value, string field) where T : class
        => value ?? throw Fail($"{field} must not be null.");

    private static List<T> RequiredItems<T>(List<T>? list, string field) where T : class
    {
        var items = Required(list, field);
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is null)
                throw Fail($"{field}[{i}] must not be null.");
        }

        return items;
    }

    // The file is comma separated with CRLF or LF line ends and nothing is quoted, so these would shift or split a row.
    // Stray bytes from an explicit inject are the one exception and do not pass through here.
    private static void RejectSeparators(string? value, string field)
    {
        if (value is not null && value.AsSpan().IndexOfAny(',', '\r', '\n') >= 0)
            throw Fail($"{field} must not contain a comma, CR or LF.");
    }

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static InvalidOperationException Fail(string message) => new("testResults: " + message);

    // ---- types ----

    private enum DeviceKind
    {
        Serial,
        Fixed,
        Range,
        Bin
    }

    private enum AnnotationSource
    {
        ProgramLine,
        UpperLimit,
        LowerLimit,
        Unit
    }

    private sealed record AnnotationRow(byte[] Label, AnnotationSource Source, long ProgramLineStart, long ProgramLineStep)
    {
        public string LabelText => Encoding.UTF8.GetString(Label);
    }

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
        // A ranged prober column prints RangeMin + (device / RangeStride) % RangeCount.
        public long RangeMin { get; init; }
        public UInt128 RangeCount { get; init; }
        public UInt128 RangeStride { get; init; } = UInt128.One;
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

        // True when some value this format prints lies inside lower..upper, limits included. The printed value nearest
        // the lower limit is the first candidate; when it falls short of the limit, the next printed values above it are
        // tried (one step, and one tenth of a step for the finer grid just below a power of ten).
        public bool CanPrintInside(double lower, double upper)
        {
            Span<byte> scratch = stackalloc byte[ScratchSize];
            var nearest = RoundTrip(lower, scratch);
            var step = Step(nearest);
            foreach (var candidate in new[] { nearest, nearest + step, nearest + step / 10 })
            {
                var printed = RoundTrip(candidate, scratch);
                if (printed >= lower && printed <= upper)
                    return true;
            }

            return false;
        }

        // The value a reader sees after printing: the nearest printed value.
        public double Rounded(double value)
        {
            Span<byte> scratch = stackalloc byte[ScratchSize];
            return RoundTrip(value, scratch);
        }

        // Raw values that print inside lower..upper (either may be absent) lie between low and high: each edge is halfway
        // from the outermost printed value inside the limits to its neighbour outside. Low can exceed high when no printed
        // value lies inside. A limit that does not round to a finite value keeps itself as its edge.
        public void PrintedEdges(double? lower, double? upper, out double low, out double high)
        {
            low = double.NegativeInfinity;
            high = double.PositiveInfinity;
            Span<byte> scratch = stackalloc byte[ScratchSize];
            if (lower.HasValue)
            {
                var printed = RoundTrip(lower.Value, scratch);
                if (printed < lower.Value)
                    printed = RoundTrip(printed + Gap(printed, true, scratch), scratch);

                low = double.IsFinite(printed) ? printed - Gap(printed, false, scratch) / 2 : lower.Value;
            }

            if (upper.HasValue)
            {
                var printed = RoundTrip(upper.Value, scratch);
                if (printed > upper.Value)
                    printed = RoundTrip(printed - Gap(printed, false, scratch), scratch);

                high = double.IsFinite(printed) ? printed + Gap(printed, true, scratch) / 2 : upper.Value;
            }
        }

        private double RoundTrip(double value, Span<byte> scratch)
            => double.Parse(scratch[..Format(value, scratch)], NumberStyles.Float, CultureInfo.InvariantCulture);

        // Distance from a printed value to its neighbour above or below. With an exponent the grid gets ten times finer
        // below a power of ten, so the neighbour toward zero of one is closer.
        private double Gap(double printed, bool up, Span<byte> scratch)
        {
            if (!Exponent)
                return Math.Pow(10, -Precision);

            if (printed == 0 || !double.IsFinite(printed))
                return 0;

            var length = Format(printed, scratch);
            var exponent = int.Parse(scratch[(scratch[..length].IndexOf((byte)'e') + 1)..length], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            var step = PowerOfTen(exponent - Precision);
            var towardZero = up == (printed < 0);
            return towardZero && Math.Abs(printed) == PowerOfTen(exponent) ? step / 10 : step;
        }

        private static double PowerOfTen(int exponent)
            => double.Parse("1e" + exponent.ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);

        // Distance between neighbouring printed values at this magnitude.
        private double Step(double value)
        {
            if (!Exponent)
                return Math.Pow(10, -Precision);

            return value == 0 || !double.IsFinite(value) ? 0 : Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(value))) - Precision);
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
        public bool HasLimits => Upper.HasValue || Lower.HasValue;
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
