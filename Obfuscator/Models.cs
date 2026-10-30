// SPDX-License-Identifier: MPL-2.0
/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Squalor.Obfuscator;

public sealed class DataGenConfig
{
    // Used by rowMode=fixed, and as the target minimum row count for rowMode=max.
    // For rowMode=test-results it is the device count (one data row per device).
    public int NRows { get; set; }

    // Optional seed to make synthetic generation reproducible.
    public int? Seed { get; set; }

    // fixed: exactly NRows
    // sweep: exactly the cartesian expansion of SweepAxes
    // max: at least NRows, repeating the sweep pattern if needed
    // test-results: a wide production test-results file described by TestResults (Columns and SweepAxes stay empty)
    public string RowMode { get; set; } = "fixed";

    // Full CSV schema, including sweep columns and any generated data columns.
    public List<ColumnGenerationSpec> Columns { get; set; } = new();

    // Ordered from outermost to innermost loop for rowMode=sweep or rowMode=max.
    public List<SweepAxisSpec> SweepAxes { get; set; } = new();

    // Required for rowMode=test-results and rejected for every other mode.
    public TestResultsSpec? TestResults { get; set; }
}

// Wide test-results file: optional preamble, optional annotation rows, a header, then one row per device.
public sealed class TestResultsSpec
{
    // crlf or lf. Every line, including the last, ends with it.
    public string LineEnding { get; set; } = "crlf";

    public PreambleSpec? Preamble { get; set; }

    // Rows between the preamble and the header, in this order.
    public List<AnnotationSpec> Annotations { get; set; } = new();

    // The programLine annotation is ProgramLineStart + ProgramLineStep * (test column index, from 0).
    public long ProgramLineStart { get; set; } = 1;
    public long ProgramLineStep { get; set; } = 1;

    // Each device column is optional. Order in the file: serial, site, proberX, proberY, bin, then the test columns.
    public DeviceColumnsSpec DeviceColumns { get; set; } = new();

    // Each test column is as wide as its header (in UTF-8 bytes) plus this padding.
    public int TestColumnPadding { get; set; } = 4;

    // Test columns in order: condition set, then test within the set.
    public List<ConditionSetSpec> ConditionSets { get; set; } = new();
}

public sealed class PreambleSpec
{
    // Keys are space-padded to this many bytes before ":,". A longer key gets no padding.
    public int KeyWidth { get; set; }

    public List<PreambleEntrySpec> Entries { get; set; } = new();

    // Opt-in and off by default (empty): overwrites bytes of a key to simulate a corrupted key.
    // Without it every preamble line is clean text.
    public List<PreambleInjectSpec> Inject { get; set; } = new();
}

public sealed class PreambleEntrySpec
{
    public string Key { get; set; } = string.Empty;

    // static: Value as is.
    // date: Value is an ISO 8601 date or date-time (read as UTC), printed with Format (default yyyy-MM-dd HH:mm:ss).
    // counter: prints Start. Step is kept for future appended blocks and does not change the printed value.
    public string Kind { get; set; } = "static";
    public string? Value { get; set; }
    public string? Format { get; set; }
    public long Start { get; set; } = 1;
    public long Step { get; set; } = 1;
}

public sealed class PreambleInjectSpec
{
    // Must match an entry key.
    public string Key { get; set; } = string.Empty;

    // Byte offset inside the padded key; the bytes replace the same number of bytes from here.
    public int Offset { get; set; }

    // Raw byte values 0..255, for example 255 (invalid UTF-8) or 19 (control character).
    public List<int> Bytes { get; set; } = new();
}

public sealed class AnnotationSpec
{
    // Padded so that Label plus ":" fills the first device column.
    public string Label { get; set; } = string.Empty;

    // programLine, upperLimit, lowerLimit or unit. Device columns are blank.
    public string Source { get; set; } = string.Empty;
}

public sealed class DeviceColumnsSpec
{
    public SerialColumnSpec? Serial { get; set; }
    public FixedColumnSpec? Site { get; set; }
    public FixedColumnSpec? ProberX { get; set; }
    public FixedColumnSpec? ProberY { get; set; }
    public BinColumnSpec? Bin { get; set; }
}

