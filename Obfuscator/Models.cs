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
    public int NRows { get; set; }

    // Optional seed to make synthetic generation reproducible.
    public int? Seed { get; set; }

    // fixed: exactly NRows
    // sweep: exactly the cartesian expansion of SweepAxes
    // max: at least NRows, repeating the sweep pattern if needed
    public string RowMode { get; set; } = "fixed";

    // Full CSV schema, including sweep columns and any generated data columns.
    public List<ColumnGenerationSpec> Columns { get; set; } = new();

    // Ordered from outermost to innermost loop for rowMode=sweep or rowMode=max.
    public List<SweepAxisSpec> SweepAxes { get; set; } = new();
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

    // If provided, the manifest is AES-encrypted on disk.
    public string? Passphrase { get; set; }

    // Optional GPG recipients for encrypting the written manifest file in place.
    public List<string> GpgRecipients { get; set; } = new();

    // If true, blank cells are left untouched even on obfuscated columns.
    public bool PreserveBlanks { get; set; } = true;

    // If set, deterministic transforms are derived from this key.
    // Same key + same salt (the salt is per manifest) = same obfuscation behavior.
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
    public string Version { get; set; } = "2.0";
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

    // boolean
    public bool InvertBoolean { get; set; }

    // string substitution
    public Dictionary<string, string>? StringMap { get; set; }
    public StringObfuscationMode StringMode { get; set; } = StringObfuscationMode.Auto;

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
