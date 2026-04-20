using Squalor.Obfuscator;
using System.Reflection;

return ObfuscatorCliProgram.Run(args);

internal static class ObfuscatorCliProgram
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "generate" => RunGenerate(args[1..]),
                "obfuscate" => RunObfuscate(args[1..]),
                "deobfuscate" => RunDeobfuscate(args[1..]),
                _ => Fail($"Unknown command '{args[0]}'.")
            };
        }
        catch (Exception ex)
        {
            // For normal user errors, print to stderr and return a nonzero exit code instead of throwing.
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int RunGenerate(string[] args)
    {
        var options = ParseOptions(args);
        var config = RequireSingle(options, "--config");
        var output = RequireSingle(options, "--output");
        EnsureOutputDirectoryExists(output, options.ContainsKey("--create-output-dir"));

        var obfuscator = new Obfuscator();
        obfuscator.GenerateCsvFromConfig(config, output);
        Console.WriteLine($"Generated CSV: {output}");
        return 0;
    }

    private static int RunObfuscate(string[] args)
    {
        var options = ParseOptions(args);
        var output = RequireSingle(options, "--output");
        var manifest = RequireSingle(options, "--manifest");

        EnsureOutputDirectoryExists(output, options.ContainsKey("--create-output-dir"));
        EnsureOutputDirectoryExists(manifest, options.ContainsKey("--create-output-dir"));

        var obfuscator = new Obfuscator();
        obfuscator.ObfuscateCsv(
            inputCsvPath: RequireSingle(options, "--input"),
            outputCsvPath: output,
            obfPath: manifest,
            options: new ObfuscationOptions
            {
                Seed = ParseNullableInt(GetSingle(options, "--seed"), "--seed"),
                Passphrase = GetSingle(options, "--passphrase"),
                DeterministicKey = GetSingle(options, "--deterministic-key"),
                PreserveBlanks = ParseBool(GetSingle(options, "--preserve-blanks"), defaultValue: true),
                UseIncludeListAsAllowList = options.ContainsKey("--allow-list"),
                StringMode = ParseStringMode(GetSingle(options, "--string-mode")),
                IncludeColumns = GetMany(options, "--include"),
                ExcludeColumns = GetMany(options, "--exclude"),
                GpgRecipients = GetMany(options, "--gpg-recipient")
            });

        Console.WriteLine($"Obfuscated CSV: {output}");
        Console.WriteLine($"Manifest: {manifest}");
        return 0;
    }

    private static int RunDeobfuscate(string[] args)
    {
        var options = ParseOptions(args);
        var output = RequireSingle(options, "--output");

        EnsureOutputDirectoryExists(output, options.ContainsKey("--create-output-dir"));

        var obfuscator = new Obfuscator();
        obfuscator.DeobfuscateCsv(
            obfuscatedCsvPath: RequireSingle(options, "--input"),
            obfPath: RequireSingle(options, "--manifest"),
            outputCsvPath: output,
            passphrase: GetSingle(options, "--passphrase"),
            deterministicKey: GetSingle(options, "--deterministic-key"));

        Console.WriteLine($"Restored CSV: {output}");
        return 0;
    }

    private static Dictionary<string, List<string>> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
                throw new InvalidOperationException($"Unexpected token '{token}'. Options must start with '--'.");

            if (!result.TryGetValue(token, out var values))
            {
                values = [];
                result[token] = values;
            }

            // Flags are stored with an empty string value so presence checks work the same as normal options.
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values.Add(args[++i]);
            }
            else
            {
                values.Add(string.Empty);
            }
        }

        return result;
    }

    private static string RequireSingle(Dictionary<string, List<string>> options, string name)
        => GetSingle(options, name) ?? throw new InvalidOperationException($"Missing required option '{name}'.");

    private static string? GetSingle(Dictionary<string, List<string>> options, string name)
    {
        if (!options.TryGetValue(name, out var values) || values.Count == 0)
            return null;

        return values[^1];
    }

    private static List<string> GetMany(Dictionary<string, List<string>> options, string name)
    {
        if (!options.TryGetValue(name, out var values))
            return [];

        return values
            .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
    }

    private static int? ParseNullableInt(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!int.TryParse(value, out var parsed))
            throw new InvalidOperationException($"Option '{name}' must be an integer.");

        return parsed;
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        if (!bool.TryParse(value, out var parsed))
            throw new InvalidOperationException("Boolean options must be 'true' or 'false'.");

        return parsed;
    }

    private static StringObfuscationMode ParseStringMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return StringObfuscationMode.Auto;

        return value.Trim().ToLowerInvariant() switch
        {
            "auto" => StringObfuscationMode.Auto,
            "mapping" => StringObfuscationMode.Mapping,
            "deterministic-token" => StringObfuscationMode.DeterministicToken,
            _ => throw new InvalidOperationException("String mode must be one of: auto, mapping, deterministic-token.")
        };
    }

    private static bool IsHelp(string arg)
        => arg is "-h" or "--help" or "help";

    private static void EnsureOutputDirectoryExists(string outputPath, bool createOutputDirectory)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(directory) || Directory.Exists(directory))
            return;

        if (createOutputDirectory)
        {
            // Make directory creation explicit so the CLI does not silently create paths by default.
            Directory.CreateDirectory(directory);
            return;
        }

        throw new DirectoryNotFoundException(
            $"Output directory '{directory}' does not exist. Create it first or rerun with '--create-output-dir'.");
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine();
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        PrintBanner();
        Console.WriteLine("Usage:");
        Console.WriteLine("  obfuscator generate --config <file.json> --output <file.csv> [--create-output-dir]");
        Console.WriteLine("  obfuscator obfuscate --input <file.csv> --output <file.csv> --manifest <file.obf> [options]");
        Console.WriteLine("  obfuscator deobfuscate --input <file.csv> --manifest <file.obf> --output <file.csv> [options]");
        Console.WriteLine();
        Console.WriteLine("Output options:");
        Console.WriteLine("  --create-output-dir");
        Console.WriteLine("      Create missing parent directories for --output and --manifest paths.");
        Console.WriteLine();
        Console.WriteLine("Obfuscate options:");
        Console.WriteLine("  --deterministic-key <secret>");
        Console.WriteLine("  --string-mode <auto|mapping|deterministic-token>");
        Console.WriteLine("  --include <colA,colB>");
        Console.WriteLine("  --exclude <colA,colB>");
        Console.WriteLine("  --allow-list");
        Console.WriteLine("  --seed <int>");
        Console.WriteLine("  --passphrase <secret>");
        Console.WriteLine("  --gpg-recipient <recipient>");
        Console.WriteLine("  --preserve-blanks <true|false>");
        Console.WriteLine();
        Console.WriteLine("Deobfuscate options:");
        Console.WriteLine("  --passphrase <secret>");
        Console.WriteLine("  --deterministic-key <secret>");
    }

    private static void PrintBanner()
    {
        var assembly = typeof(ObfuscatorCliProgram).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var version = string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString() ?? "unknown"
            : informationalVersion;

        Console.WriteLine($"obfuscator {version}");
        Console.WriteLine("CSV generation, reversible obfuscation, and deobfuscation for .NET.");
        Console.WriteLine();
    }
}