public sealed class SerialColumnSpec
{
    public string Name { get; set; } = "Serial";
    public int Width { get; set; } = 12;
    public long Start { get; set; } = 1;
    public long Step { get; set; } = 1;
}

public sealed class FixedColumnSpec
{
    public string Name { get; set; } = string.Empty;
    public int Width { get; set; } = 6;

    // The same text on every row; absent prints an empty cell. Give Value or Range, not both.
    public string? Value { get; set; }

    // proberX and proberY only. Both ranged: the devices walk a raster, X from min to max (the fast axis), then Y steps
    // by 1 and X restarts, wrapping to the start once the grid is used up. One ranged: it cycles min..max per device and
    // the other prober column keeps its value. No randomness.
    public ProberRangeSpec? Range { get; set; }
}

// Inclusive bounds of a prober coordinate. Both are required.
public sealed class ProberRangeSpec
{
    public long? Min { get; set; }
    public long? Max { get; set; }
}

public sealed class BinColumnSpec
{
    public string Name { get; set; } = "Bin";
    public int Width { get; set; } = 6;

    // Bin value to relative weight. Bins are drawn independently of the test values.
    public Dictionary<string, double> Weights { get; set; } = new();
}

// Either Conditions (one condition set) or Axes (every combination of the axes' values), not both.
public sealed class ConditionSetSpec
{
    // [[key, value], ...] in header order.
    public List<List<string>>? Conditions { get; set; }

    // The first axis is the outermost loop. KeyOrder (a permutation of the axis keys) sets the order inside the brackets.
    public List<ConditionAxisSpec>? Axes { get; set; }
    public List<string>? KeyOrder { get; set; }

    // Measured at every condition set this entry expands to.
    public List<TestSpec> Tests { get; set; } = new();
}

public sealed class ConditionAxisSpec
{
    public string Key { get; set; } = string.Empty;
    public List<string> Values { get; set; } = new();
}

public sealed class TestSpec
{
    public string Name { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public double? UpperLimit { get; set; }
    public double? LowerLimit { get; set; }

    // uniform (Min, Max) or normal (Mean, StdDev). A value whose printed form falls outside the limits is drawn again
    // (up to 10,000 times, then generation fails), never clamped onto a limit. Validation rejects a distribution
    // that prints inside the limits less than 1% of the time, judged on the print format's grid and not on raw values.
    public string Distribution { get; set; } = "uniform";
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Mean { get; set; }
    public double? StdDev { get; set; }

    // printf-style %.Nf or %.Ne. Also used for the limit annotation rows.
    public string Format { get; set; } = "%.6f";

    // NullRate: percent (0..100) of cells that hold NullToken.
    // OutOfLimitRate: percent (0..100) of the non-null cells drawn just outside the nearer limit; with equal limits the
    // side is drawn at random.
    public double NullRate { get; set; }
    public string NullToken { get; set; } = "NA.";
    public double OutOfLimitRate { get; set; }

    // caseVariant: the test name in the opposite case. repeatUnit: the header reads Test(V)(V).
    public string? Quirk { get; set; }
}

public sealed class SweepAxisSpec
{
    // Must match a column name in Columns and that column must provide discrete Values.
    public string Name { get; set; } = string.Empty;
}

public sealed class ColumnGenerationSpec
{
    // Name is written directly into the generated CSV header.
    public string Name { get; set; } = string.Empty;

    // Informational only. Some configs may choose to put units directly in Name instead.
    public string? Units { get; set; }

    // Supported broad types: string, bool/boolean, date/datetime/datetimeoffset, guid,
    // and the numeric integer/floating types handled by IsNumeric/IsInteger.
    public string DataType { get; set; } = "string";

    // String generation options:
    // - StaticValue wins first
    // - then discrete Values for sweep/pick-list style configs
    // - then RandomString/StringPrefix/RandomStringLength if enabled
    public string? StaticValue { get; set; }
    public bool RandomString { get; set; }
    public int RandomStringLength { get; set; } = 12;
    public string? StringPrefix { get; set; }

