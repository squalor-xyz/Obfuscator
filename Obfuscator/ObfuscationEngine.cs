using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Squalor.Obfuscator;

internal sealed class ObfuscationEngine
{
    private const string DeterministicTokenPrefix = "OBF_TKN_";
    private const int KindInferenceSampleSize = 100;
    private const int DeterministicTokenIvLengthBytes = 16;
    private const double ShiftRangeHalfWidth = 100000.0;
    private const double ShiftRangeWidth = ShiftRangeHalfWidth * 2.0;
    private const int DateShiftRangeHalfWidthDays = 3650;
    private const int DateShiftRangeWidthDays = DateShiftRangeHalfWidthDays * 2;
    private readonly Random _random;
    private readonly string? _deterministicKey;

    public ObfuscationEngine(int? seed = null, string? deterministicKey = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        _deterministicKey = string.IsNullOrWhiteSpace(deterministicKey) ? null : deterministicKey;
    }

    public ObfuscationManifest Obfuscate(
        string inputCsvPath,
        string outputCsvPath,
        string manifestPath,
        ObfuscationOptions options)
    {
        var headers = CsvStreaming.ReadHeaders(inputCsvPath);

        var manifest = BuildManifest(inputCsvPath, outputCsvPath, headers, options);

        CsvStreaming.TransformCsv(
            inputCsvPath,
            outputCsvPath,
            (_, row) => TransformRowObfuscate(row, manifest, options));

        // Hash the manifest after all derived strategy data is populated so deobfuscation
        // can reject tampering before applying reversible transforms.
        manifest.IntegrityHashSha256 = ComputeIntegrityHash(manifest);

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        ManifestCrypto.WriteManifest(manifestPath, json, options.Passphrase, options.GpgRecipients);

        return manifest;
    }

    public void Deobfuscate(
        string obfuscatedCsvPath,
        string manifestPath,
        string outputCsvPath,
        string? passphrase)
    {
        var manifestJson = ManifestCrypto.ReadManifest(manifestPath, passphrase);
        var manifest = JsonSerializer.Deserialize<ObfuscationManifest>(manifestJson)
            ?? throw new InvalidOperationException("Failed to deserialize manifest.");

        var expectedHash = ComputeIntegrityHash(manifest);
        if (!string.Equals(expectedHash, manifest.IntegrityHashSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Manifest integrity check failed.");

        CsvStreaming.TransformCsv(
            obfuscatedCsvPath,
            outputCsvPath,
            (_, row) => TransformRowDeobfuscate(row, manifest));
    }

    private ObfuscationManifest BuildManifest(
        string inputCsvPath,
        string outputCsvPath,
        IReadOnlyList<string> headers,
        ObfuscationOptions options,
        int inferenceSampleSize = 250)
    {
        var manifest = new ObfuscationManifest
        {
            SourceFileName = Path.GetFileName(inputCsvPath),
            ObfuscatedFileName = Path.GetFileName(outputCsvPath)
        };

        // Type inference is sample-based for speed. Mapping-mode string strategies
        // are filled in from a full scan later because they need the full distinct value set.
        var sampleRows = CsvStreaming.ReadRows(inputCsvPath).Take(inferenceSampleSize).ToList();

        for (var i = 0; i < headers.Count; i++)
        {
            var h = headers[i];
            var values = sampleRows.Select(r => r.TryGetValue(h, out var v) ? v : string.Empty).ToList();
            var spec = CreateColumnSpec(h, i, values, options);
            manifest.Columns.Add(spec);
        }

        PopulateStringStrategies(inputCsvPath, manifest);

        return manifest;
    }

    private ColumnObfuscationSpec CreateColumnSpec(
        string header,
        int ordinal,
        IReadOnlyList<string> values,
        ObfuscationOptions options)
    {
        var kind = InferKind(values);

        var spec = new ColumnObfuscationSpec
        {
            Name = header,
            Ordinal = ordinal,
            Kind = kind,
            IsObfuscated = ShouldObfuscate(header, options)
        };

        if (!spec.IsObfuscated)
            return spec;

        switch (kind)
        {
            case ObfuscatedColumnKind.Boolean:
                spec.InvertBoolean = GetDeterministicBool(header, "invertBoolean");
                break;

            case ObfuscatedColumnKind.Integer:
                spec.Scale = GetDeterministicScale(header);
                spec.Shift = GetDeterministicShift(header);
                spec.NumericTypeHint = "integer";
                break;

            case ObfuscatedColumnKind.Floating:
                spec.Scale = GetDeterministicScale(header);
                spec.Shift = GetDeterministicShift(header);
                spec.NumericTypeHint = "floating";
                break;

            case ObfuscatedColumnKind.DateTime:
                spec.DateShiftTicks = GetDeterministicDateShiftTicks(header);
                spec.DateFormat = GuessDateFormat(values);
                break;

            case ObfuscatedColumnKind.String:
                spec.StringMode = ResolveStringMode(options);
                break;
        }

        return spec;
    }

    private static StringObfuscationMode ResolveStringMode(ObfuscationOptions options)
    {
        return options.StringMode switch
        {
            StringObfuscationMode.Auto when !string.IsNullOrWhiteSpace(options.DeterministicKey) => StringObfuscationMode.DeterministicToken,
            StringObfuscationMode.Auto => StringObfuscationMode.Mapping,
            _ => options.StringMode
        };
    }

    private void PopulateStringStrategies(string inputCsvPath, ObfuscationManifest manifest)
    {
        var mappedSpecs = manifest.Columns
            .Where(spec => spec.IsObfuscated &&
                           spec.Kind == ObfuscatedColumnKind.String &&
                           spec.StringMode == StringObfuscationMode.Mapping)
            .ToList();

        if (mappedSpecs.Count == 0)
            return;

        // Mapping mode has to see the whole file so the manifest can restore every distinct string value.
        var distinctValuesByColumn = mappedSpecs.ToDictionary(
            spec => spec.Name,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.OrdinalIgnoreCase);

        foreach (var row in CsvStreaming.ReadRows(inputCsvPath))
        {
            foreach (var spec in mappedSpecs)
            {
                if (!row.TryGetValue(spec.Name, out var value) || string.IsNullOrWhiteSpace(value))
                    continue;

                distinctValuesByColumn[spec.Name].Add(value);
            }
        }

        foreach (var spec in mappedSpecs)
            spec.StringMap = BuildStringMap(spec.Name, distinctValuesByColumn[spec.Name]);
    }

    private bool ShouldObfuscate(string columnName, ObfuscationOptions options)
    {
        var included = options.IncludeColumns.Any(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase));
        var excluded = options.ExcludeColumns.Any(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase));

        // Allow-list mode is opt-in obfuscation; otherwise include/exclude just narrows the default behavior.
        if (options.UseIncludeListAsAllowList)
            return included && !excluded;

        if (excluded)
            return false;

        if (options.IncludeColumns.Count == 0)
            return true;

        return included;
    }

