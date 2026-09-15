/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
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
    public void DeterministicKey_DerivationUsesManifestSalt()
    {
        var inputPath = Path.Combine(_tempDir, "salt-input.csv");
        File.WriteAllLines(inputPath, ["CustomerId", "ALPHA", "ALPHA"], Encoding.UTF8);
        var options = new ObfuscationOptions
        {
            DeterministicKey = "unit-test-key",
            StringMode = StringObfuscationMode.DeterministicToken
        };

        _obfuscator.ObfuscateCsv(inputPath, Path.Combine(_tempDir, "salt-a.csv"), Path.Combine(_tempDir, "salt-a.obf"), options);
        _obfuscator.ObfuscateCsv(inputPath, Path.Combine(_tempDir, "salt-b.csv"), Path.Combine(_tempDir, "salt-b.obf"), options);

        var tokenA = File.ReadAllLines(Path.Combine(_tempDir, "salt-a.csv"), Encoding.UTF8)[1];
        var tokenB = File.ReadAllLines(Path.Combine(_tempDir, "salt-b.csv"), Encoding.UTF8)[1];
        Assert.NotEqual(tokenA, tokenB);
    }

    [Fact]
    public void DeterministicKey_SameKeyAndSalt_ProducesJoinableTokens()
    {
        var inputPath = Path.Combine(_tempDir, "join-input.csv");
        File.WriteAllLines(inputPath, ["CustomerId", "ALPHA", "ALPHA", "BETA"], Encoding.UTF8);

        _obfuscator.ObfuscateCsv(
            inputPath,
            Path.Combine(_tempDir, "join-out.csv"),
            Path.Combine(_tempDir, "join.obf"),
            new ObfuscationOptions
            {
                DeterministicKey = "unit-test-key",
                StringMode = StringObfuscationMode.DeterministicToken
            });

        var lines = File.ReadAllLines(Path.Combine(_tempDir, "join-out.csv"), Encoding.UTF8);
        Assert.Equal(lines[1], lines[2]);
        Assert.NotEqual(lines[1], lines[3]);
    }

    [Fact]
    public void Manifest_AesGcm_TamperedCiphertext_ThrowsAuthenticationFailure()
    {
        var inputPath = Path.Combine(_tempDir, "gcm-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "gcm-obfuscated.csv");
        var restoredPath = Path.Combine(_tempDir, "gcm-restored.csv");
        var manifestPath = Path.Combine(_tempDir, "gcm.obf");
        File.WriteAllLines(inputPath, ["CustomerId", "ALPHA"], Encoding.UTF8);

        _obfuscator.ObfuscateCsv(
            inputPath,
            obfuscatedPath,
            manifestPath,
            new ObfuscationOptions { Passphrase = "secret" });

        var text = File.ReadAllText(manifestPath, Encoding.UTF8);
        Assert.StartsWith("OBF_AESGCM_V3\n", text, StringComparison.Ordinal);
        var packed = Convert.FromBase64String(text["OBF_AESGCM_V3\n".Length..]);
        packed[packed.Length / 2] ^= 0x01;
        File.WriteAllText(manifestPath, "OBF_AESGCM_V3\n" + Convert.ToBase64String(packed), Encoding.UTF8);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath, passphrase: "secret"));
        Assert.DoesNotContain("JSON", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AES-GCM", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_V2_StillReadable()
    {
        var inputPath = Path.Combine(_tempDir, "v2-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "v2-obfuscated.csv");
        var restoredPath = Path.Combine(_tempDir, "v2-restored.csv");
        var manifestPath = Path.Combine(_tempDir, "v2.obf");
        File.WriteAllLines(inputPath, ["CustomerId", "ALPHA"], Encoding.UTF8);

        _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath);
        var plain = File.ReadAllText(manifestPath, Encoding.UTF8);
        Assert.StartsWith("OBF_PLAIN_V2\n", plain, StringComparison.Ordinal);
        var json = plain["OBF_PLAIN_V2\n".Length..];
        var v2 = "OBF_AES_V2\n" + ManifestCrypto.EncryptAes(json, "legacy");
        File.WriteAllText(manifestPath, v2, Encoding.UTF8);

        _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath, passphrase: "legacy");
        Assert.Equal("ALPHA", File.ReadAllLines(restoredPath, Encoding.UTF8)[1]);
    }

    [Fact]
    public void Cli_PassphraseFromEnvironment_Works()
    {
        var inputPath = Path.Combine(_tempDir, "env-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "env-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "env.obf");
        File.WriteAllLines(inputPath, ["CustomerId", "ALPHA"], Encoding.UTF8);
        var previous = Environment.GetEnvironmentVariable("OBFUSCATOR_PASSPHRASE");
        Environment.SetEnvironmentVariable("OBFUSCATOR_PASSPHRASE", "from-env");
        try
        {
            var exit = ObfuscatorCliProgram.Run(
            [
                "obfuscate",
                "--input", inputPath,
                "--output", obfuscatedPath,
                "--manifest", manifestPath
            ]);
            Assert.Equal(0, exit);
            Assert.StartsWith("OBF_AESGCM_V3\n", File.ReadAllText(manifestPath, Encoding.UTF8), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OBFUSCATOR_PASSPHRASE", previous);
        }
    }

    [Fact]
    public void Cli_PassphraseFromStdin_Works()
    {
        var inputPath = Path.Combine(_tempDir, "stdin-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "stdin-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "stdin.obf");
        File.WriteAllLines(inputPath, ["CustomerId", "ALPHA"], Encoding.UTF8);
        var original = Console.In;
        Console.SetIn(new StringReader("from-stdin\n"));
        try
        {
            var exit = ObfuscatorCliProgram.Run(
            [
                "obfuscate",
                "--input", inputPath,
                "--output", obfuscatedPath,
                "--manifest", manifestPath,
                "--passphrase-stdin"
            ]);
            Assert.Equal(0, exit);
            Assert.StartsWith("OBF_AESGCM_V3\n", File.ReadAllText(manifestPath, Encoding.UTF8), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetIn(original);
        }
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

        Assert.Contains("checksum mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    public void GenerateCsv_SemiconductorExampleConfig_MatchesCommittedFixture()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "ExampleConfigSemiconductor.json");
        var goldenPath = Path.Combine(AppContext.BaseDirectory, "ExampleConfigSemiconductor.golden.csv");
        var outputPath = Path.Combine(_tempDir, "semi-golden.csv");
        _obfuscator.GenerateCsvFromConfig(configPath, outputPath);
        Assert.Equal(File.ReadAllBytes(goldenPath), File.ReadAllBytes(outputPath));
    }

    [SkippableFact]
    public void Manifest_GpgRoundTrip_RestoresOriginal()
    {
        Skip.If(!GpgAvailable(), "gpg is not on PATH");

        var gnupg = Path.Combine(Path.GetTempPath(), "r9g" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(gnupg);
        var previous = Environment.GetEnvironmentVariable("GNUPGHOME");
        Environment.SetEnvironmentVariable("GNUPGHOME", gnupg);
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(
                    gnupg,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            GenerateTestGpgKey("r9-test@example.invalid", gnupg);

            var inputPath = Path.Combine(_tempDir, "gpg-input.csv");
            var obfuscatedPath = Path.Combine(_tempDir, "gpg-obfuscated.csv");
            var restoredPath = Path.Combine(_tempDir, "gpg-restored.csv");
            var manifestPath = Path.Combine(_tempDir, "gpg.obf");
            File.WriteAllLines(inputPath, ["CustomerId,Region", "ALPHA,west"], Encoding.UTF8);

            _obfuscator.ObfuscateCsv(
                inputPath,
                obfuscatedPath,
                manifestPath,
                new ObfuscationOptions { GpgRecipients = ["r9-test@example.invalid"] });

            var header = File.ReadAllText(manifestPath, Encoding.UTF8);
            Assert.StartsWith("OBF_GPG_V2\n", header, StringComparison.Ordinal);

            _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath);
            Assert.Equal(
                File.ReadAllLines(inputPath, Encoding.UTF8),
                File.ReadAllLines(restoredPath, Encoding.UTF8));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GNUPGHOME", previous);
            try
            {
                if (Directory.Exists(gnupg))
                    Directory.Delete(gnupg, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void Manifest_UnrecognizedContent_ThrowsWithFileName()
    {
        var obfuscatedPath = Path.Combine(_tempDir, "unrec-obfuscated.csv");
        var restoredPath = Path.Combine(_tempDir, "unrec-restored.csv");
        var manifestPath = Path.Combine(_tempDir, "unrec.obf");
        File.WriteAllText(obfuscatedPath, "CustomerId\nALPHA\n", Encoding.UTF8);
        File.WriteAllText(manifestPath, "hello", Encoding.UTF8);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath));
        Assert.Contains("Unrecognized manifest format", ex.Message, StringComparison.Ordinal);
        Assert.Contains(manifestPath, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_TruncatedAesPayload_ThrowsClearly()
    {
        var obfuscatedPath = Path.Combine(_tempDir, "trunc-obfuscated.csv");
        var restoredPath = Path.Combine(_tempDir, "trunc-restored.csv");
        var manifestPath = Path.Combine(_tempDir, "trunc.obf");
        File.WriteAllText(obfuscatedPath, "CustomerId\nALPHA\n", Encoding.UTF8);
        File.WriteAllText(
            manifestPath,
            "OBF_AES_V2\n" + Convert.ToBase64String(new byte[8]),
            Encoding.UTF8);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath, passphrase: "secret"));
        Assert.IsNotType<ArgumentOutOfRangeException>(ex.InnerException);
        Assert.Contains("AES", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Obfuscate_WhitespacePassphrase_Throws()
    {
        var inputPath = Path.Combine(_tempDir, "ws-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "ws-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "ws.obf");
        File.WriteAllLines(inputPath, ["CustomerId,Region", "ALPHA,west"], Encoding.UTF8);

        Assert.ThrowsAny<Exception>(() =>
            _obfuscator.ObfuscateCsv(
                inputPath,
                obfuscatedPath,
                manifestPath,
                new ObfuscationOptions { Passphrase = "   " }));

        if (File.Exists(manifestPath))
            Assert.DoesNotContain("OBF_PLAIN_V2", File.ReadAllText(manifestPath, Encoding.UTF8), StringComparison.Ordinal);
    }

    [Fact]
    public void Obfuscate_AesPassphrase_ManifestIsEncrypted()
    {
        var inputPath = Path.Combine(_tempDir, "aes-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "aes-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "aes.obf");
        File.WriteAllLines(inputPath, ["CustomerId,Region", "ALPHA,west"], Encoding.UTF8);

        _obfuscator.ObfuscateCsv(
            inputPath,
            obfuscatedPath,
            manifestPath,
            new ObfuscationOptions { Passphrase = "secret" });

        var text = File.ReadAllText(manifestPath, Encoding.UTF8);
        Assert.StartsWith("OBF_AESGCM_V3\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ALPHA", text, StringComparison.Ordinal);
        Assert.DoesNotContain("west", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Obfuscate_AesRoundTrip_RestoresOriginal()
    {
        var inputPath = Path.Combine(_tempDir, "aes-rt-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "aes-rt-obfuscated.csv");
        var restoredPath = Path.Combine(_tempDir, "aes-rt-restored.csv");
        var manifestPath = Path.Combine(_tempDir, "aes-rt.obf");
        File.WriteAllLines(inputPath, ["CustomerId,Region", "ALPHA,west"], Encoding.UTF8);

        _obfuscator.ObfuscateCsv(
            inputPath,
            obfuscatedPath,
            manifestPath,
            new ObfuscationOptions { Passphrase = "secret" });
        _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath, passphrase: "secret");

        Assert.Equal(
            File.ReadAllLines(inputPath, Encoding.UTF8),
            File.ReadAllLines(restoredPath, Encoding.UTF8));
    }

    [Fact]
    public void Obfuscate_WrongPassphrase_Throws()
    {
        var inputPath = Path.Combine(_tempDir, "aes-bad-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "aes-bad-obfuscated.csv");
        var restoredPath = Path.Combine(_tempDir, "aes-bad-restored.csv");
        var manifestPath = Path.Combine(_tempDir, "aes-bad.obf");
        File.WriteAllLines(inputPath, ["CustomerId,Region", "ALPHA,west"], Encoding.UTF8);

        _obfuscator.ObfuscateCsv(
            inputPath,
            obfuscatedPath,
            manifestPath,
            new ObfuscationOptions { Passphrase = "secret" });

        Assert.ThrowsAny<Exception>(() =>
            _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath, passphrase: "wrong"));
    }

    [SkippableFact]
    public void Obfuscate_GpgFailure_LeavesNoFileAtTargetPath()
    {
        Skip.If(!GpgAvailable(), "gpg is not on PATH");

        var inputPath = Path.Combine(_tempDir, "gpg-fail-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "gpg-fail-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "gpg-fail.obf");
        File.WriteAllLines(inputPath, ["CustomerId,Region", "ALPHA,west"], Encoding.UTF8);

        Assert.ThrowsAny<Exception>(() =>
            _obfuscator.ObfuscateCsv(
                inputPath,
                obfuscatedPath,
                manifestPath,
                new ObfuscationOptions { GpgRecipients = ["nobody@invalid"] }));

        Assert.False(File.Exists(manifestPath), manifestPath);
    }

    [Fact]
    public void Cli_PassphraseFollowedByAnotherFlag_Fails()
    {
        var inputPath = Path.Combine(_tempDir, "cli-pp-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "cli-pp-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "cli-pp.obf");
        File.WriteAllLines(inputPath, ["CustomerId,Region", "ALPHA,west"], Encoding.UTF8);

        using var stderr = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(stderr);
        try
        {
            var exit = ObfuscatorCliProgram.Run(
            [
                "obfuscate",
                "--input", inputPath,
                "--output", obfuscatedPath,
                "--manifest", manifestPath,
                "--passphrase",
                "--create-output-dir"
            ]);
            Assert.NotEqual(0, exit);
            Assert.Contains("--passphrase", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Obfuscate_ValueContradictingInferredKind_IsNotPassedThrough()
    {
        var inputPath = Path.Combine(_tempDir, "mixed-kind-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "mixed-kind-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "mixed-kind.obf");
        WriteMixedKindCsv(inputPath);

        _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath);

        var text = File.ReadAllText(obfuscatedPath, Encoding.UTF8);
        Assert.DoesNotContain("LOT-SECRET-ABC", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Obfuscate_MixedTypeColumn_ReportsCount()
    {
        var inputPath = Path.Combine(_tempDir, "mixed-count-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "mixed-count-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "mixed-count.obf");
        WriteMixedKindCsv(inputPath);

        var manifest = _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath);
        Assert.True(manifest.UnparsedValueCounts.TryGetValue("Serial", out var n) && n > 0, "Serial unparsed count");

        var strictPath = Path.Combine(_tempDir, "mixed-strict-obfuscated.csv");
        var strictManifest = Path.Combine(_tempDir, "mixed-strict.obf");
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _obfuscator.ObfuscateCsv(
                inputPath,
                strictPath,
                strictManifest,
                new ObfuscationOptions { Strict = true }));
        Assert.Contains("Serial", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(strictPath));
    }

    [Fact]
    public void Obfuscate_BlankLinesPreserved()
    {
        var inputPath = Path.Combine(_tempDir, "blank-line-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "blank-line-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "blank-line.obf");
        File.WriteAllText(inputPath, "A,B\n1,2\n\n3,4\n", Encoding.UTF8);
        var inputLines = File.ReadAllLines(inputPath, Encoding.UTF8).Length;

        _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath);

        Assert.Equal(inputLines, File.ReadAllLines(obfuscatedPath, Encoding.UTF8).Length);
    }

    [Fact]
    public void Obfuscate_DateAtMaxValue_DoesNotThrow()
    {
        var inputPath = Path.Combine(_tempDir, "date-max-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "date-max-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "date-max.obf");
        File.WriteAllLines(inputPath, ["Expires", "9999-12-31"], Encoding.UTF8);

        _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath);
        Assert.True(File.Exists(obfuscatedPath));
        Assert.True(new FileInfo(obfuscatedPath).Length > 0);
    }

    [Fact]
    public void TransformCsv_InputEqualsOutput_Throws()
    {
        var csv = Path.Combine(_tempDir, "same-path.csv");
        File.WriteAllText(csv, "Name\nAlice\n", Encoding.UTF8);
        var before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(csv)));
        var manifest = Path.Combine(_tempDir, "same-path.obf");

        Assert.ThrowsAny<Exception>(() => _obfuscator.ObfuscateCsv(csv, csv, manifest));

        Assert.True(File.Exists(csv));
        var after = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(csv)));
        Assert.Equal(before, after);
    }

    [Fact]
    public void TransformCsv_InputEqualsOutput_DifferentCasing_Throws()
    {
        var csv = Path.Combine(_tempDir, "rel-path.csv");
        File.WriteAllText(csv, "Name\nAlice\n", Encoding.UTF8);
        var dotted = Path.Combine(_tempDir, ".", "rel-path.csv");
        var before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(csv)));
        var manifest = Path.Combine(_tempDir, "rel-path.obf");

        Assert.ThrowsAny<Exception>(() => _obfuscator.ObfuscateCsv(csv, dotted, manifest));

        Assert.True(File.Exists(csv));
        Assert.Equal(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(csv))));
    }

    [Fact]
    public void Obfuscate_ExistingOutput_WithoutForce_ThrowsAndPreservesFile()
    {
        var input = Path.Combine(_tempDir, "keep-in.csv");
        var output = Path.Combine(_tempDir, "keep-out.csv");
        var manifest = Path.Combine(_tempDir, "keep.obf");
        File.WriteAllText(input, "Name\nAlice\n", Encoding.UTF8);
        var payload = Encoding.UTF8.GetBytes("KEEP-EXISTING-OUTPUT");
        File.WriteAllBytes(output, payload);
        var before = Convert.ToHexString(SHA256.HashData(payload));

        Assert.ThrowsAny<Exception>(() => _obfuscator.ObfuscateCsv(input, output, manifest));

        Assert.Equal(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))));
    }

    [Fact]
    public void Obfuscate_ExistingOutput_WithForce_Overwrites()
    {
        var input = Path.Combine(_tempDir, "force-in.csv");
        var output = Path.Combine(_tempDir, "force-out.csv");
        var manifest = Path.Combine(_tempDir, "force.obf");
        File.WriteAllText(input, "Name\nAlice\n", Encoding.UTF8);
        File.WriteAllText(output, "KEEP-EXISTING-OUTPUT", Encoding.UTF8);

        _obfuscator.ObfuscateCsv(input, output, manifest, new ObfuscationOptions { Force = true });

        Assert.True(File.Exists(output));
        var text = File.ReadAllText(output, Encoding.UTF8);
        Assert.DoesNotContain("KEEP-EXISTING-OUTPUT", text, StringComparison.Ordinal);
        Assert.Contains("Name", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Obfuscate_PartialFailure_PreservesPreExistingOutput()
    {
        var input = Path.Combine(_tempDir, "preexist-input.csv");
        var output = Path.Combine(_tempDir, "preexist-obfuscated.csv");
        var manifest = Path.Combine(_tempDir, "preexist.obf");
        File.WriteAllLines(input, ["N", "1", "9007199254740993"], Encoding.UTF8);
        var payload = Encoding.UTF8.GetBytes("KEEP-EXISTING-OUTPUT");
        File.WriteAllBytes(output, payload);
        var before = Convert.ToHexString(SHA256.HashData(payload));

        Assert.ThrowsAny<Exception>(() =>
            _obfuscator.ObfuscateCsv(input, output, manifest, new ObfuscationOptions { Force = true }));

        Assert.True(File.Exists(output));
        Assert.Equal(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))));
    }

    [Fact]
    public void Obfuscate_SemicolonDelimitedInput_RoundTripsDelimiter()
    {
        var input = Path.Combine(_tempDir, "semi-in.csv");
        var obfuscated = Path.Combine(_tempDir, "semi-obf.csv");
        var restored = Path.Combine(_tempDir, "semi-out.csv");
        var manifest = Path.Combine(_tempDir, "semi.obf");
        File.WriteAllText(input, "Name;City\nalice;boston\n", Encoding.UTF8);

        var written = _obfuscator.ObfuscateCsv(input, obfuscated, manifest);
        Assert.Equal(";", written.Delimiter);
        Assert.StartsWith("Name;City", File.ReadAllText(obfuscated, Encoding.UTF8), StringComparison.Ordinal);
        Assert.DoesNotContain("Name,City", File.ReadAllText(obfuscated, Encoding.UTF8), StringComparison.Ordinal);

        _obfuscator.DeobfuscateCsv(obfuscated, manifest, restored);

        var restoredText = File.ReadAllText(restored, Encoding.UTF8);
        Assert.Contains(';', restoredText);
        Assert.DoesNotContain("alice,boston", restoredText, StringComparison.Ordinal);
        Assert.Contains("alice;boston", restoredText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_ExistingFile_WithoutForce_Throws()
    {
        var input = Path.Combine(_tempDir, "man-in.csv");
        var output = Path.Combine(_tempDir, "man-out.csv");
        var manifest = Path.Combine(_tempDir, "man.obf");
        File.WriteAllText(input, "Name\nAlice\n", Encoding.UTF8);
        var payload = Encoding.UTF8.GetBytes("KEEP-EXISTING-MANIFEST");
        File.WriteAllBytes(manifest, payload);
        var before = Convert.ToHexString(SHA256.HashData(payload));

        Assert.ThrowsAny<Exception>(() => _obfuscator.ObfuscateCsv(input, output, manifest));

        Assert.Equal(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifest))));
    }

    [Fact]
    public void Obfuscate_PartialFailure_DeletesOutput()
    {
        var inputPath = Path.Combine(_tempDir, "partial-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "partial-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "partial.obf");
        File.WriteAllLines(inputPath, ["N", "1", "9007199254740993"], Encoding.UTF8);

        Assert.ThrowsAny<Exception>(() =>
            _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath));
        Assert.False(File.Exists(obfuscatedPath), obfuscatedPath);
    }

    [Fact]
    public void Obfuscate_SmallDoubleValues_RoundTripWithinTolerance()
    {
        // A linear map over double cannot be bit-exact; 1e-9 relative (or 1e-12 abs) is the contract.
        var inputPath = Path.Combine(_tempDir, "dbl-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "dbl-obfuscated.csv");
        var restoredPath = Path.Combine(_tempDir, "dbl-restored.csv");
        var manifestPath = Path.Combine(_tempDir, "dbl.obf");
        File.WriteAllLines(inputPath, ["Meas", "3.3"], Encoding.UTF8);

        _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath);
        _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath);

        var restored = File.ReadAllLines(restoredPath, Encoding.UTF8)[1];
        var x = double.Parse(restored, CultureInfo.InvariantCulture);
        var err = Math.Abs(x - 3.3);
        Assert.True(err <= 1e-12 || err / 3.3 <= 1e-9, $"restored {x}, abs err {err}");
        Assert.DoesNotContain("Infinity", File.ReadAllText(obfuscatedPath, Encoding.UTF8), StringComparison.Ordinal);
    }

    [Fact]
    public void Obfuscate_IntegerZero_RestoresAsZero()
    {
        var inputPath = Path.Combine(_tempDir, "zero-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "zero-obfuscated.csv");
        var restoredPath = Path.Combine(_tempDir, "zero-restored.csv");
        var manifestPath = Path.Combine(_tempDir, "zero.obf");
        File.WriteAllLines(inputPath, ["N", "0"], Encoding.UTF8);

        _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath);
        _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath);

        Assert.Equal("0", File.ReadAllLines(restoredPath, Encoding.UTF8)[1]);
    }

    [Fact]
    public void Obfuscate_CaseDuplicateColumns_ThrowsClearly()
    {
        var inputPath = Path.Combine(_tempDir, "dup-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "dup-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "dup.obf");
        File.WriteAllLines(inputPath, ["Lot,lot", "A,B"], Encoding.UTF8);

        var ex = Assert.ThrowsAny<Exception>(() =>
            _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath));
        Assert.Contains("Lot", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lot", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("same key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deobfuscate_ManifestFromDifferentFile_ThrowsUnlessOverridden()
    {
        var inputA = Path.Combine(_tempDir, "src-a.csv");
        var outA = Path.Combine(_tempDir, "out-a.csv");
        var manA = Path.Combine(_tempDir, "a.obf");
        var inputB = Path.Combine(_tempDir, "src-b.csv");
        var outB = Path.Combine(_tempDir, "out-b.csv");
        var manB = Path.Combine(_tempDir, "b.obf");
        var restored = Path.Combine(_tempDir, "restored-mismatch.csv");
        File.WriteAllLines(inputA, ["N", "1"], Encoding.UTF8);
        File.WriteAllLines(inputB, ["N", "2"], Encoding.UTF8);

        _obfuscator.ObfuscateCsv(inputA, outA, manA);
        _obfuscator.ObfuscateCsv(inputB, outB, manB);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _obfuscator.DeobfuscateCsv(outB, manA, restored));
        Assert.Contains("out-b.csv", ex.Message, StringComparison.Ordinal);
        Assert.Contains("out-a.csv", ex.Message, StringComparison.Ordinal);

        _obfuscator.DeobfuscateCsv(outB, manA, restored, allowMismatchedSource: true);
        Assert.True(File.Exists(restored));
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

    private static void WriteMixedKindCsv(string path)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("Serial,Val");
        for (var i = 1; i <= 250; i++)
            writer.WriteLine($"10000{i},1.5");
        writer.WriteLine("LOT-SECRET-ABC,1.5");
    }

    private static bool GpgAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gpg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("--version");
            using var p = Process.Start(psi);
            if (p is null)
                return false;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void GenerateTestGpgKey(string uid, string homedir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "gpg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("--homedir");
        psi.ArgumentList.Add(homedir);
        psi.ArgumentList.Add("--batch");
        psi.ArgumentList.Add("--yes");
        psi.ArgumentList.Add("--pinentry-mode");
        psi.ArgumentList.Add("loopback");
        psi.ArgumentList.Add("--passphrase");
        psi.ArgumentList.Add("");
        psi.ArgumentList.Add("--quick-generate-key");
        psi.ArgumentList.Add(uid);
        psi.ArgumentList.Add("default");
        psi.ArgumentList.Add("default");
        psi.ArgumentList.Add("never");
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start gpg");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"gpg keygen failed with exit {p.ExitCode}{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
    }
}