    // Discrete values used for sweep expansion or picking from a fixed set.
    public List<string> Values { get; set; } = new();

    // Optional sweep-aware generated ID behavior:
    // - outer-group: one ID per outer sweep group
    // - inner-step: 1-based counter over the innermost sweep axis
    public string? GeneratedIdMode { get; set; }

    // Informational metadata. The generator does not use these fields directly.
    public string? GoodDirection { get; set; }
    public string? Info { get; set; }

    // Boolean generation uses this as the approximate percent chance of "true".
    public double? TruePercentage { get; set; }

    // Numeric generation options:
    // - TotalRangeMin/Max define the hard bounds
    // - IdealRangeMin/Max define the region used more often
    // - TracksWith/TracksInverselyWith create loose correlations to earlier numeric columns
    public double? IdealRangeMin { get; set; }
    public double? IdealRangeMax { get; set; }
    public List<string> TracksWith { get; set; } = new();
    public List<string> TracksInverselyWith { get; set; } = new();
    public double? TotalRangeMin { get; set; }
    public double? TotalRangeMax { get; set; }

    // Linear is the default numeric distribution. Logarithmic skews toward lower normalized values.
    public bool IsLinear { get; set; } = true;
    public bool IsLogarithmic { get; set; }

    // Rough chance that a generated numeric value lands in the ideal range instead of the outer tails.
    public double? PercentageInIdealRange { get; set; }

    // Date generation options for date/datetime/datetimeoffset columns.
    public string? DateMinUtc { get; set; }
    public string? DateMaxUtc { get; set; }
    public string? DateFormat { get; set; }