    private Dictionary<string, string> TransformRowObfuscate(
        IDictionary<string, string> row,
        ObfuscationManifest manifest,
        ObfuscationOptions options)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var spec in manifest.Columns)
        {
            row.TryGetValue(spec.Name, out var value);
            value ??= string.Empty;

            // PreserveBlanks leaves empty cells alone even on obfuscated columns.
            if (!spec.IsObfuscated || (string.IsNullOrWhiteSpace(value) && options.PreserveBlanks))
            {
                result[spec.Name] = value;
                continue;
            }

            result[spec.Name] = ObfuscateValue(value, spec);
        }

        return result;
    }

    private Dictionary<string, string> TransformRowDeobfuscate(
        IDictionary<string, string> row,
        ObfuscationManifest manifest)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var spec in manifest.Columns)
        {
            row.TryGetValue(spec.Name, out var value);
            value ??= string.Empty;

            if (!spec.IsObfuscated || string.IsNullOrWhiteSpace(value))
            {
                result[spec.Name] = value;
                continue;
            }

            result[spec.Name] = DeobfuscateValue(value, spec);
        }

        return result;
    }

    private static ObfuscatedColumnKind InferKind(IReadOnlyList<string> values)
    {
        var sample = values.Where(v => !string.IsNullOrWhiteSpace(v)).Take(KindInferenceSampleSize).ToList();
        if (sample.Count == 0) return ObfuscatedColumnKind.Empty;
        if (sample.All(v => ParsingUtility.TryParseBoolean(v, out _))) return ObfuscatedColumnKind.Boolean;
        if (sample.All(v => ParsingUtility.TryParseInteger(v, out _))) return ObfuscatedColumnKind.Integer;
        if (sample.All(v => ParsingUtility.TryParseFloating(v, out _))) return ObfuscatedColumnKind.Floating;
        if (sample.All(v => ParsingUtility.TryParseDateTime(v, out _))) return ObfuscatedColumnKind.DateTime;
        return ObfuscatedColumnKind.String;
    }

    private string ObfuscateValue(string value, ColumnObfuscationSpec spec)
    {
        return spec.Kind switch
        {
            ObfuscatedColumnKind.Boolean => ObfuscateBoolean(value, spec),
            ObfuscatedColumnKind.Integer => ObfuscateInteger(value, spec),
            ObfuscatedColumnKind.Floating => ObfuscateFloating(value, spec),
            ObfuscatedColumnKind.DateTime => ObfuscateDateTime(value, spec),
            ObfuscatedColumnKind.String => ObfuscateString(value, spec),
            _ => value
        };
    }

    private string DeobfuscateValue(string value, ColumnObfuscationSpec spec)
    {
        return spec.Kind switch
        {
            ObfuscatedColumnKind.Boolean => DeobfuscateBoolean(value, spec),
            ObfuscatedColumnKind.Integer => DeobfuscateInteger(value, spec),
            ObfuscatedColumnKind.Floating => DeobfuscateFloating(value, spec),
            ObfuscatedColumnKind.DateTime => DeobfuscateDateTime(value, spec),
            ObfuscatedColumnKind.String => DeobfuscateString(value, spec),
            _ => value
        };
    }

    private static string ObfuscateBoolean(string value, ColumnObfuscationSpec spec)
    {
        if (!bool.TryParse(value, out var b)) return value;
        return (spec.InvertBoolean ? !b : b) ? "true" : "false";
    }

    private static string DeobfuscateBoolean(string value, ColumnObfuscationSpec spec)
    {
        if (!bool.TryParse(value, out var b)) return value;
        return (spec.InvertBoolean ? !b : b) ? "true" : "false";
    }

    private static string ObfuscateInteger(string value, ColumnObfuscationSpec spec)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return value;
        var y = (n * spec.Scale) + spec.Shift;
        return Math.Round(y).ToString(CultureInfo.InvariantCulture);
    }

    private static string DeobfuscateInteger(string value, ColumnObfuscationSpec spec)
    {
        if (!double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var n)) return value;
        var x = (n - spec.Shift) / spec.Scale;
        return Math.Round(x).ToString(CultureInfo.InvariantCulture);
    }

    private static string ObfuscateFloating(string value, ColumnObfuscationSpec spec)
    {
        if (!double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var n)) return value;
        var y = (n * spec.Scale) + spec.Shift;
        return y.ToString("G17", CultureInfo.InvariantCulture);
    }

    private static string DeobfuscateFloating(string value, ColumnObfuscationSpec spec)
    {
        if (!double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var n)) return value;
        var x = (n - spec.Shift) / spec.Scale;
        return x.ToString("G17", CultureInfo.InvariantCulture);
    }

    private static string ObfuscateDateTime(string value, ColumnObfuscationSpec spec)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return value;

        var shifted = dt.AddTicks(spec.DateShiftTicks);
        return shifted.ToString(spec.DateFormat ?? "O", CultureInfo.InvariantCulture);
    }

    private static string DeobfuscateDateTime(string value, ColumnObfuscationSpec spec)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return value;

        var shifted = dt.AddTicks(-spec.DateShiftTicks);
        return shifted.ToString(spec.DateFormat ?? "O", CultureInfo.InvariantCulture);
    }

    private string ObfuscateString(string value, ColumnObfuscationSpec spec)
    {
        return spec.StringMode switch
        {
            StringObfuscationMode.Mapping => ObfuscateMappedString(value, spec),
            StringObfuscationMode.DeterministicToken => ObfuscateDeterministicToken(value, spec),
            _ => value
        };
    }

    private string DeobfuscateString(string value, ColumnObfuscationSpec spec)
    {
        return spec.StringMode switch
        {
            StringObfuscationMode.Mapping => DeobfuscateMappedString(value, spec),
            StringObfuscationMode.DeterministicToken => DeobfuscateDeterministicToken(value, spec),
            _ => value
        };
    }

    private static string ObfuscateMappedString(string value, ColumnObfuscationSpec spec)
    {
        if (spec.StringMap is null) return value;
        return spec.StringMap.TryGetValue(value, out var mapped) ? mapped : value;
    }

    private static string DeobfuscateMappedString(string value, ColumnObfuscationSpec spec)
    {
        if (spec.StringMap is null) return value;

        spec.ReverseStringMap ??= spec.StringMap.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.Ordinal);
        return spec.ReverseStringMap.TryGetValue(value, out var original) ? original : value;
    }

    private string ObfuscateDeterministicToken(string value, ColumnObfuscationSpec spec)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = DeriveDeterministicTokenKey(spec.Name);
        aes.IV = DeriveDeterministicTokenIv(spec.Name);

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(value);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        return DeterministicTokenPrefix + Base64UrlEncode(cipherBytes);
    }

    private string DeobfuscateDeterministicToken(string value, ColumnObfuscationSpec spec)
    {
        if (!value.StartsWith(DeterministicTokenPrefix, StringComparison.Ordinal))
            return value;

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = DeriveDeterministicTokenKey(spec.Name);
        aes.IV = DeriveDeterministicTokenIv(spec.Name);

        using var decryptor = aes.CreateDecryptor();
        var cipherBytes = Base64UrlDecode(value[DeterministicTokenPrefix.Length..]);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private Dictionary<string, string> BuildStringMap(string header, IEnumerable<string> values)
    {
        var distinct = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var original in distinct)
        {
            map[original] = _deterministicKey is null
                ? "OBF_" + Guid.NewGuid().ToString("N")
                : "OBF_" + HashUtility.Sha256Hex($"{_deterministicKey}|{header}|{original}")[..24];
        }

        return map;
    }

    private byte[] DeriveDeterministicTokenKey(string header)
    {
        if (_deterministicKey is null)
            throw new InvalidOperationException($"Column '{header}' requires a deterministic key for string tokenization.");

        return SHA256.HashData(Encoding.UTF8.GetBytes($"{_deterministicKey}|{header}|token-key"));
    }

    private byte[] DeriveDeterministicTokenIv(string header)
    {
        if (_deterministicKey is null)
            throw new InvalidOperationException($"Column '{header}' requires a deterministic key for string tokenization.");

        return SHA256.HashData(Encoding.UTF8.GetBytes($"{_deterministicKey}|{header}|token-iv"))[..DeterministicTokenIvLengthBytes];
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    private double GetDeterministicScale(string header)
    {
        if (_deterministicKey is null)
            return GenerateRandomScale();

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{_deterministicKey}|{header}|scale"));
        var u = BitConverter.ToUInt32(bytes, 0) / (double)uint.MaxValue;
        var sign = (bytes[4] & 1) == 0 ? -1.0 : 1.0;
        var magnitude = 1.5 + (u * 8.5);
        return sign * magnitude;
    }

    private double GetDeterministicShift(string header)
    {
        if (_deterministicKey is null)
            return (_random.NextDouble() * ShiftRangeWidth) - ShiftRangeHalfWidth;

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{_deterministicKey}|{header}|shift"));
        var u = BitConverter.ToUInt32(bytes, 0) / (double)uint.MaxValue;
        return (u * ShiftRangeWidth) - ShiftRangeHalfWidth;
    }

    private long GetDeterministicDateShiftTicks(string header)
    {
        if (_deterministicKey is null)
            return TimeSpan.FromDays(_random.Next(-DateShiftRangeHalfWidthDays, DateShiftRangeHalfWidthDays)).Ticks;

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{_deterministicKey}|{header}|dateShift"));
        var u = BitConverter.ToUInt32(bytes, 0) / (double)uint.MaxValue;
        var days = (int)Math.Round((u * DateShiftRangeWidthDays) - DateShiftRangeHalfWidthDays);
        return TimeSpan.FromDays(days).Ticks;
    }

    private bool GetDeterministicBool(string header, string purpose)
    {
        if (_deterministicKey is null)
            return _random.Next(0, 2) == 1;

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{_deterministicKey}|{header}|{purpose}"));
        return (bytes[0] & 1) == 1;
    }

    private double GenerateRandomScale()
    {
        var sign = _random.Next(0, 2) == 0 ? -1.0 : 1.0;
        var magnitude = 1.5 + (_random.NextDouble() * 8.5);
        return sign * magnitude;
    }

    private static string? GuessDateFormat(IReadOnlyList<string> values)
    {
        var sample = values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (sample is null) return "O";
        return sample.Contains('T', StringComparison.Ordinal) ? "O" : "yyyy-MM-dd";
    }

    private static string ComputeIntegrityHash(ObfuscationManifest manifest)
    {
        var clone = new ObfuscationManifest
        {
            Version = manifest.Version,
            CreatedUtc = manifest.CreatedUtc,
            SourceFileName = manifest.SourceFileName,
            ObfuscatedFileName = manifest.ObfuscatedFileName,
            IntegrityHashSha256 = string.Empty,
            Columns = manifest.Columns
        };

        var json = JsonSerializer.Serialize(clone);
        return HashUtility.Sha256Hex(json);
    }
}
