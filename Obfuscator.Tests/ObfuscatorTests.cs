using System.Text;
using System.Text.Json;
using Squalor.Obfuscator;
using Xunit;

namespace Squalor.Obfuscator.Tests;

public sealed class ObfuscatorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "ObfuscatorTests", Guid.NewGuid().ToString("N"));
    private readonly Obfuscator _obfuscator = new();

    public ObfuscatorTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void GenerateCsvFromConfig_WritesExpectedHeaderAndRowCount()
    {
        var configPath = Path.Combine(_tempDir, "config.json");
        var outputPath = Path.Combine(_tempDir, "generated.csv");

        File.WriteAllText(configPath, """
        {
          "rowMode": "fixed",
          "nRows": 3,
          "seed": 7,
          "columns": [
            { "name": "Id", "dataType": "int", "totalRangeMin": 1, "totalRangeMax": 100 },
            { "name": "Enabled", "dataType": "boolean", "truePercentage": 100 },
            { "name": "Region", "dataType": "string", "staticValue": "east" }
          ]
        }
        """, Encoding.UTF8);

        _obfuscator.GenerateCsvFromConfig(configPath, outputPath);

        var lines = File.ReadAllLines(outputPath);
        Assert.Equal(4, lines.Length);
        Assert.Equal("Id,Enabled,Region", lines[0]);
        Assert.All(lines[1..], line => Assert.Contains(",true,east", line, StringComparison.Ordinal));
    }

    [Fact]
    public void GenerateCsvFromConfig_WithSweepAxes_GeneratesStimulusGroupAndSweepIds()
    {
        var configPath = Path.Combine(_tempDir, "sweep-config.json");
        var outputPath = Path.Combine(_tempDir, "sweep-generated.csv");

        File.WriteAllText(configPath, """
        {
          "rowMode": "sweep",
          "seed": 7,
          "sweepAxes": [
            { "name": "Temp(degC)" },
            { "name": "Frequency(MHz)" },
            { "name": "Pout(dBm)" }
          ],
          "columns": [
            { "name": "stimulusGrp(id)", "dataType": "bigint", "totalRangeMin": 1, "totalRangeMax": 9999, "generatedIdMode": "outer-group" },
            { "name": "sweep(id)", "dataType": "bigint", "totalRangeMin": 1, "totalRangeMax": 9999, "generatedIdMode": "inner-step" },
            { "name": "Temp(degC)", "dataType": "double", "totalRangeMin": -40, "totalRangeMax": 25, "values": [ "-40", "25" ] },
            { "name": "Frequency(MHz)", "dataType": "double", "totalRangeMin": 2400, "totalRangeMax": 2450, "values": [ "2400", "2450" ] },
            { "name": "Pout(dBm)", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 10, "values": [ "0", "5", "10" ] }
          ]
        }
        """, Encoding.UTF8);

        _obfuscator.GenerateCsvFromConfig(configPath, outputPath);

        var lines = File.ReadAllLines(outputPath);
        Assert.Equal(13, lines.Length);
        Assert.Equal("stimulusGrp(id),sweep(id),Temp(degC),Frequency(MHz),Pout(dBm)", lines[0]);

        var rows = lines[1..]
            .Select(line => line.Split(','))
            .Select(parts => new
            {
                StimulusGroupId = parts[0],
                SweepId = parts[1],
                Temp = parts[2],
                Frequency = parts[3],
                Pout = parts[4]
            })
            .ToArray();

        Assert.Collection(rows,
            row => { Assert.Equal("1", row.StimulusGroupId); Assert.Equal("1", row.SweepId); Assert.Equal("-40", row.Temp); Assert.Equal("2400", row.Frequency); Assert.Equal("0", row.Pout); },
            row => { Assert.Equal("1", row.StimulusGroupId); Assert.Equal("2", row.SweepId); Assert.Equal("-40", row.Temp); Assert.Equal("2400", row.Frequency); Assert.Equal("5", row.Pout); },
            row => { Assert.Equal("1", row.StimulusGroupId); Assert.Equal("3", row.SweepId); Assert.Equal("-40", row.Temp); Assert.Equal("2400", row.Frequency); Assert.Equal("10", row.Pout); },
            row => { Assert.Equal("2", row.StimulusGroupId); Assert.Equal("1", row.SweepId); Assert.Equal("-40", row.Temp); Assert.Equal("2450", row.Frequency); Assert.Equal("0", row.Pout); },
            row => { Assert.Equal("2", row.StimulusGroupId); Assert.Equal("2", row.SweepId); Assert.Equal("-40", row.Temp); Assert.Equal("2450", row.Frequency); Assert.Equal("5", row.Pout); },
            row => { Assert.Equal("2", row.StimulusGroupId); Assert.Equal("3", row.SweepId); Assert.Equal("-40", row.Temp); Assert.Equal("2450", row.Frequency); Assert.Equal("10", row.Pout); },
            row => { Assert.Equal("3", row.StimulusGroupId); Assert.Equal("1", row.SweepId); Assert.Equal("25", row.Temp); Assert.Equal("2400", row.Frequency); Assert.Equal("0", row.Pout); },
            row => { Assert.Equal("3", row.StimulusGroupId); Assert.Equal("2", row.SweepId); Assert.Equal("25", row.Temp); Assert.Equal("2400", row.Frequency); Assert.Equal("5", row.Pout); },
            row => { Assert.Equal("3", row.StimulusGroupId); Assert.Equal("3", row.SweepId); Assert.Equal("25", row.Temp); Assert.Equal("2400", row.Frequency); Assert.Equal("10", row.Pout); },
            row => { Assert.Equal("4", row.StimulusGroupId); Assert.Equal("1", row.SweepId); Assert.Equal("25", row.Temp); Assert.Equal("2450", row.Frequency); Assert.Equal("0", row.Pout); },
            row => { Assert.Equal("4", row.StimulusGroupId); Assert.Equal("2", row.SweepId); Assert.Equal("25", row.Temp); Assert.Equal("2450", row.Frequency); Assert.Equal("5", row.Pout); },
            row => { Assert.Equal("4", row.StimulusGroupId); Assert.Equal("3", row.SweepId); Assert.Equal("25", row.Temp); Assert.Equal("2450", row.Frequency); Assert.Equal("10", row.Pout); });
    }

    [Fact]
    public void GenerateCsvFromConfig_WithRowModeMax_RepeatsSweepPatternUpToNRows()
    {
        var configPath = Path.Combine(_tempDir, "max-sweep-config.json");
        var outputPath = Path.Combine(_tempDir, "max-sweep-generated.csv");

        File.WriteAllText(configPath, """
        {
          "rowMode": "max",
          "nRows": 5,
          "seed": 7,
          "sweepAxes": [
            { "name": "Frequency(MHz)" },
            { "name": "Pout(dBm)" }
          ],
          "columns": [
            { "name": "stimulusGrp(id)", "dataType": "bigint", "totalRangeMin": 1, "totalRangeMax": 9999, "generatedIdMode": "outer-group" },
            { "name": "sweep(id)", "dataType": "bigint", "totalRangeMin": 1, "totalRangeMax": 9999, "generatedIdMode": "inner-step" },
            { "name": "Frequency(MHz)", "dataType": "double", "totalRangeMin": 2400, "totalRangeMax": 2450, "values": [ "2400", "2450" ] },
            { "name": "Pout(dBm)", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 5, "values": [ "0", "5" ] }
          ]
        }
        """, Encoding.UTF8);

        _obfuscator.GenerateCsvFromConfig(configPath, outputPath);

        var lines = File.ReadAllLines(outputPath);
        Assert.Equal(6, lines.Length);

        var rows = lines[1..]
            .Select(line => line.Split(','))
            .Select(parts => new
            {
                StimulusGroupId = parts[0],
                SweepId = parts[1],
                Frequency = parts[2],
                Pout = parts[3]
            })
            .ToArray();

        Assert.Collection(rows,
            row => { Assert.Equal("1", row.StimulusGroupId); Assert.Equal("1", row.SweepId); Assert.Equal("2400", row.Frequency); Assert.Equal("0", row.Pout); },
            row => { Assert.Equal("1", row.StimulusGroupId); Assert.Equal("2", row.SweepId); Assert.Equal("2400", row.Frequency); Assert.Equal("5", row.Pout); },
            row => { Assert.Equal("2", row.StimulusGroupId); Assert.Equal("1", row.SweepId); Assert.Equal("2450", row.Frequency); Assert.Equal("0", row.Pout); },
            row => { Assert.Equal("2", row.StimulusGroupId); Assert.Equal("2", row.SweepId); Assert.Equal("2450", row.Frequency); Assert.Equal("5", row.Pout); },
            row => { Assert.Equal("1", row.StimulusGroupId); Assert.Equal("1", row.SweepId); Assert.Equal("2400", row.Frequency); Assert.Equal("0", row.Pout); });
    }

    [Fact]
    public void ObfuscateAndDeobfuscate_MappingMode_RestoresAllDistinctStringValues()
    {
        var inputPath = Path.Combine(_tempDir, "mapping-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "mapping-obfuscated.csv");
        var restoredPath = Path.Combine(_tempDir, "mapping-restored.csv");
        var manifestPath = Path.Combine(_tempDir, "mapping.obf");

        var rows = Enumerable.Range(0, 300)
            .Select(i => $"CUST_{i:000},Region_{i:000}")
            .ToArray();
        File.WriteAllLines(inputPath, ["CustomerId,Region", .. rows], Encoding.UTF8);

        var manifest = _obfuscator.ObfuscateCsv(
            inputPath,
            obfuscatedPath,
            manifestPath,
            new ObfuscationOptions
            {
                StringMode = StringObfuscationMode.Mapping
            });

        _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath);

        var restored = File.ReadAllLines(restoredPath, Encoding.UTF8);
        var original = File.ReadAllLines(inputPath, Encoding.UTF8);

        Assert.Equal(original, restored);
        Assert.Equal(300, manifest.Columns.Single(c => c.Name == "CustomerId").StringMap?.Count);
        Assert.Equal(300, manifest.Columns.Single(c => c.Name == "Region").StringMap?.Count);
    }

    [Fact]
    public void ObfuscateAndDeobfuscate_DeterministicTokenMode_RestoresWithoutManifestStringMap()
    {
        var inputPath = Path.Combine(_tempDir, "token-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "token-obfuscated.csv");
        var restoredPath = Path.Combine(_tempDir, "token-restored.csv");
        var manifestPath = Path.Combine(_tempDir, "token.obf");

        File.WriteAllLines(inputPath,
            [
                "CustomerId,Region",
                "ALPHA,west",
                "ALPHA,west",
                "BETA,east"
            ],
            Encoding.UTF8);

        var manifest = _obfuscator.ObfuscateCsv(
            inputPath,
            obfuscatedPath,
            manifestPath,
            new ObfuscationOptions
            {
                DeterministicKey = "unit-test-key",
                StringMode = StringObfuscationMode.DeterministicToken
            });

        _obfuscator.DeobfuscateCsv(
            obfuscatedPath,
            manifestPath,
            restoredPath,
            deterministicKey: "unit-test-key");

        var obfuscatedLines = File.ReadAllLines(obfuscatedPath);
        var restored = File.ReadAllLines(restoredPath, Encoding.UTF8);
        var original = File.ReadAllLines(inputPath, Encoding.UTF8);
        var customerSpec = manifest.Columns.Single(c => c.Name == "CustomerId");

        Assert.Equal(original, restored);
        Assert.Null(customerSpec.StringMap);
        Assert.Equal(StringObfuscationMode.DeterministicToken, customerSpec.StringMode);
        Assert.StartsWith("OBF_TKN_", obfuscatedLines[1].Split(',')[0], StringComparison.Ordinal);
        Assert.Equal(obfuscatedLines[1].Split(',')[0], obfuscatedLines[2].Split(',')[0]);
        Assert.NotEqual(obfuscatedLines[1].Split(',')[0], obfuscatedLines[3].Split(',')[0]);
    }

    [Fact]
    public void Deobfuscate_DeterministicTokenModeWithoutKey_Throws()
    {
        var inputPath = Path.Combine(_tempDir, "missing-key-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "missing-key-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "missing-key.obf");

        File.WriteAllLines(inputPath,
            [
                "CustomerId",
                "ALPHA"
            ],
            Encoding.UTF8);

        _obfuscator.ObfuscateCsv(
            inputPath,
            obfuscatedPath,
            manifestPath,
            new ObfuscationOptions
            {
                DeterministicKey = "unit-test-key",
                StringMode = StringObfuscationMode.DeterministicToken
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, Path.Combine(_tempDir, "missing-key-restored.csv")));

        Assert.Contains("requires a deterministic key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Obfuscate_WithIncludeAllowList_OnlyObfuscatesIncludedColumns()
    {
        var inputPath = Path.Combine(_tempDir, "allow-list-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "allow-list-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "allow-list.obf");

        File.WriteAllLines(
            inputPath,
            [
                "CustomerId,Region,Status",
                "ALPHA,west,active"
            ],
            Encoding.UTF8);

        var manifest = _obfuscator.ObfuscateCsv(
            inputPath,
            obfuscatedPath,
            manifestPath,
            new ObfuscationOptions
            {
                UseIncludeListAsAllowList = true,
                IncludeColumns = ["CustomerId"]
            });

        var obfuscatedLines = File.ReadAllLines(obfuscatedPath, Encoding.UTF8);
        var values = obfuscatedLines[1].Split(',');

        Assert.NotEqual("ALPHA", values[0]);
        Assert.Equal("west", values[1]);
        Assert.Equal("active", values[2]);
        Assert.True(manifest.Columns.Single(c => c.Name == "CustomerId").IsObfuscated);
        Assert.False(manifest.Columns.Single(c => c.Name == "Region").IsObfuscated);
        Assert.False(manifest.Columns.Single(c => c.Name == "Status").IsObfuscated);
    }

    [Fact]
    public void Obfuscate_WithPreserveBlanksFalse_TransformsBlankAndRestoresIt()
    {
        var inputPath = Path.Combine(_tempDir, "blank-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "blank-obfuscated.csv");
        var restoredPath = Path.Combine(_tempDir, "blank-restored.csv");
        var manifestPath = Path.Combine(_tempDir, "blank.obf");

        File.WriteAllLines(
            inputPath,
            [
                "CustomerId,Region",
                "ALPHA,west",
                ",east"
            ],
            Encoding.UTF8);

        _obfuscator.ObfuscateCsv(
            inputPath,
            obfuscatedPath,
            manifestPath,
            new ObfuscationOptions
            {
                PreserveBlanks = false,
                DeterministicKey = "unit-test-key",
                StringMode = StringObfuscationMode.DeterministicToken
            });

        _obfuscator.DeobfuscateCsv(
            obfuscatedPath,
            manifestPath,
            restoredPath,
            deterministicKey: "unit-test-key");

        var obfuscatedLines = File.ReadAllLines(obfuscatedPath, Encoding.UTF8);
        var restoredLines = File.ReadAllLines(restoredPath, Encoding.UTF8);
        var obfuscatedValues = obfuscatedLines[2].Split(',');
        var restoredValues = restoredLines[2].Split(',');

        Assert.StartsWith("OBF_TKN_", obfuscatedValues[0], StringComparison.Ordinal);
        Assert.Equal(string.Empty, restoredValues[0]);
        Assert.Equal("east", restoredValues[1]);
    }

    [Fact]
    public void ObfuscateAndDeobfuscate_DateAndNumericColumns_RestoreOriginalValues()
    {
        var inputPath = Path.Combine(_tempDir, "date-numeric-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "date-numeric-obfuscated.csv");
        var restoredPath = Path.Combine(_tempDir, "date-numeric-restored.csv");
        var manifestPath = Path.Combine(_tempDir, "date-numeric.obf");

        File.WriteAllLines(
            inputPath,
            [
                "SampleDate,Count,Voltage",
                "2026-04-20,42,3.3",
                "2026-04-21,105,4.2"
            ],
            Encoding.UTF8);

        _obfuscator.ObfuscateCsv(
            inputPath,
            obfuscatedPath,
            manifestPath,
            new ObfuscationOptions
            {
                DeterministicKey = "unit-test-key"
            });

        _obfuscator.DeobfuscateCsv(
            obfuscatedPath,
            manifestPath,
            restoredPath,
            deterministicKey: "unit-test-key");

        var restoredLines = File.ReadAllLines(restoredPath, Encoding.UTF8);

        Assert.Equal("SampleDate,Count,Voltage", restoredLines[0]);

        var row1 = restoredLines[1].Split(',');
        Assert.Equal("2026-04-20", row1[0]);
        Assert.Equal("42", row1[1]);
        Assert.InRange(double.Parse(row1[2]), 3.299999, 3.300001);

        var row2 = restoredLines[2].Split(',');
        Assert.Equal("2026-04-21", row2[0]);
        Assert.Equal("105", row2[1]);
        Assert.InRange(double.Parse(row2[2]), 4.199999, 4.200001);
    }

    [Fact]
    public void Deobfuscate_WithTamperedManifest_ThrowsIntegrityCheckFailure()
    {
        var inputPath = Path.Combine(_tempDir, "tamper-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "tamper-obfuscated.csv");
        var restoredPath = Path.Combine(_tempDir, "tamper-restored.csv");
        var manifestPath = Path.Combine(_tempDir, "tamper.obf");

        File.WriteAllLines(
            inputPath,
            [
                "CustomerId,Region",
                "ALPHA,west"
            ],
            Encoding.UTF8);

        _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath);

        var manifestText = File.ReadAllText(manifestPath, Encoding.UTF8);
        var header = "OBF_PLAIN_V2\n";
        Assert.StartsWith(header, manifestText, StringComparison.Ordinal);

        var manifest = JsonSerializer.Deserialize<ObfuscationManifest>(manifestText[header.Length..])
            ?? throw new InvalidOperationException("Failed to deserialize test manifest.");
        manifest.SourceFileName = "tampered.csv";
        File.WriteAllText(
            manifestPath,
            header + JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath));

        Assert.Contains("integrity check failed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deobfuscate_WithWindowsStyleManifestHeader_RestoresOnCurrentPlatform()
    {
        var inputPath = Path.Combine(_tempDir, "cross-platform-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "cross-platform-obfuscated.csv");
        var restoredPath = Path.Combine(_tempDir, "cross-platform-restored.csv");
        var manifestPath = Path.Combine(_tempDir, "cross-platform.obf");

        File.WriteAllLines(
            inputPath,
            [
                "CustomerId,Region",
                "ALPHA,west",
                "BETA,east"
            ],
            Encoding.UTF8);

        _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath);

        var manifestText = File.ReadAllText(manifestPath, Encoding.UTF8);
        var rewrittenManifestText = manifestText.Replace("OBF_PLAIN_V2\n", "OBF_PLAIN_V2\r\n", StringComparison.Ordinal);
        File.WriteAllText(manifestPath, rewrittenManifestText, Encoding.UTF8);

        _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath);

        Assert.Equal(
            File.ReadAllLines(inputPath, Encoding.UTF8),
            File.ReadAllLines(restoredPath, Encoding.UTF8));
    }

    [Fact]
    public void GetDataGenParametersFromConfig_WithUnsupportedRowMode_Throws()
    {
        var configPath = WriteConfig(
            "bad-row-mode.json",
            """
            {
              "rowMode": "banana",
              "nRows": 2,
              "columns": [
                { "name": "Id", "dataType": "int", "totalRangeMin": 1, "totalRangeMax": 10 }
              ]
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => _obfuscator.GetDataGenParametersFromConfig(configPath));

        Assert.Contains("Unsupported rowMode", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetDataGenParametersFromConfig_WithFixedRowModeAndSweepAxes_Throws()
    {
        var configPath = WriteConfig(
            "fixed-with-sweep.json",
            """
            {
              "rowMode": "fixed",
              "nRows": 2,
              "sweepAxes": [
                { "name": "Pout(dBm)" }
              ],
              "columns": [
                { "name": "Pout(dBm)", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 10, "values": [ "0", "5" ] }
              ]
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => _obfuscator.GetDataGenParametersFromConfig(configPath));

        Assert.Contains("cannot be used with sweepAxes", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetDataGenParametersFromConfig_WithSweepRowModeAndMissingSweepAxisValues_Throws()
    {
        var configPath = WriteConfig(
            "missing-sweep-values.json",
            """
            {
              "rowMode": "sweep",
              "sweepAxes": [
                { "name": "Pout(dBm)" }
              ],
              "columns": [
                { "name": "Pout(dBm)", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 10 }
              ]
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => _obfuscator.GetDataGenParametersFromConfig(configPath));

        Assert.Contains("must define at least one value", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetDataGenParametersFromConfig_WithNonIntegerGeneratedIdColumn_Throws()
    {
        var configPath = WriteConfig(
            "bad-generated-id.json",
            """
            {
              "rowMode": "sweep",
              "sweepAxes": [
                { "name": "Pout(dBm)" }
              ],
              "columns": [
                { "name": "stimulusGrp(id)", "dataType": "double", "totalRangeMin": 1, "totalRangeMax": 999, "generatedIdMode": "outer-group" },
                { "name": "Pout(dBm)", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 10, "values": [ "0", "5" ] }
              ]
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => _obfuscator.GetDataGenParametersFromConfig(configPath));

        Assert.Contains("must use a numeric integer type", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenerateCsv_WithMaxRowModeAndLargeSweepExpansion_UsesSweepCardinality()
    {
        var outputPath = Path.Combine(_tempDir, "max-large-sweep.csv");
        var config = new DataGenConfig
        {
            RowMode = "max",
            NRows = 2,
            Seed = 7,
            SweepAxes =
            [
                new SweepAxisSpec { Name = "Frequency(MHz)" },
                new SweepAxisSpec { Name = "Pout(dBm)" }
            ],
            Columns =
            [
                new ColumnGenerationSpec { Name = "stimulusGrp(id)", DataType = "bigint", TotalRangeMin = 1, TotalRangeMax = 9999, GeneratedIdMode = "outer-group" },
                new ColumnGenerationSpec { Name = "sweep(id)", DataType = "bigint", TotalRangeMin = 1, TotalRangeMax = 9999, GeneratedIdMode = "inner-step" },
                new ColumnGenerationSpec { Name = "Frequency(MHz)", DataType = "double", TotalRangeMin = 2400, TotalRangeMax = 2450, Values = ["2400", "2450"] },
                new ColumnGenerationSpec { Name = "Pout(dBm)", DataType = "double", TotalRangeMin = 0, TotalRangeMax = 10, Values = ["0", "5", "10"] }
            ]
        };

        _obfuscator.GenerateCsv(config, outputPath);

        var lines = File.ReadAllLines(outputPath);
        Assert.Equal(7, lines.Length);
    }

    [Fact]
    public void GenerateCsv_WithFixedRowModeAndSeed_IsDeterministic()
    {
        var outputPath1 = Path.Combine(_tempDir, "deterministic-1.csv");
        var outputPath2 = Path.Combine(_tempDir, "deterministic-2.csv");
        var config = new DataGenConfig
        {
            RowMode = "fixed",
            NRows = 4,
            Seed = 123,
            Columns =
            [
                new ColumnGenerationSpec { Name = "Id", DataType = "int", TotalRangeMin = 1, TotalRangeMax = 100 },
                new ColumnGenerationSpec { Name = "Enabled", DataType = "boolean", TruePercentage = 25 },
                new ColumnGenerationSpec { Name = "Code", DataType = "string", RandomString = true, StringPrefix = "SQ_", RandomStringLength = 4 }
            ]
        };

        _obfuscator.GenerateCsv(config, outputPath1);
        _obfuscator.GenerateCsv(config, outputPath2);

        Assert.Equal(File.ReadAllText(outputPath1, Encoding.UTF8), File.ReadAllText(outputPath2, Encoding.UTF8));
    }

    [Fact]
    public void GenerateCsv_WithIdealRangeAndSeed_IsDeterministic()
    {
        var outputPath1 = Path.Combine(_tempDir, "ideal-1.csv");
        var outputPath2 = Path.Combine(_tempDir, "ideal-2.csv");
        var config = new DataGenConfig
        {
            RowMode = "fixed",
            NRows = 40,
            Seed = 123,
            Columns =
            [
                new ColumnGenerationSpec
                {
                    Name = "Gain",
                    DataType = "double",
                    TotalRangeMin = 5,
                    TotalRangeMax = 35,
                    IdealRangeMin = 18,
                    IdealRangeMax = 28
                }
            ]
        };

        _obfuscator.GenerateCsv(config, outputPath1);
        _obfuscator.GenerateCsv(config, outputPath2);

        Assert.Equal(File.ReadAllText(outputPath1, Encoding.UTF8), File.ReadAllText(outputPath2, Encoding.UTF8));
    }

    [Fact]
    public void GenerateCsv_SemiconductorExampleConfig_IsDeterministic()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "ExampleConfigSemiconductor.json");
        Assert.True(File.Exists(configPath), configPath);
        var outputPath1 = Path.Combine(_tempDir, "semi-1.csv");
        var outputPath2 = Path.Combine(_tempDir, "semi-2.csv");

        _obfuscator.GenerateCsvFromConfig(configPath, outputPath1);
        _obfuscator.GenerateCsvFromConfig(configPath, outputPath2);

        Assert.Equal(File.ReadAllBytes(outputPath1), File.ReadAllBytes(outputPath2));
    }

    [Fact]
    public void CliGenerate_WithoutCreateOutputDirFlag_ThrowsHelpfulMessage()
    {
        var configPath = WriteConfig(
            "cli-generate.json",
            """
            {
              "rowMode": "fixed",
              "nRows": 1,
              "seed": 7,
              "columns": [
                { "name": "Id", "dataType": "int", "totalRangeMin": 1, "totalRangeMax": 10 }
              ]
            }
            """);
        var outputPath = Path.Combine(_tempDir, "missing", "nested", "sample.csv");
        using var stderr = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(stderr);

        try
        {
            var exitCode = ObfuscatorCliProgram.Run(["generate", "--config", configPath, "--output", outputPath]);

            Assert.Equal(1, exitCode);
            Assert.Contains("--create-output-dir", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void CliGenerate_WithCreateOutputDirFlag_CreatesDirectoryAndWritesFile()
    {
        var configPath = WriteConfig(
            "cli-generate-create-dir.json",
            """
            {
              "rowMode": "fixed",
              "nRows": 1,
              "seed": 7,
              "columns": [
                { "name": "Id", "dataType": "int", "totalRangeMin": 1, "totalRangeMax": 10 }
              ]
            }
            """);
        var outputDirectory = Path.Combine(_tempDir, "created", "nested");
        var outputPath = Path.Combine(outputDirectory, "sample.csv");

        var exitCode = ObfuscatorCliProgram.Run(
            ["generate", "--config", configPath, "--output", outputPath, "--create-output-dir"]);

        Assert.Equal(0, exitCode);
        Assert.True(Directory.Exists(outputDirectory));
        Assert.True(File.Exists(outputPath));
    }

    private string WriteConfig(string fileName, string json)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, json, Encoding.UTF8);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