    [JsonIgnore]
    public bool IsNumeric =>
        DataType.Equals("double", StringComparison.OrdinalIgnoreCase) ||
        DataType.Equals("float", StringComparison.OrdinalIgnoreCase) ||
        DataType.Equals("decimal", StringComparison.OrdinalIgnoreCase) ||
        DataType.Equals("int", StringComparison.OrdinalIgnoreCase) ||
        DataType.Equals("long", StringComparison.OrdinalIgnoreCase) ||
        DataType.Equals("bigint", StringComparison.OrdinalIgnoreCase) ||
        DataType.Equals("short", StringComparison.OrdinalIgnoreCase) ||
        DataType.Equals("byte", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsInteger =>
        DataType.Equals("int", StringComparison.OrdinalIgnoreCase) ||
        DataType.Equals("long", StringComparison.OrdinalIgnoreCase) ||
        DataType.Equals("bigint", StringComparison.OrdinalIgnoreCase) ||
        DataType.Equals("short", StringComparison.OrdinalIgnoreCase) ||
        DataType.Equals("byte", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsBoolean =>
        DataType.Equals("bool", StringComparison.OrdinalIgnoreCase) ||
        DataType.Equals("boolean", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsString =>
        DataType.Equals("string", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsDateTime =>
        DataType.Equals("datetime", StringComparison.OrdinalIgnoreCase) ||
        DataType.Equals("datetimeoffset", StringComparison.OrdinalIgnoreCase) ||
        DataType.Equals("date", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsGuid =>
        DataType.Equals("guid", StringComparison.OrdinalIgnoreCase);
}

public sealed class ObfuscationOptions
{
    // Optional seed for non-deterministic transform generation.
    public int? Seed { get; set; }

    // If provided, the manifest is AES-GCM-encrypted on disk. This does not encrypt the CSV.
    public string? Passphrase { get; set; }

    // Optional GPG recipients for encrypting the written manifest file in place.
    public List<string> GpgRecipients { get; set; } = new();

    // If true, blank cells are left untouched even on obfuscated columns.
    public bool PreserveBlanks { get; set; } = true;

    // If set, deterministic transforms are derived from this key and the per-manifest salt.
    // This is not a guarantee of secrecy; deterministic tokens preserve equality patterns.
    public string? DeterministicKey { get; set; }

    // Column filtering:
    // - with an empty IncludeColumns list, all columns are obfuscated except ExcludeColumns
    // - with UseIncludeListAsAllowList=true, only explicitly included columns are obfuscated
    public List<string> IncludeColumns { get; set; } = new();
    public List<string> ExcludeColumns { get; set; } = new();

    // If true, only columns explicitly included are obfuscated.
    public bool UseIncludeListAsAllowList { get; set; }

    // When true, a value that does not parse as the inferred column kind throws instead of
    // falling back to the string strategy.
    public bool Strict { get; set; }

    // When true, overwrite an existing output CSV or manifest. Default refuses.
    public bool Force { get; set; }

    // String columns can use explicit mapping, deterministic tokens, or auto mode based on DeterministicKey.
    public StringObfuscationMode StringMode { get; set; } = StringObfuscationMode.Auto;
}

public enum StringObfuscationMode
{
    Auto,
    Mapping,
    DeterministicToken
}

public enum ObfuscatedColumnKind
{
    Empty,
    String,
    Boolean,
    Integer,
    Floating,
    DateTime
}

public sealed class ObfuscationManifest
{
    // Version of the manifest schema, not the package version.
    // 2.1: deterministic-token columns record TokenScheme (AES-SIV). 2.0 manifests have none (legacy CBC tokens).
    // 2.2: floating columns record FloatScheme (exact decimal). Older manifests have none (approximate double transform).
    public string Version { get; set; } = "2.2";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string SourceFileName { get; set; } = string.Empty;
    public string ObfuscatedFileName { get; set; } = string.Empty;

    public bool PreserveBlanks { get; set; } = true;

    // CSV field delimiter detected on obfuscate; deobfuscate writes the same one.
    // Null on pre-S14 manifests means comma. Omitted from integrity JSON when null so old checksums still verify.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Delimiter { get; set; }

    public Dictionary<string, int> UnparsedValueCounts { get; set; } = new();

    // Random salt for PBKDF2/HKDF of the deterministic key. Not secret.
    public string Salt { get; set; } = string.Empty;

    // Unkeyed checksum of the manifest (excluding this field). Detects accidental edits, not an attacker.
    public string IntegrityHashSha256 { get; set; } = string.Empty;

    // One spec per CSV column describing how it was transformed.
    public List<ColumnObfuscationSpec> Columns { get; set; } = new();
}

public sealed class ColumnObfuscationSpec
{
    public string Name { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public ObfuscatedColumnKind Kind { get; set; }
    public bool IsObfuscated { get; set; } = true;

    // numeric reversible transform:
    // y = x * Scale + Shift
    public double Scale { get; set; } = 1.0;
    public double Shift { get; set; }

    // Exact floating transform: ExactScale and ExactShift are decimal strings and y is computed without
    // rounding. Null on pre-2.2 manifests means the double transform above, which is only approximate.
    // Omitted from integrity JSON when null so old checksums still verify.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FloatScheme { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExactScale { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExactShift { get; set; }

    // boolean
    public bool InvertBoolean { get; set; }

    // string substitution
    public Dictionary<string, string>? StringMap { get; set; }
    public StringObfuscationMode StringMode { get; set; } = StringObfuscationMode.Auto;

    // Deterministic-token construction. Null on pre-2.1 manifests means legacy AES-CBC tokens,
    // which can only be decrypted. Omitted from integrity JSON when null so old checksums still verify.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TokenScheme { get; set; }

    // datetime reversible transform:
    // obfuscatedTicks = originalTicks + DateShiftTicks
    public long DateShiftTicks { get; set; }

    // original parsing/formatting hints
    public string? DateFormat { get; set; }
    public string? NumericTypeHint { get; set; }

    // Built on demand for mapping-mode string restoration.
    [JsonIgnore]
    public Dictionary<string, string>? ReverseStringMap { get; set; }
}

internal static class HashUtility
{
    public static string Sha256Hex(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}

internal static class ParsingUtility
{
    public static bool TryParseBoolean(string value, out bool result)
        => bool.TryParse(value, out result);

    public static bool TryParseInteger(string value, out long result)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    public static bool TryParseFloating(string value, out double result)
        => double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);

    public static bool TryParseDateTime(string value, out DateTimeOffset result)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
}
