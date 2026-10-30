// SPDX-License-Identifier: MPL-2.0
/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
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
    public void GenerateCsvFromConfig_WithExactSweepIdRanges_UsesEveryIdOncePerColumn()
    {
        var configPath = WriteConfig("exact-id-ranges.json", """
        {
          "rowMode": "max",
          "nRows": 12,
          "sweepAxes": [ { "name": "T" }, { "name": "P" } ],
          "columns": [
            { "name": "GrpA", "dataType": "int", "totalRangeMin": 1, "totalRangeMax": 3, "generatedIdMode": "outer-group" },
            { "name": "GrpB", "dataType": "int", "totalRangeMin": 100, "totalRangeMax": 102, "generatedIdMode": "outer-group" },
            { "name": "Step", "dataType": "int", "totalRangeMin": 7, "totalRangeMax": 8, "generatedIdMode": "inner-step" },
            { "name": "T", "dataType": "string", "values": [ "a", "b", "c" ] },
            { "name": "P", "dataType": "string", "values": [ "x", "y" ] }
          ]
        }
        """);
        var outputPath = Path.Combine(_tempDir, "exact-id-ranges.csv");

        _obfuscator.GenerateCsvFromConfig(configPath, outputPath);

        var rows = File.ReadAllLines(outputPath)[1..].Select(line => string.Join(",", line.Split(',')[..3]));
        Assert.Equal(
            [
                "1,100,7", "1,100,8", "2,101,7", "2,101,8", "3,102,7", "3,102,8",
                "1,100,7", "1,100,8", "2,101,7", "2,101,8", "3,102,7", "3,102,8"
            ],
            rows);
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
        Assert.StartsWith("OBF_TK2_", obfuscatedLines[1].Split(',')[0], StringComparison.Ordinal);
        Assert.Equal(obfuscatedLines[1].Split(',')[0], obfuscatedLines[2].Split(',')[0]);
        Assert.NotEqual(obfuscatedLines[1].Split(',')[0], obfuscatedLines[3].Split(',')[0]);
    }

    [Fact]
    public void DeterministicKey_SameKeyAcrossRuns_ProducesDifferentTokens()
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

    [Theory]
    [InlineData("", "bb1d6929e95937287fa37d129b756746")]
    [InlineData("6bc1bee22e409f96e93d7e117393172a", "070a16b46b4d4144f79bdd9dd04a287c")]
    [InlineData("6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411", "dfa66747de9ae63030ca32611497c827")]
    [InlineData("6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710", "51f0bebf7e3b9d92fc49741779363cfe")]
    public void AesCmac_Rfc4493Vectors(string messageHex, string expectedHex)
    {
        // AesSiv keys CMAC with the leftmost half of its key; the rightmost half is unused here.
        var key = Convert.FromHexString("2b7e151628aed2a6abf7158809cf4f3c" + new string('0', 32));
        using var siv = new AesSiv(key);

        Assert.Equal(expectedHex, Convert.ToHexString(siv.Cmac(Convert.FromHexString(messageHex))), ignoreCase: true);
    }

    [Fact]
    public void AesSiv_Rfc5297A1_DeterministicVector()
    {
        using var siv = new AesSiv(Convert.FromHexString("fffefdfcfbfaf9f8f7f6f5f4f3f2f1f0f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff"));
        var ad = Convert.FromHexString("101112131415161718191a1b1c1d1e1f2021222324252627");
        var plaintext = Convert.FromHexString("112233445566778899aabbccddee");
        var expected = "85632d07c6e8f37f950acd320a2ecc9340c02b9690c4dc04daef7f6afe5c";

        var output = siv.Encrypt(plaintext, ad);

        Assert.Equal(expected, Convert.ToHexString(output), ignoreCase: true);
        Assert.Equal(plaintext, siv.Decrypt(output, ad));
    }

    [Fact]
    public void AesSiv_Rfc5297A2_MultipleAssociatedDataVector()
    {
        using var siv = new AesSiv(Convert.FromHexString("7f7e7d7c7b7a79787776757473727170404142434445464748494a4b4c4d4e4f"));
        var ad1 = Convert.FromHexString("00112233445566778899aabbccddeeffdeaddadadeaddadaffeeddccbbaa99887766554433221100");
        var ad2 = Convert.FromHexString("102030405060708090a0");
        var nonce = Convert.FromHexString("09f911029d74e35bd84156c5635688c0");
        var plaintext = Convert.FromHexString("7468697320697320736f6d6520706c61696e7465787420746f20656e6372797074207573696e67205349562d414553");
        var expected = "7bdb6e3b432667eb06f4d14bff2fbd0fcb900f2fddbe404326601965c889bf17dba77ceb094fa663b7a3f748ba8af829ea64ad544a272e9c485b62a3fd5c0d";

        var output = siv.Encrypt(plaintext, ad1, ad2, nonce);

        Assert.Equal(expected, Convert.ToHexString(output), ignoreCase: true);
        Assert.Equal(plaintext, siv.Decrypt(output, ad1, ad2, nonce));
    }

    [Fact]
    public void AesSiv_TamperedInputOrAssociatedData_Throws()
    {
        using var siv = new AesSiv(Convert.FromHexString("fffefdfcfbfaf9f8f7f6f5f4f3f2f1f0f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff"));
        var ad = "column"u8.ToArray();
        var output = siv.Encrypt("a value longer than one block"u8, ad);

        for (var i = 0; i < output.Length; i++)
        {
            var tampered = (byte[])output.Clone();
            tampered[i] ^= 0x01;
            Assert.Throws<CryptographicException>(() => siv.Decrypt(tampered, ad));
        }

        Assert.Throws<CryptographicException>(() => siv.Decrypt(output, "other"u8.ToArray()));
        Assert.Throws<CryptographicException>(() => siv.Decrypt(output.AsSpan(0, 15), ad));
    }

    [Fact]
    public void DeterministicToken_EqualityDistinctnessAndColumnSeparation()
    {
        var (lines, manifest) = ObfuscateWithTokens("token-scope", ["A,B", "ALPHA,ALPHA", "ALPHA,BETA", "BETA,ALPHA"]);
        var rows = lines.Skip(1).Select(l => l.Split(',')).ToList();

        Assert.Equal(rows[0][0], rows[1][0]);
        Assert.NotEqual(rows[0][0], rows[2][0]);
        Assert.Equal(rows[0][1], rows[2][1]);
        // The same value in different columns must not be joinable.
        Assert.NotEqual(rows[0][0], rows[0][1]);
        Assert.Equal("2.2", manifest.Version);
        Assert.All(manifest.Columns, c => Assert.Equal("aes-siv-cmac-256/v1", c.TokenScheme));
    }

    [Fact]
    public void DeterministicToken_SharedPlaintextPrefix_DoesNotShareTokenPrefix()
    {
        // Under the old fixed-IV CBC tokens these two values shared their first ciphertext block.
        var (lines, _) = ObfuscateWithTokens("token-prefix", ["Id", "CUSTOMER-0000000000000001", "CUSTOMER-0000000000000002"]);
        var a = DecodeTokenPayload(lines[1]);
        var b = DecodeTokenPayload(lines[2]);

        Assert.False(a.AsSpan(0, 16).SequenceEqual(b.AsSpan(0, 16)));
        Assert.False(a.AsSpan(16, 16).SequenceEqual(b.AsSpan(16, 16)));
    }

    [Theory]
    [InlineData("abcdefghijklmno", 16)]
    [InlineData("abcdefghijklmnop", 32)]
    [InlineData("abcdefghijklmnopq", 32)]
    [InlineData("Zürich 東京 ✓", 32)]
    [InlineData("x", 16)]
    public void DeterministicToken_RoundTripsAndPadsTo16ByteBuckets(string value, int ciphertextLength)
    {
        var inputPath = Path.Combine(_tempDir, "token-pad-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "token-pad-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "token-pad.obf");
        var restoredPath = Path.Combine(_tempDir, "token-pad-restored.csv");
        File.WriteAllLines(inputPath, ["Value", value], Encoding.UTF8);
        _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath, TokenOptions());

        _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath, deterministicKey: "unit-test-key");

        Assert.Equal(16 + ciphertextLength, DecodeTokenPayload(File.ReadAllLines(obfuscatedPath, Encoding.UTF8)[1]).Length);
        Assert.Equal(File.ReadAllLines(inputPath, Encoding.UTF8), File.ReadAllLines(restoredPath, Encoding.UTF8));
    }

    [Fact]
    public void DeterministicToken_TamperedToken_ThrowsWithoutEchoingValues()
    {
        var obfuscatedPath = Path.Combine(_tempDir, "token-tamper-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "token-tamper.obf");
        var inputPath = Path.Combine(_tempDir, "token-tamper-input.csv");
        File.WriteAllLines(inputPath, ["CustomerId", "SECRET-CUSTOMER"], Encoding.UTF8);
        _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath, TokenOptions());

        var lines = File.ReadAllLines(obfuscatedPath, Encoding.UTF8);
        var token = lines[1];
        var i = "OBF_TK2_".Length + 4;
        lines[1] = token[..i] + (token[i] == 'A' ? 'B' : 'A') + token[(i + 1)..];
        File.WriteAllLines(obfuscatedPath, lines, Encoding.UTF8);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, Path.Combine(_tempDir, "token-tamper-restored.csv"), deterministicKey: "unit-test-key"));

        Assert.Contains("failed authentication", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(lines[1], ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(token, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeterministicToken_WrongKey_Throws()
    {
        var obfuscatedPath = Path.Combine(_tempDir, "token-wrong-key-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "token-wrong-key.obf");
        var inputPath = Path.Combine(_tempDir, "token-wrong-key-input.csv");
        File.WriteAllLines(inputPath, ["CustomerId", "ALPHA"], Encoding.UTF8);
        _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath, TokenOptions());

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, Path.Combine(_tempDir, "token-wrong-key-restored.csv"), deterministicKey: "other-key"));

        Assert.Contains("failed authentication", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeterministicToken_LegacyCbcManifest_StillDeobfuscates()
    {
        // Fixture written by the pre-AES-SIV build (main @ fc19ca7) with key "unit-test-key".
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var restoredPath = Path.Combine(_tempDir, "legacy-restored.csv");

        _obfuscator.DeobfuscateCsv(
            Path.Combine(fixtures, "legacy-token-v2.csv"),
            Path.Combine(fixtures, "legacy-token-v2.obf"),
            restoredPath,
            deterministicKey: "unit-test-key");

        Assert.Equal(
            File.ReadAllLines(Path.Combine(fixtures, "legacy-token-v2.source.csv"), Encoding.UTF8),
            File.ReadAllLines(restoredPath, Encoding.UTF8));
    }

    private static ObfuscationOptions TokenOptions() => new()
    {
        DeterministicKey = "unit-test-key",
        StringMode = StringObfuscationMode.DeterministicToken
    };

    private (string[] Lines, ObfuscationManifest Manifest) ObfuscateWithTokens(string name, string[] csvLines)
    {
        var inputPath = Path.Combine(_tempDir, name + "-input.csv");
        var outputPath = Path.Combine(_tempDir, name + "-output.csv");
        File.WriteAllLines(inputPath, csvLines, Encoding.UTF8);
        var manifest = _obfuscator.ObfuscateCsv(inputPath, outputPath, Path.Combine(_tempDir, name + ".obf"), TokenOptions());
        return (File.ReadAllLines(outputPath, Encoding.UTF8), manifest);
    }

    private static byte[] DecodeTokenPayload(string token)
    {
        Assert.StartsWith("OBF_TK2_", token, StringComparison.Ordinal);
        var b64 = token["OBF_TK2_".Length..].Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b64.PadRight(b64.Length + ((4 - (b64.Length % 4)) % 4), '='));
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
    public void Manifest_AesCbcV2_IsUnsupported()
    {
        var inputPath = Path.Combine(_tempDir, "v2-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "v2-obfuscated.csv");
        var restoredPath = Path.Combine(_tempDir, "v2-restored.csv");
        var manifestPath = Path.Combine(_tempDir, "v2.obf");
        File.WriteAllLines(inputPath, ["CustomerId", "ALPHA"], Encoding.UTF8);

        _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath);
        File.WriteAllText(manifestPath, "OBF_AES_V2\n" + Convert.ToBase64String(new byte[40]), Encoding.UTF8);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath, passphrase: "legacy"));
        Assert.Contains("OBF_AES_V2", ex.Message, StringComparison.Ordinal);
        Assert.Contains("unsupported", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(restoredPath));
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
    public void Cli_PassphraseFromFile_RoundTrips()
    {
        var inputPath = Path.Combine(_tempDir, "file-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "file-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "file.obf");
        var restoredPath = Path.Combine(_tempDir, "file-restored.csv");
        var passphrasePath = Path.Combine(_tempDir, "passphrase.txt");
        File.WriteAllLines(inputPath, ["CustomerId", "ALPHA"], Encoding.UTF8);
        File.WriteAllText(passphrasePath, "from-file\n");

        var exit = ObfuscatorCliProgram.Run(
        [
            "obfuscate",
            "--input", inputPath,
            "--output", obfuscatedPath,
            "--manifest", manifestPath,
            "--passphrase-file", passphrasePath
        ]);
        Assert.Equal(0, exit);
        Assert.StartsWith("OBF_AESGCM_V3\n", File.ReadAllText(manifestPath, Encoding.UTF8), StringComparison.Ordinal);

        exit = ObfuscatorCliProgram.Run(
        [
            "deobfuscate",
            "--input", obfuscatedPath,
            "--manifest", manifestPath,
            "--output", restoredPath,
            "--passphrase-file", passphrasePath
        ]);
        Assert.Equal(0, exit);
        Assert.Equal(File.ReadAllLines(inputPath, Encoding.UTF8), File.ReadAllLines(restoredPath, Encoding.UTF8));
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

        Assert.StartsWith("OBF_TK2_", obfuscatedValues[0], StringComparison.Ordinal);
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

    [Theory]
    [InlineData("duplicate-axis", "Duplicate sweep axis",
        """
        { "rowMode": "sweep", "sweepAxes": [ { "name": "P" }, { "name": "p" } ],
          "columns": [ { "name": "P", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 10, "values": [ "0", "5" ] } ] }
        """)]
    [InlineData("duplicate-axis-value", "duplicate value",
        """
        { "rowMode": "sweep", "sweepAxes": [ { "name": "Site" } ],
          "columns": [ { "name": "Site", "dataType": "string", "values": [ "A", "B", "A" ] } ] }
        """)]
    [InlineData("duplicate-numeric-axis-value", "duplicate value",
        """
        { "rowMode": "sweep", "sweepAxes": [ { "name": "P" } ],
          "columns": [ { "name": "P", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 10, "values": [ "5", "5.0" ] } ] }
        """)]
    [InlineData("non-numeric-axis-value", "is not a valid number",
        """
        { "rowMode": "sweep", "sweepAxes": [ { "name": "P" } ],
          "columns": [ { "name": "P", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 10, "values": [ "0", "five" ] } ] }
        """)]
    [InlineData("axis-value-out-of-range", "outside its total range",
        """
        { "rowMode": "sweep", "sweepAxes": [ { "name": "P" } ],
          "columns": [ { "name": "P", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 10, "values": [ "0", "11" ] } ] }
        """)]
    [InlineData("axis-is-generated-id", "cannot be both a sweep axis and a generatedIdMode column",
        """
        { "rowMode": "sweep", "sweepAxes": [ { "name": "Id" } ],
          "columns": [ { "name": "Id", "dataType": "int", "totalRangeMin": 1, "totalRangeMax": 10, "values": [ "1", "2" ], "generatedIdMode": "inner-step" } ] }
        """)]
    [InlineData("generated-id-in-fixed-mode", "requires rowMode 'sweep' or 'max'",
        """
        { "rowMode": "fixed", "nRows": 2,
          "columns": [ { "name": "Id", "dataType": "int", "totalRangeMin": 1, "totalRangeMax": 10, "generatedIdMode": "outer-group" } ] }
        """)]
    [InlineData("outer-group-range-too-small", "needs 3 IDs but its range 1..2 holds 2",
        """
        { "rowMode": "sweep", "sweepAxes": [ { "name": "T" }, { "name": "P" } ],
          "columns": [
            { "name": "Grp", "dataType": "int", "totalRangeMin": 1, "totalRangeMax": 2, "generatedIdMode": "outer-group" },
            { "name": "T", "dataType": "string", "values": [ "a", "b", "c" ] },
            { "name": "P", "dataType": "string", "values": [ "x", "y" ] } ] }
        """)]
    [InlineData("outer-group-range-too-small-max-mode", "needs 4 IDs but its range 5..7 holds 3",
        """
        { "rowMode": "max", "nRows": 20, "sweepAxes": [ { "name": "T" }, { "name": "F" }, { "name": "P" } ],
          "columns": [
            { "name": "Grp", "dataType": "int", "totalRangeMin": 5, "totalRangeMax": 7, "generatedIdMode": "outer-group" },
            { "name": "T", "dataType": "string", "values": [ "a", "b" ] },
            { "name": "F", "dataType": "string", "values": [ "c", "d" ] },
            { "name": "P", "dataType": "string", "values": [ "x" ] } ] }
        """)]
    [InlineData("inner-step-range-too-small", "needs 3 IDs but its range 1..2 holds 2",
        """
        { "rowMode": "sweep", "sweepAxes": [ { "name": "P" } ],
          "columns": [
            { "name": "Step", "dataType": "int", "totalRangeMin": 1, "totalRangeMax": 2, "generatedIdMode": "inner-step" },
            { "name": "P", "dataType": "string", "values": [ "x", "y", "z" ] } ] }
        """)]
    [InlineData("inner-step-range-too-small-max-mode", "needs 2 IDs but its range 9..9 holds 1",
        """
        { "rowMode": "max", "nRows": 10, "sweepAxes": [ { "name": "T" }, { "name": "P" } ],
          "columns": [
            { "name": "Step", "dataType": "int", "totalRangeMin": 9, "totalRangeMax": 9, "generatedIdMode": "inner-step" },
            { "name": "T", "dataType": "string", "values": [ "a", "b", "c" ] },
            { "name": "P", "dataType": "string", "values": [ "x", "y" ] } ] }
        """)]
    [InlineData("tracks-unknown-column", "references unknown column",
        """
        { "rowMode": "fixed", "nRows": 2,
          "columns": [ { "name": "A", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 1, "tracksWith": [ "Missing" ] } ] }
        """)]
    [InlineData("tracks-non-numeric-column", "references non-numeric column",
        """
        { "rowMode": "fixed", "nRows": 2,
          "columns": [
            { "name": "S", "dataType": "string", "staticValue": "x" },
            { "name": "A", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 1, "tracksInverselyWith": [ "S" ] } ] }
        """)]
    [InlineData("tracks-itself", "cannot track itself",
        """
        { "rowMode": "fixed", "nRows": 2,
          "columns": [ { "name": "A", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 1, "tracksWith": [ "a" ] } ] }
        """)]
    [InlineData("tracks-later-column", "must appear before it",
        """
        { "rowMode": "fixed", "nRows": 2,
          "columns": [
            { "name": "A", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 1, "tracksWith": [ "B" ] },
            { "name": "B", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 1 } ] }
        """)]
    [InlineData("reversed-total-range", "TotalRangeMin greater than TotalRangeMax",
        """
        { "rowMode": "fixed", "nRows": 2,
          "columns": [ { "name": "A", "dataType": "double", "totalRangeMin": 10, "totalRangeMax": 0 } ] }
        """)]
    [InlineData("one-sided-ideal-range", "both IdealRangeMin and IdealRangeMax, or neither",
        """
        { "rowMode": "fixed", "nRows": 2,
          "columns": [ { "name": "A", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 10, "idealRangeMin": 2 } ] }
        """)]
    [InlineData("ideal-range-outside-total", "ideal range must lie within its total range",
        """
        { "rowMode": "fixed", "nRows": 2,
          "columns": [ { "name": "A", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 10, "idealRangeMin": 2, "idealRangeMax": 12 } ] }
        """)]
    [InlineData("reversed-ideal-range", "IdealRangeMin greater than IdealRangeMax",
        """
        { "rowMode": "fixed", "nRows": 2,
          "columns": [ { "name": "A", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 10, "idealRangeMin": 8, "idealRangeMax": 2 } ] }
        """)]
    [InlineData("true-percentage-above-100", "TruePercentage must be between 0 and 100",
        """
        { "rowMode": "fixed", "nRows": 2,
          "columns": [ { "name": "B", "dataType": "boolean", "truePercentage": 101 } ] }
        """)]
    [InlineData("ideal-percentage-negative", "PercentageInIdealRange must be between 0 and 100",
        """
        { "rowMode": "fixed", "nRows": 2,
          "columns": [ { "name": "A", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 10, "percentageInIdealRange": -1 } ] }
        """)]
    [InlineData("int-range-beyond-type", "does not fit data type 'int'",
        """
        { "rowMode": "fixed", "nRows": 2,
          "columns": [ { "name": "A", "dataType": "int", "totalRangeMin": 0, "totalRangeMax": 3000000000 } ] }
        """)]
    [InlineData("negative-random-string-length", "RandomStringLength cannot be negative",
        """
        { "rowMode": "fixed", "nRows": 2,
          "columns": [ { "name": "S", "dataType": "string", "randomString": true, "randomStringLength": -1 } ] }
        """)]
    [InlineData("bad-date", "is not a valid date",
        """
        { "rowMode": "fixed", "nRows": 2,
          "columns": [ { "name": "D", "dataType": "datetime", "dateMinUtc": "not-a-date", "dateMaxUtc": "2026-01-01T00:00:00Z" } ] }
        """)]
    [InlineData("reversed-dates", "DateMinUtc is after DateMaxUtc",
        """
        { "rowMode": "fixed", "nRows": 2,
          "columns": [ { "name": "D", "dataType": "datetime", "dateMinUtc": "2026-01-01T00:00:00Z", "dateMaxUtc": "2025-01-01T00:00:00Z" } ] }
        """)]
    [InlineData("nrows-beyond-limit", "exceeds the limit",
        """
        { "rowMode": "fixed", "nRows": 2147483647,
          "columns": [ { "name": "A", "dataType": "double", "totalRangeMin": 0, "totalRangeMax": 1 } ] }
        """)]
    public void GetDataGenParametersFromConfig_WithInvalidConfig_Throws(string name, string expectedMessage, string json)
    {
        var configPath = WriteConfig(name + ".json", json);

        var ex = Assert.Throws<InvalidOperationException>(() => _obfuscator.GetDataGenParametersFromConfig(configPath));

        Assert.Contains(expectedMessage, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenerateCsv_WithNonFiniteRange_ThrowsBeforeWriting()
    {
        var outputPath = Path.Combine(_tempDir, "nan-range.csv");
        var config = new DataGenConfig
        {
            RowMode = "fixed",
            NRows = 2,
            Columns = [new ColumnGenerationSpec { Name = "A", DataType = "double", TotalRangeMin = double.NaN, TotalRangeMax = 1 }]
        };

        var ex = Assert.Throws<InvalidOperationException>(() => _obfuscator.GenerateCsv(config, outputPath));

        Assert.Contains("must be finite", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outputPath));
    }

    [Theory]
    [InlineData(8, 300, "overflows")]
    [InlineData(3, 2000, "exceeds the limit")]
    public void GenerateCsv_WithOversizedSweepExpansion_ThrowsBeforeWriting(int axisCount, int valuesPerAxis, string expectedMessage)
    {
        var outputPath = Path.Combine(_tempDir, "oversized-sweep.csv");
        var values = Enumerable.Range(0, valuesPerAxis).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
        var columns = Enumerable.Range(0, axisCount)
            .Select(i => new ColumnGenerationSpec { Name = $"Axis{i}", DataType = "string", Values = values })
            .ToList();
        var config = new DataGenConfig
        {
            RowMode = "sweep",
            SweepAxes = columns.Select(c => new SweepAxisSpec { Name = c.Name }).ToList(),
            Columns = columns
        };

        var ex = Assert.Throws<InvalidOperationException>(() => _obfuscator.GenerateCsv(config, outputPath));

        Assert.Contains("Sweep expansion", ex.Message, StringComparison.Ordinal);
        Assert.Contains(expectedMessage, ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));
    }

    [Theory]
    [InlineData("ExampleConfig.json")]
    [InlineData("ExampleConfigSemiconductor.json")]
    [InlineData("ExampleConfigSemiconductorDemo.json")]
    [InlineData("ExampleConfigTestResults.json")]
    public void GetDataGenParametersFromConfig_ExampleConfigs_PassValidation(string fileName)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, fileName);
        Assert.True(File.Exists(configPath), configPath);

        var config = _obfuscator.GetDataGenParametersFromConfig(configPath);

        Assert.True(config.Columns.Count > 0 || config.TestResults is not null);
    }

    [Theory]
    [InlineData("sweep", 0)]
    [InlineData("max", 10)]
    public void GenerateCsv_WithSweepRowModesAndSeed_IsDeterministic(string rowMode, int nRows)
    {
        var outputPath1 = Path.Combine(_tempDir, $"{rowMode}-seeded-1.csv");
        var outputPath2 = Path.Combine(_tempDir, $"{rowMode}-seeded-2.csv");
        var config = new DataGenConfig
        {
            RowMode = rowMode,
            NRows = nRows,
            Seed = 42,
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
                new ColumnGenerationSpec { Name = "Pout(dBm)", DataType = "double", TotalRangeMin = 0, TotalRangeMax = 10, Values = ["0", "5", "10"] },
                new ColumnGenerationSpec { Name = "Gain(dB)", DataType = "double", TotalRangeMin = 5, TotalRangeMax = 35, IdealRangeMin = 18, IdealRangeMax = 28, TracksWith = ["Pout(dBm)"] }
            ]
        };

        _obfuscator.GenerateCsv(config, outputPath1);
        _obfuscator.GenerateCsv(config, outputPath2);

        Assert.Equal(File.ReadAllBytes(outputPath1), File.ReadAllBytes(outputPath2));
        Assert.Equal(rowMode == "max" ? 11 : 7, File.ReadAllLines(outputPath1).Length);
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
                new ColumnGenerationSpec { Name = "Code", DataType = "string", RandomString = true, StringPrefix = "SQ_", RandomStringLength = 4 },
                new ColumnGenerationSpec { Name = "Uid", DataType = "guid" },
                new ColumnGenerationSpec { Name = "When", DataType = "datetime" }
            ]
        };

        _obfuscator.GenerateCsv(config, outputPath1);
        _obfuscator.GenerateCsv(config, outputPath2);

        Assert.Equal(File.ReadAllText(outputPath1, Encoding.UTF8), File.ReadAllText(outputPath2, Encoding.UTF8));
    }

    [Fact]
    public void Generate_SameSeed_ProducesIdenticalGuidColumns()
    {
        var config = new DataGenConfig
        {
            RowMode = "fixed",
            NRows = 8,
            Seed = 123,
            Columns = [new ColumnGenerationSpec { Name = "Id", DataType = "guid" }]
        };
        var a = Path.Combine(_tempDir, "guid-a.csv");
        var b = Path.Combine(_tempDir, "guid-b.csv");
        _obfuscator.GenerateCsv(config, a);
        _obfuscator.GenerateCsv(config, b);
        Assert.Equal(File.ReadAllText(a, Encoding.UTF8), File.ReadAllText(b, Encoding.UTF8));
    }

    [Fact]
    public void Generate_SameSeed_ProducesIdenticalDateColumns_WhenBoundsAbsent()
    {
        var config = new DataGenConfig
        {
            RowMode = "fixed",
            NRows = 8,
            Seed = 123,
            Columns = [new ColumnGenerationSpec { Name = "When", DataType = "datetime" }]
        };
        var a = Path.Combine(_tempDir, "date-a.csv");
        var b = Path.Combine(_tempDir, "date-b.csv");
        _obfuscator.GenerateCsv(config, a);
        _obfuscator.GenerateCsv(config, b);
        Assert.Equal(File.ReadAllText(a, Encoding.UTF8), File.ReadAllText(b, Encoding.UTF8));
    }

    [Fact]
    public void ObfuscateInteger_LargeInputWithHighScale_RoundTripsExactly()
    {
        const long n = 8_000_000_000_000_001L;
        var spec = new ColumnObfuscationSpec { Name = "N", Scale = 10, Shift = 0 };
        var engine = new ObfuscationEngine();
        var ex = Assert.Throws<InvalidOperationException>(
            () => engine.ObfuscateInteger(n.ToString(CultureInfo.InvariantCulture), spec));
        Assert.Contains("N", ex.Message, StringComparison.Ordinal);
        Assert.Contains("8000000000000001", ex.Message, StringComparison.Ordinal);
        Assert.Contains("2^53", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeobfuscateInteger_NonFiniteManifestValue_Throws()
    {
        var spec = new ColumnObfuscationSpec { Name = "N", Scale = 2, Shift = 1 };
        var engine = new ObfuscationEngine();
        Assert.Throws<InvalidOperationException>(() => engine.DeobfuscateInteger("NaN", spec));
        Assert.Throws<InvalidOperationException>(() => engine.DeobfuscateInteger("Infinity", spec));
    }

    [Fact]
    public void DeobfuscateInteger_ZeroScale_Throws()
    {
        var spec = new ColumnObfuscationSpec { Name = "N", Scale = 0, Shift = 1 };
        var engine = new ObfuscationEngine();
        var ex = Assert.Throws<InvalidOperationException>(() => engine.DeobfuscateInteger("10", spec));
        Assert.Contains("Scale", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FloatingColumn_RoundTripsToSameNumericValue()
    {
        var inputPath = Path.Combine(_tempDir, "float-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "float-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "float.obf");
        var restoredPath = Path.Combine(_tempDir, "float-restored.csv");
        string[] values = ["131.60538314346292", "0.1", "-2.5e-10", "1000.000001", "\"1,234.5\"", "3E2", "-0", "0.30000000000000004"];
        File.WriteAllLines(inputPath, ["Amount", .. values], Encoding.UTF8);

        _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath,
            new ObfuscationOptions { DeterministicKey = "unit-test-key" });
        _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath, deterministicKey: "unit-test-key");

        var restored = File.ReadAllLines(restoredPath, Encoding.UTF8)[1..];
        Assert.Equal(values.Length, restored.Length);
        for (var i = 0; i < values.Length; i++)
            Assert.Equal(ParseNumber(values[i]), ParseNumber(restored[i]));
    }

    [Theory]
    [InlineData(1.5, 0.0)]
    [InlineData(-9.87654321, 123456.789)]
    [InlineData(10.0, -1e-6)]
    [InlineData(-2.0000001, 3.3e15)]
    [InlineData(7.25, 1e300)]
    [InlineData(4.0, double.PositiveInfinity)]
    public void ExactFloat_RoundTripsSameNumericValueAcrossTransforms(double scale, double shift)
    {
        var spec = new ColumnObfuscationSpec { Name = "F", Kind = ObfuscatedColumnKind.Floating };
        ObfuscationEngine.SetExactFloatTransform(spec, scale, shift);
        var engine = new ObfuscationEngine();
        var random = new Random(13);
        var values = new List<string>
        {
            "0", "-0", "5e-324", "4.9406564584124654E-324", "1e-30", "1e300", "-1.7976931348623157E+308",
            "131.60538314346292", "0.30000000000000004", "1,234,567.25", " 42 ", "+.5", "7.", "1.50", "-3E-2"
        };
        for (var i = 0; i < 200; i++)
            values.Add(BitConverter.Int64BitsToDouble(random.NextInt64()).ToString("R", CultureInfo.InvariantCulture));

        foreach (var value in values.Where(v => double.IsFinite(double.Parse(v, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture))))
        {
            var obfuscated = engine.ObfuscateFloating(value, spec);
            var restored = engine.DeobfuscateFloating(obfuscated, spec);
            Assert.Equal(ExactDecimal.Parse(value).ToString(), restored);
            Assert.Equal(
                double.Parse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture),
                double.Parse(restored, NumberStyles.Float, CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void FloatingColumn_CellWhoseOutputExceedsParseBounds_RoundTrips()
    {
        var inputPath = Path.Combine(_tempDir, "float-wide.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "float-wide-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "float-wide.obf");
        var restoredPath = Path.Combine(_tempDir, "float-wide-restored.csv");
        // Each input is within ExactDecimal's bounds, but x * Scale + Shift spans more than 1000 digits.
        string[] lines = ["Amount", "1234.5", "1e-1000", "-7" + new string('3', 999)];
        File.WriteAllLines(inputPath, lines, Encoding.UTF8);

        var manifest = _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath,
            new ObfuscationOptions { DeterministicKey = "unit-test-key" });
        _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath, deterministicKey: "unit-test-key");

        Assert.Equal(2, manifest.UnparsedValueCounts["Amount"]);
        var restored = File.ReadAllLines(restoredPath, Encoding.UTF8);
        for (var i = 1; i < lines.Length; i++)
            Assert.Equal(ExactDecimal.Parse(lines[i]).ToString(), ExactDecimal.Parse(restored[i]).ToString());
    }

    [Fact]
    public void ExactFloat_QuantizesScaleAndShift()
    {
        var spec = new ColumnObfuscationSpec { Name = "F" };
        ObfuscationEngine.SetExactFloatTransform(spec, -3.141592653589793, 98765.4321);

        Assert.Equal(ObfuscationEngine.ExactFloatScheme, spec.FloatScheme);
        Assert.Equal("-3.1416", spec.ExactScale);
        Assert.Equal("98770", spec.ExactShift);
        Assert.Equal(-3.1416, spec.Scale);
        Assert.Equal(98770, spec.Shift);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("-0.000", "0")]
    [InlineData("1.50", "1.5")]
    [InlineData("1,000.25", "1000.25")]
    [InlineData("12e3", "12000")]
    [InlineData("0.000123", "0.000123")]
    [InlineData("1e300", "1E+300")]
    [InlineData("-1.25e-30", "-1.25E-30")]
    [InlineData("1234567890123456789012345", "1234567890123456789012345")]
    public void ExactDecimal_ParsesAndFormatsNormalized(string input, string expected)
    {
        Assert.True(ExactDecimal.TryParse(input, out var value));
        Assert.Equal(expected, value.ToString());
        Assert.True(ExactDecimal.TryParse(value.ToString(), out var reparsed));
        Assert.Equal(value.ToString(), reparsed.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-")]
    [InlineData(".")]
    [InlineData("1e")]
    [InlineData("1e-99999999")]
    [InlineData("1e1200")]
    [InlineData("1.2.3")]
    [InlineData(",5")]
    [InlineData("0x10")]
    public void ExactDecimal_RejectsNonNumbersAndUnboundedValues(string input)
    {
        Assert.False(ExactDecimal.TryParse(input, out _));
    }

    [Fact]
    public void ExactDecimal_TryParse_RejectsTooManyDigits()
    {
        Assert.False(ExactDecimal.TryParse(new string('7', 1001), out _));
        Assert.True(ExactDecimal.TryParse(new string('7', 1000), out _));
    }

    [Fact]
    public void ExactFloat_EditedCell_ThrowsWithoutEchoingValue()
    {
        var spec = new ColumnObfuscationSpec { Name = "F", Kind = ObfuscatedColumnKind.Floating };
        ObfuscationEngine.SetExactFloatTransform(spec, 3.0, 0.0);
        var engine = new ObfuscationEngine();

        var ex = Assert.Throws<InvalidOperationException>(() => engine.DeobfuscateFloating("1000000.0000001", spec));
        Assert.Contains("'F'", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("1000000", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactFloat_UnknownScheme_Throws()
    {
        var spec = new ColumnObfuscationSpec { Name = "F", FloatScheme = "exact-decimal/v9", ExactScale = "2", ExactShift = "1" };
        var engine = new ObfuscationEngine();

        var ex = Assert.Throws<InvalidOperationException>(() => engine.DeobfuscateFloating("3", spec));
        Assert.Contains("exact-decimal/v9", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyFloatSpec_UsesDoubleTransformAndOmitsNewFields()
    {
        var spec = new ColumnObfuscationSpec { Name = "F", Kind = ObfuscatedColumnKind.Floating, Scale = 2.5, Shift = 10 };
        var engine = new ObfuscationEngine();

        Assert.Equal((2.5 * 2.5 + 10).ToString("G17", CultureInfo.InvariantCulture), engine.ObfuscateFloating("2.5", spec));
        Assert.Equal("2.5", engine.DeobfuscateFloating("16.25", spec));

        // Pre-2.2 manifests have no such keys, so their integrity JSON must not gain them.
        var json = JsonSerializer.Serialize(spec);
        Assert.DoesNotContain("FloatScheme", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ExactScale", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ExactShift", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FloatingColumn_UnboundedCell_IsUnparsedAndRoundTrips()
    {
        var inputPath = Path.Combine(_tempDir, "float-unbounded.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "float-unbounded-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "float-unbounded.obf");
        var restoredPath = Path.Combine(_tempDir, "float-unbounded-restored.csv");
        File.WriteAllLines(inputPath, ["Amount", "1.5", "1e-99999999", "2.25"], Encoding.UTF8);

        var manifest = _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath, new ObfuscationOptions());
        _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath);

        Assert.Equal(ObfuscatedColumnKind.Floating, manifest.Columns.Single().Kind);
        Assert.Equal(1, manifest.UnparsedValueCounts["Amount"]);
        Assert.StartsWith("OBF_", File.ReadAllLines(obfuscatedPath, Encoding.UTF8)[2], StringComparison.Ordinal);
        Assert.Equal(["Amount", "1.5", "1e-99999999", "2.25"], File.ReadAllLines(restoredPath, Encoding.UTF8));
    }

    private static decimal ParseNumber(string value)
        => decimal.Parse(value.Trim('"'), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

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
        AssertSemiconductorGoldenMatches(goldenPath, outputPath);
    }

    [Fact]
    public void DemoConfig_Generate_HasFacetColumnAndMoreThanGoldenRows()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "ExampleConfigSemiconductorDemo.json");
        Assert.True(File.Exists(configPath), configPath);
        var outputPath = Path.Combine(_tempDir, "semi-demo.csv");
        _obfuscator.GenerateCsvFromConfig(configPath, outputPath);
        var lines = File.ReadAllLines(outputPath);
        Assert.True(lines.Length > 1, "expected a header plus data rows");
        var header = lines[0];
        Assert.Contains("Site", header, StringComparison.Ordinal);
        var dataRows = lines.Length - 1;
        Assert.NotEqual(81, dataRows);
        Assert.True(dataRows > 81, "demo sample must have more rows than the frozen 81-row golden");
    }

    // ---- rowMode test-results ----

    private static DataGenConfig TestResultsConfig(int devices = 5, Action<TestResultsSpec>? edit = null)
    {
        var spec = new TestResultsSpec
        {
            LineEnding = "crlf",
            Preamble = new PreambleSpec
            {
                KeyWidth = 12,
                Entries =
                [
                    new PreambleEntrySpec { Key = "Project", Kind = "static", Value = "Alpha" },
                    new PreambleEntrySpec { Key = "Opened", Kind = "date", Value = "2026-03-04T05:06:07Z", Format = "yyyy-MM-dd" },
                    new PreambleEntrySpec { Key = "Seq", Kind = "counter", Start = 100, Step = 5 },
                    new PreambleEntrySpec { Key = "Seq2", Kind = "counter", Start = 7, Step = 5 },
                    new PreambleEntrySpec { Key = "A Very Long Key Name", Kind = "static", Value = "x" }
                ]
            },
            Annotations =
            [
                new AnnotationSpec { Label = "Line", Source = "programLine" },
                new AnnotationSpec { Label = "Upper", Source = "upperLimit" },
                new AnnotationSpec { Label = "Lower", Source = "lowerLimit" },
                new AnnotationSpec { Label = "Unit", Source = "unit" }
            ],
            ProgramLineStart = 1000,
            ProgramLineStep = 10,
            DeviceColumns = new DeviceColumnsSpec
            {
                Serial = new SerialColumnSpec { Name = "SN", Width = 8, Start = 100, Step = 3 },
                Site = new FixedColumnSpec { Name = "Site", Width = 5, Value = "1" },
                ProberX = new FixedColumnSpec { Name = "X", Width = 4, Value = "7" },
                ProberY = new FixedColumnSpec { Name = "Y", Width = 4, Value = "9" },
                Bin = new BinColumnSpec { Name = "Bin", Width = 4, Weights = new Dictionary<string, double> { ["1"] = 90, ["2"] = 10 } }
            },
            ConditionSets =
            [
                new ConditionSetSpec
                {
                    Conditions = [["Vdd", "3.3"], ["Temp", "25"]],
                    Tests = [TrTest("Gain", "dB", 20, 10), TrTest("Idd", "mA", 50, 5)]
                },
                new ConditionSetSpec
                {
                    Axes =
                    [
                        new ConditionAxisSpec { Key = "Vdd", Values = ["3.3", "5.0"] },
                        new ConditionAxisSpec { Key = "Mode", Values = ["lo", "hi"] }
                    ],
                    KeyOrder = ["Mode", "Vdd"],
                    Tests = [TrTest("Gain", "dB", 20, 10)]
                }
            ]
        };
        edit?.Invoke(spec);
        return new DataGenConfig { RowMode = "test-results", NRows = devices, Seed = 11, TestResults = spec };
    }

    private static TestSpec TrTest(string name, string unit, double upper, double lower) => new()
    {
        Name = name,
        Unit = unit,
        UpperLimit = upper,
        LowerLimit = lower,
        Distribution = "uniform",
        Min = lower,
        Max = upper,
        Format = "%.3f"
    };

    // Latin-1 maps every byte to one char, so string lengths are byte lengths and raw bytes survive.
    private string[] GenerateTestResultsLines(DataGenConfig config, string name = "tr", string eol = "\r\n")
    {
        var path = Path.Combine(_tempDir, name + ".csv");
        _obfuscator.GenerateCsv(config, path, force: true);
        var text = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        Assert.EndsWith(eol, text, StringComparison.Ordinal);
        return text[..^eol.Length].Split(eol);
    }

    // Annotation, header and data lines of TestResultsConfig(): 4 + 1 + devices, after 5 preamble lines.
    private const int TrPreambleLines = 5;
    private const int TrAnnotationLines = 4;
    private const int TrHeaderIndex = TrPreambleLines + TrAnnotationLines;

    private static string[] TrCells(string line) => line.Split(',');

    [Theory]
    [InlineData("crlf", "\r\n")]
    [InlineData("lf", "\n")]
    public void GenerateCsv_TestResults_HasPreambleAnnotationsHeaderAndEqualLengthRows(string lineEnding, string eol)
    {
        var config = TestResultsConfig(7, s => s.LineEnding = lineEnding);

        var lines = GenerateTestResultsLines(config, eol: eol);

        Assert.Equal(TrPreambleLines + TrAnnotationLines + 1 + 7, lines.Length);
        Assert.StartsWith("Project", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("Line", lines[TrPreambleLines], StringComparison.Ordinal);
        Assert.StartsWith("SN", lines[TrHeaderIndex], StringComparison.Ordinal);
        Assert.StartsWith("100", lines[TrHeaderIndex + 1], StringComparison.Ordinal);
        var width = lines[TrHeaderIndex].Length;
        Assert.All(lines[TrPreambleLines..], line => Assert.Equal(width, line.Length));
        if (eol == "\n")
            Assert.DoesNotContain('\r', string.Concat(lines));
    }

    [Fact]
    public void GenerateCsv_TestResults_WritesPaddedPreambleLines()
    {
        var lines = GenerateTestResultsLines(TestResultsConfig());

        Assert.Equal("Project     :,Alpha", lines[0]);
        Assert.Equal("Opened      :,2026-03-04", lines[1]);
        Assert.Equal("Seq         :,100", lines[2]);
        Assert.Equal("Seq2        :,12", lines[3]);
        Assert.Equal("A Very Long Key Name:,x", lines[4]);
    }

    [Fact]
    public void GenerateCsv_TestResults_InjectsBytesIntoTheNamedKeyOnly()
    {
        var config = TestResultsConfig(3, s =>
        {
            s.Preamble!.Inject =
            [
                new PreambleInjectSpec { Key = "Project", Offset = 2, Bytes = [0xFF] },
                new PreambleInjectSpec { Key = "Seq", Offset = 1, Bytes = [0x13, 0x13] }
            ];
        });
        var path = Path.Combine(_tempDir, "tr-inject.csv");
        _obfuscator.GenerateCsv(config, path);

        var bytes = File.ReadAllBytes(path);
        var lines = Encoding.Latin1.GetString(bytes).Split("\r\n");

        Assert.Equal("Prÿject     :,Alpha", lines[0]);
        Assert.Equal("S\u0013\u0013 " + "        :,100", lines[2]);
        Assert.Equal(1, bytes.Count(b => b == 0xFF));
        Assert.Equal(2, bytes.Count(b => b == 0x13));
        // Every other line is clean ASCII text.
        var clean = string.Join("\r\n", lines.Where((_, i) => i is not (0 or 2)));
        Assert.All(clean.Where(c => c != '\r' && c != '\n'), c => Assert.InRange(c, ' ', '~'));
        Assert.ThrowsAny<Exception>(() => new UTF8Encoding(false, true).GetString(bytes));
    }

    [Fact]
    public void GenerateCsv_TestResults_WritesAnnotationRowsPaddedToTheFirstDeviceColumn()
    {
        var lines = GenerateTestResultsLines(TestResultsConfig());
        var rows = lines[TrPreambleLines..TrHeaderIndex].Select(TrCells).ToArray();
        var header = TrCells(lines[TrHeaderIndex]);

        // Serial column is 8 wide: label padded to 7, then ":".
        Assert.Equal("Line   :", rows[0][0]);
        Assert.Equal("Upper  :", rows[1][0]);
        Assert.Equal("Lower  :", rows[2][0]);
        Assert.Equal("Unit   :", rows[3][0]);
        foreach (var row in rows)
        {
            Assert.Equal(header.Length, row.Length);
            Assert.Equal(new[] { 5, 4, 4, 4 }, row[1..5].Select(c => c.Length));
            Assert.All(row[1..5], c => Assert.Equal(string.Empty, c.Trim()));
        }

        Assert.Equal(new[] { "1000", "1010", "1020", "1030", "1040", "1050" }, rows[0][5..].Select(c => c.Trim()));
        Assert.Equal(new[] { "20.000", "50.000", "20.000", "20.000", "20.000", "20.000" }, rows[1][5..].Select(c => c.Trim()));
        Assert.Equal(new[] { "10.000", "5.000", "10.000", "10.000", "10.000", "10.000" }, rows[2][5..].Select(c => c.Trim()));
        Assert.Equal(new[] { "dB", "mA", "dB", "dB", "dB", "dB" }, rows[3][5..].Select(c => c.Trim()));
    }

    [Fact]
    public void GenerateCsv_TestResults_BuildsConditionBracketHeaders()
    {
        var lines = GenerateTestResultsLines(TestResultsConfig());

        var names = TrCells(lines[TrHeaderIndex]).Select(c => c.Trim()).ToArray();

        Assert.Equal(
            new[]
            {
                "SN", "Site", "X", "Y", "Bin",
                "[Vdd=3.3|Temp=25]Gain(dB)",
                "[Vdd=3.3|Temp=25]Idd(mA)",
                "[Mode=lo|Vdd=3.3]Gain(dB)",
                "[Mode=hi|Vdd=3.3]Gain(dB)",
                "[Mode=lo|Vdd=5.0]Gain(dB)",
                "[Mode=hi|Vdd=5.0]Gain(dB)"
            },
            names);
    }

    [Fact]
    public void GenerateCsv_TestResults_QuirksChangeOnlyTheColumnsTheyName()
    {
        var baseline = TrCells(GenerateTestResultsLines(TestResultsConfig(), "tr-base")[TrHeaderIndex]);
        var quirky = TestResultsConfig(5, s =>
        {
            s.ConditionSets[0].Tests[0].Quirk = "caseVariant";
            s.ConditionSets[0].Tests[1].Quirk = "repeatUnit";
        });
        var lines = GenerateTestResultsLines(quirky, "tr-quirk");
        var names = TrCells(lines[TrHeaderIndex]);

        Assert.Equal("[Vdd=3.3|Temp=25]gain(dB)", names[5].Trim());
        Assert.Equal("[Vdd=3.3|Temp=25]Idd(mA)(mA)", names[6].Trim());
        for (var i = 0; i < baseline.Length; i++)
        {
            if (i is not (5 or 6))
                Assert.Equal(baseline[i], names[i]);
        }

        // The widened header widens its column, and every line still has one length.
        Assert.Equal(baseline[6].Length + 4, names[6].Length);
        Assert.All(lines[TrPreambleLines..], line => Assert.Equal(lines[TrHeaderIndex].Length, line.Length));
    }

    [Fact]
    public void GenerateCsv_TestResults_UsesHeaderLengthPlusPaddingAsTestColumnWidth()
    {
        foreach (var padding in new[] { 0, 4, 9 })
        {
            var lines = GenerateTestResultsLines(TestResultsConfig(2, s => s.TestColumnPadding = padding), "tr-pad");
            var header = TrCells(lines[TrHeaderIndex]);

            Assert.Equal(new[] { 8, 5, 4, 4, 4 }, header[..5].Select(c => c.Length));
            Assert.All(header[5..], c => Assert.Equal(padding, c.Length - c.TrimEnd().Length));
            Assert.All(lines[(TrHeaderIndex + 1)..], line => Assert.Equal(header.Length, TrCells(line).Length));
        }
    }

    [Fact]
    public void GenerateCsv_TestResults_DefaultsTestColumnPaddingToFour()
    {
        Assert.Equal(4, new TestResultsSpec().TestColumnPadding);
        var lines = GenerateTestResultsLines(TestResultsConfig(1), "tr-default-pad");

        Assert.Equal(4, TrCells(lines[TrHeaderIndex])[5].Length - "[Vdd=3.3|Temp=25]Gain(dB)".Length);
    }

    [Fact]
    public void GenerateCsv_TestResults_CellLongerThanItsColumn_FailsAndWritesNothing()
    {
        var outputPath = Path.Combine(_tempDir, "tr-overflow.csv");
        // Serials 100, 103, ... reach four digits at the 300th device.
        var config = TestResultsConfig(400, s =>
        {
            s.Annotations = [];
            s.DeviceColumns.Serial!.Width = 3;
        });

        var ex = Assert.Throws<InvalidOperationException>(() => _obfuscator.GenerateCsv(config, outputPath));

        Assert.Contains("exceeds", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outputPath));
        Assert.False(File.Exists(outputPath + ".tmp"));
    }

    [Fact]
    public void GenerateCsv_TestResults_TestValueLongerThanItsColumn_FailsAndWritesNothing()
    {
        var outputPath = Path.Combine(_tempDir, "tr-value-overflow.csv");
        var config = TestResultsConfig(5, s =>
        {
            s.TestColumnPadding = 0;
            s.ConditionSets = [new ConditionSetSpec { Conditions = [], Tests = [new TestSpec { Name = "T", Min = 0, Max = 1, Format = "%.9f" }] }];
        });

        var ex = Assert.Throws<InvalidOperationException>(() => _obfuscator.GenerateCsv(config, outputPath));

        Assert.Contains("exceeds", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outputPath));
        Assert.False(File.Exists(outputPath + ".tmp"));
    }

    [Fact]
    public void GenerateCsv_TestResults_WritesSerialSequenceAndBinsFollowingTheWeights()
    {
        var config = TestResultsConfig(10_000, s =>
        {
            s.Annotations = [];
            s.DeviceColumns.Bin!.Weights = new Dictionary<string, double> { ["1"] = 70, ["2"] = 20, ["7"] = 10 };
        });
        var lines = GenerateTestResultsLines(config, "tr-bins");
        var data = lines[(TrPreambleLines + 1)..].Select(TrCells).ToArray();

        Assert.Equal(10_000, data.Length);
        Assert.Equal(
            Enumerable.Range(0, 10_000).Select(i => (100 + 3 * i).ToString(CultureInfo.InvariantCulture)),
            data.Select(r => r[0].Trim()));
        Assert.All(data, r => Assert.Equal("1", r[1].Trim()));
        var counts = data.GroupBy(r => r[4].Trim()).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new[] { "1", "2", "7" }, counts.Keys.Order());
        Assert.InRange(counts["1"], 6_700, 7_300);
        Assert.InRange(counts["2"], 1_800, 2_200);
        Assert.InRange(counts["7"], 800, 1_200);
    }

    [Theory]
    [InlineData("%.9f", @"^-?\d+\.\d{9}$")]
    [InlineData("%.2f", @"^-?\d+\.\d{2}$")]
    [InlineData("%.0f", @"^-?\d+$")]
    [InlineData("%.4e", @"^-?\d\.\d{4}e[+-]\d{2}$")]
    public void GenerateCsv_TestResults_FormatsValuesWithThePrintfFormat(string format, string pattern)
    {
        var config = TestResultsConfig(50, s =>
        {
            s.Annotations = [];
            s.TestColumnPadding = 30;
            s.ConditionSets = [new ConditionSetSpec { Conditions = [], Tests = [new TestSpec { Name = "V", Min = 0.001, Max = 12345, Format = format }] }];
        });

        var lines = GenerateTestResultsLines(config, "tr-format");

        var cells = lines[(TrPreambleLines + 1)..].Select(l => TrCells(l)[^1].Trim()).ToArray();
        Assert.Equal(50, cells.Length);
        Assert.All(cells, c => Assert.Matches(pattern, c));
    }

    [Fact]
    public void GenerateCsv_TestResults_FormatsLargeExponentsLikePrintf()
    {
        var config = TestResultsConfig(20, s =>
        {
            s.Annotations = [];
            s.TestColumnPadding = 30;
            s.ConditionSets = [new ConditionSetSpec { Conditions = [], Tests = [new TestSpec { Name = "V", Min = 1e100, Max = 9e100, Format = "%.2e" }] }];
        });

        var cells = GenerateTestResultsLines(config, "tr-exp").Skip(TrPreambleLines + 1).Select(l => TrCells(l)[^1].Trim()).ToArray();

        Assert.All(cells, c => Assert.Matches(@"^\d\.\d{2}e\+100$", c));
    }

    private double[] TrColumnValues(DataGenConfig config, out string[] tokens, string name = "tr-values")
    {
        var lines = GenerateTestResultsLines(config, name);
        var cells = lines[(TrPreambleLines + 1)..].Select(l => TrCells(l)[^1].Trim()).ToArray();
        tokens = cells;
        return cells
            .Where(c => double.TryParse(c, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            .Select(c => double.Parse(c, CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static DataGenConfig TrSingleTestConfig(int devices, Action<TestSpec> edit)
    {
        var test = new TestSpec { Name = "V", Unit = "V", UpperLimit = 2, LowerLimit = 1, Min = 1, Max = 2, Format = "%.3f" };
        edit(test);
        return TestResultsConfig(devices, s =>
        {
            s.Annotations = [];
            s.TestColumnPadding = 20;
            s.ConditionSets = [new ConditionSetSpec { Conditions = [], Tests = [test] }];
        });
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(100.0, 10_000)]
    [InlineData(25.0, -1)]
    public void GenerateCsv_TestResults_NullTokenAppearsAtTheConfiguredRate(double rate, int expected)
    {
        var config = TrSingleTestConfig(10_000, t => t.NullRate = rate);

        TrColumnValues(config, out var tokens);

        var nulls = tokens.Count(c => c == "NA.");
        if (expected >= 0)
            Assert.Equal(expected, nulls);
        else
            Assert.InRange(nulls, 2_300, 2_700);
    }

    [Fact]
    public void GenerateCsv_TestResults_UsesTheConfiguredNullToken()
    {
        var config = TrSingleTestConfig(20, t => { t.NullRate = 100; t.NullToken = "--"; });

        TrColumnValues(config, out var tokens);

        Assert.All(tokens, c => Assert.Equal("--", c));
    }

    [Theory]
    [InlineData("uniform")]
    [InlineData("normal")]
    public void GenerateCsv_TestResults_KeepsValuesInsideTheLimitsWhenTheOutOfLimitRateIsZero(string distribution)
    {
        var config = TrSingleTestConfig(10_000, t =>
        {
            t.Distribution = distribution;
            t.Mean = 1.5;
            t.StdDev = 2;
            t.Min = 0;
            t.Max = 3;
        });

        var values = TrColumnValues(config, out _);

        Assert.Equal(10_000, values.Length);
        Assert.All(values, v => Assert.InRange(v, 1.0, 2.0));
        if (distribution == "normal")
            Assert.Contains(values, v => v == 1.0);
    }

    [Theory]
    [InlineData(100.0, 10_000)]
    [InlineData(20.0, -1)]
    public void GenerateCsv_TestResults_OutOfLimitValuesAppearAtTheConfiguredRate(double rate, int expected)
    {
        var config = TrSingleTestConfig(10_000, t => t.OutOfLimitRate = rate);

        var values = TrColumnValues(config, out _);

        var outside = values.Count(v => v < 1.0 || v > 2.0);
        if (expected >= 0)
            Assert.Equal(expected, outside);
        else
            Assert.InRange(outside, 1_800, 2_200);
        // "Just outside": within a tenth of the limit span, never far away.
        Assert.All(values, v => Assert.InRange(v, 0.89, 2.11));
    }

    [Fact]
    public void GenerateCsv_TestResults_DrawsOutOfLimitValuesOnBothSides()
    {
        var config = TrSingleTestConfig(2_000, t => t.OutOfLimitRate = 100);

        var values = TrColumnValues(config, out _);

        Assert.Contains(values, v => v < 1.0);
        Assert.Contains(values, v => v > 2.0);
    }

    [Fact]
    public void GenerateCsv_TestResults_OutOfLimitValuesStayOutsideAfterRounding()
    {
        // Limits not representable at one decimal: the printed value must still be outside them.
        var config = TrSingleTestConfig(2_000, t =>
        {
            t.UpperLimit = 1.04;
            t.LowerLimit = 0.96;
            t.Min = 0.9;
            t.Max = 1.1;
            t.Format = "%.1f";
            t.OutOfLimitRate = 100;
        });

        var values = TrColumnValues(config, out _);

        Assert.All(values, v => Assert.True(v < 0.96 || v > 1.04, $"{v} is inside the limits"));
    }

    [Fact]
    public void GenerateCsv_TestResults_NormalDistributionFollowsMeanAndStdDev()
    {
        var config = TrSingleTestConfig(10_000, t =>
        {
            t.Distribution = "normal";
            t.UpperLimit = null;
            t.LowerLimit = null;
            t.Mean = 10;
            t.StdDev = 2;
            t.Format = "%.6f";
        });

        var values = TrColumnValues(config, out _);

        Assert.InRange(values.Average(), 9.9, 10.1);
        var variance = values.Select(v => (v - 10) * (v - 10)).Average();
        Assert.InRange(Math.Sqrt(variance), 1.9, 2.1);
    }

    [Fact]
    public void GenerateCsv_TestResults_SameSeedGivesIdenticalBytesAndAnotherSeedDiffers()
    {
        var a = Path.Combine(_tempDir, "tr-seed-a.csv");
        var b = Path.Combine(_tempDir, "tr-seed-b.csv");
        var c = Path.Combine(_tempDir, "tr-seed-c.csv");
        _obfuscator.GenerateCsv(TestResultsConfig(200), a);
        _obfuscator.GenerateCsv(TestResultsConfig(200), b);
        var other = TestResultsConfig(200);
        other.Seed = 12;
        _obfuscator.GenerateCsv(other, c);

        Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(b));
        Assert.NotEqual(File.ReadAllBytes(a), File.ReadAllBytes(c));
    }

    [Fact]
    public void GenerateCsv_TestResults_ZeroDevicesWritesPreambleAnnotationsAndHeaderOnly()
    {
        var lines = GenerateTestResultsLines(TestResultsConfig(0), "tr-zero");

        Assert.Equal(TrHeaderIndex + 1, lines.Length);
    }

    [Fact]
    public void GenerateCsv_TestResults_OptionalPartsCanBeLeftOut()
    {
        var config = TestResultsConfig(3, s =>
        {
            s.Preamble = null;
            s.Annotations = [];
            s.DeviceColumns = new DeviceColumnsSpec { Serial = new SerialColumnSpec { Name = "SN", Width = 6 } };
        });

        var lines = GenerateTestResultsLines(config, "tr-bare");

        Assert.Equal(4, lines.Length);
        Assert.StartsWith("SN    ,[Vdd=3.3|Temp=25]Gain(dB)", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateCsv_TestResults_ExampleConfigMatchesTheCommittedGoldenByteForByte()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "ExampleConfigTestResults.json");
        var goldenPath = Path.Combine(AppContext.BaseDirectory, "ExampleConfigTestResults.golden.csv");
        Assert.True(File.Exists(configPath), configPath);
        Assert.True(File.Exists(goldenPath), goldenPath);
        var outputPath = Path.Combine(_tempDir, "tr-golden.csv");

        _obfuscator.GenerateCsvFromConfig(configPath, outputPath);

        Assert.Equal(File.ReadAllBytes(goldenPath), File.ReadAllBytes(outputPath));
    }

    [Fact]
    public void GenerateCsv_TestResults_ExampleConfigHasTheShapeTheSpecAsks()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "ExampleConfigTestResults.json");
        var outputPath = Path.Combine(_tempDir, "tr-example.csv");
        _obfuscator.GenerateCsvFromConfig(configPath, outputPath);

        var bytes = File.ReadAllBytes(outputPath);
        var lines = Encoding.Latin1.GetString(bytes).Split("\r\n");
        var header = lines.First(l => l.Contains("Gain", StringComparison.Ordinal));
        var headerIndex = Array.IndexOf(lines, header);

        // Header, then 20 devices, then the final terminator.
        Assert.Equal(headerIndex + 1 + 20 + 1, lines.Length);
        Assert.Equal(12, TrCells(header).Length - 5);
        Assert.Contains(0xFF, bytes.Select(b => (int)b));
        Assert.Contains("NA.", lines.Skip(headerIndex + 1).SelectMany(TrCells).Select(c => c.Trim()));
    }

    public static IEnumerable<object[]> TestResultsInvalidConfigs()
    {
        static object[] Case(string name, string message, Action<DataGenConfig> mutate) => [name, message, mutate];
        static TestSpec FirstTest(DataGenConfig c) => c.TestResults!.ConditionSets[0].Tests[0];

        yield return Case("unknown preamble kind", "kind", c => c.TestResults!.Preamble!.Entries[0].Kind = "random");
        yield return Case("unknown quirk", "quirk", c => FirstTest(c).Quirk = "weird");
        yield return Case("unknown line ending", "lineEnding", c => c.TestResults!.LineEnding = "cr");
        yield return Case("unknown distribution", "distribution", c => FirstTest(c).Distribution = "beta");
        yield return Case("unknown annotation source", "source", c => c.TestResults!.Annotations[0].Source = "color");
        yield return Case("format %d", "format", c => FirstTest(c).Format = "%d");
        yield return Case("format with width", "format", c => FirstTest(c).Format = "%8.3f");
        yield return Case("format %g", "format", c => FirstTest(c).Format = "%.3g");
        yield return Case("duplicate columns", "duplicate", c => c.TestResults!.ConditionSets.Add(c.TestResults.ConditionSets[0]));
        yield return Case("duplicate device column", "duplicate", c => c.TestResults!.DeviceColumns.Site!.Name = "SN");
        yield return Case("upper below lower", "upperLimit", c => { FirstTest(c).UpperLimit = 1; FirstTest(c).LowerLimit = 2; });
        yield return Case("null rate below 0", "nullRate", c => FirstTest(c).NullRate = -0.1);
        yield return Case("null rate above 100", "nullRate", c => FirstTest(c).NullRate = 100.1);
        yield return Case("out-of-limit rate above 100", "outOfLimitRate", c => FirstTest(c).OutOfLimitRate = 101);
        yield return Case("out-of-limit rate without limits", "limit", c => { FirstTest(c).UpperLimit = null; FirstTest(c).LowerLimit = null; FirstTest(c).OutOfLimitRate = 5; });
        yield return Case("empty bin weights", "weights", c => c.TestResults!.DeviceColumns.Bin!.Weights = new());
        yield return Case("negative bin weight", "weight", c => c.TestResults!.DeviceColumns.Bin!.Weights["1"] = -1);
        yield return Case("inject into a missing key", "inject", c => c.TestResults!.Preamble!.Inject = [new PreambleInjectSpec { Key = "Nope", Offset = 0, Bytes = [255] }]);
        yield return Case("inject byte out of range", "byte", c => c.TestResults!.Preamble!.Inject = [new PreambleInjectSpec { Key = "Project", Offset = 0, Bytes = [256] }]);
        yield return Case("inject beyond the key", "offset", c => c.TestResults!.Preamble!.Inject = [new PreambleInjectSpec { Key = "Project", Offset = 11, Bytes = [255, 255] }]);
        yield return Case("negative key width", "keyWidth", c => c.TestResults!.Preamble!.KeyWidth = -1);
        yield return Case("negative device width", "width", c => c.TestResults!.DeviceColumns.Serial!.Width = -1);
        yield return Case("negative padding", "testColumnPadding", c => c.TestResults!.TestColumnPadding = -1);
        yield return Case("columns with testResults", "columns", c => c.Columns = [new ColumnGenerationSpec { Name = "A", DataType = "string" }]);
        yield return Case("sweepAxes with testResults", "sweepAxes", c => c.SweepAxes = [new SweepAxisSpec { Name = "A" }]);
        yield return Case("device count below 0", "nRows", c => c.NRows = -1);
        yield return Case("device count above Array.MaxLength", "nRows", c => c.NRows = Array.MaxLength + 1);
        yield return Case("missing testResults", "testResults", c => c.TestResults = null);
        yield return Case("testResults with another rowMode", "testResults", c => c.RowMode = "fixed");
        yield return Case("conditions and axes", "conditions", c => c.TestResults!.ConditionSets[0].Axes = [new ConditionAxisSpec { Key = "K", Values = ["1"] }]);
        yield return Case("neither conditions nor axes", "conditions", c => c.TestResults!.ConditionSets[0].Conditions = null);
        yield return Case("condition pair of three", "condition", c => c.TestResults!.ConditionSets[0].Conditions = [["a", "b", "c"]]);
        yield return Case("axis without values", "values", c => c.TestResults!.ConditionSets[1].Axes![0].Values = []);
        yield return Case("key order not a permutation", "keyOrder", c => c.TestResults!.ConditionSets[1].KeyOrder = ["Mode"]);
        yield return Case("repeated axis key", "axis", c => c.TestResults!.ConditionSets[1].Axes![1].Key = "Vdd");
        yield return Case("repeatUnit without unit", "unit", c => { FirstTest(c).Unit = null; FirstTest(c).Quirk = "repeatUnit"; });
        yield return Case("annotations without a device column", "annotation", c => c.TestResults!.DeviceColumns = new DeviceColumnsSpec());
        yield return Case("annotation label too long", "label", c => c.TestResults!.Annotations[0].Label = "A label that is much too long");
        yield return Case("uniform without range", "uniform", c => { FirstTest(c).Min = null; FirstTest(c).Max = null; });
        yield return Case("uniform min above max", "min", c => { FirstTest(c).Min = 5; FirstTest(c).Max = 1; });
        yield return Case("normal without mean", "normal", c => { FirstTest(c).Distribution = "normal"; FirstTest(c).Mean = null; });
        yield return Case("negative std dev", "stdDev", c => { FirstTest(c).Distribution = "normal"; FirstTest(c).Mean = 1; FirstTest(c).StdDev = -1; });
        yield return Case("no condition sets", "test column", c => c.TestResults!.ConditionSets = []);
        yield return Case("condition sets without tests", "test column", c => { foreach (var set in c.TestResults!.ConditionSets) set.Tests = []; });
        yield return Case("uniform range overflows", "finite", c => { FirstTest(c).Min = -1e308; FirstTest(c).Max = 1e308; });
        yield return Case("normal spread overflows", "finite", c => { FirstTest(c).Distribution = "normal"; FirstTest(c).Mean = 0; FirstTest(c).StdDev = 1e308; });
        yield return Case("normal mean overflows", "finite", c => { FirstTest(c).Distribution = "normal"; FirstTest(c).Mean = double.MaxValue; FirstTest(c).StdDev = double.MaxValue; });
        yield return Case("limit span overflows", "finite", c => { FirstTest(c).UpperLimit = 1e308; FirstTest(c).LowerLimit = -1e308; });
        yield return Case("date entry without a date", "date", c => { c.TestResults!.Preamble!.Entries[1].Value = "not a date"; });
    }

    [Theory]
    [MemberData(nameof(TestResultsInvalidConfigs))]
    public void GenerateCsv_TestResults_InvalidConfig_ThrowsBeforeWriting(string name, string message, Action<DataGenConfig> mutate)
    {
        var config = TestResultsConfig();
        mutate(config);
        var outputPath = Path.Combine(_tempDir, "tr-invalid.csv");

        var ex = Assert.Throws<InvalidOperationException>(() => _obfuscator.GenerateCsv(config, outputPath));

        Assert.True(
            ex.Message.Contains(message, StringComparison.OrdinalIgnoreCase),
            $"{name}: '{ex.Message}' should mention '{message}'");
        Assert.False(File.Exists(outputPath));
        Assert.False(File.Exists(outputPath + ".tmp"));
    }

    [Fact]
    public void GenerateCsv_TestResults_EmptyTestResultsObject_Throws()
    {
        var configPath = WriteConfig("tr-empty.json", "{ \"rowMode\": \"test-results\", \"nRows\": 3, \"testResults\": {} }");

        var ex = Assert.Throws<InvalidOperationException>(() => _obfuscator.GetDataGenParametersFromConfig(configPath));

        Assert.Contains("test column", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenerateCsv_TestResults_ValueThatOverflowsToInfinity_FailsAndWritesNothing()
    {
        // Passes validation, but just outside a limit near double.MaxValue overflows.
        var outputPath = Path.Combine(_tempDir, "tr-infinity.csv");
        var config = TrSingleTestConfig(50, t =>
        {
            t.UpperLimit = 1.7e308;
            t.LowerLimit = null;
            t.Min = 0;
            t.Max = 1;
            t.Format = "%.0f";
            t.OutOfLimitRate = 100;
        });
        config.TestResults!.TestColumnPadding = 400;

        var ex = Assert.Throws<InvalidOperationException>(() => _obfuscator.GenerateCsv(config, outputPath));

        Assert.Contains("not finite", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outputPath));
        Assert.False(File.Exists(outputPath + ".tmp"));
    }

    [Fact]
    public void GenerateCsv_TestResults_RefusesToOverwriteWithoutForce()
    {
        var outputPath = Path.Combine(_tempDir, "tr-exists.csv");
        File.WriteAllText(outputPath, "keep");

        Assert.Throws<InvalidOperationException>(() => _obfuscator.GenerateCsv(TestResultsConfig(), outputPath));

        Assert.Equal("keep", File.ReadAllText(outputPath));
    }

    [Fact]
    public void TestResultsGenerator_100kDevicesBy200Columns_StreamsRowsWithoutBuildingTheFile()
    {
        const int devices = 100_000;
        var config = new DataGenConfig
        {
            RowMode = "test-results",
            NRows = devices,
            Seed = 5,
            TestResults = new TestResultsSpec
            {
                LineEnding = "lf",
                DeviceColumns = new DeviceColumnsSpec { Serial = new SerialColumnSpec { Name = "SN", Width = 10 } },
                ConditionSets =
                [
                    new ConditionSetSpec
                    {
                        Axes = [new ConditionAxisSpec { Key = "c", Values = Enumerable.Range(0, 50).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList() }],
                        Tests = Enumerable.Range(0, 4).Select(i => new TestSpec { Name = "T" + i, Min = 0, Max = 1, Format = "%.4f", NullRate = 1 }).ToList()
                    }
                ]
            }
        };
        var sink = new CountingStream();
        var generator = new TestResultsGenerator(config);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        generator.Write(sink);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(devices + 1, sink.NewLines);
        // The output is hundreds of MB; buffering it (for example in a MemoryStream) would allocate at least that much.
        Assert.True(sink.Bytes > 100_000_000, $"the output is only {sink.Bytes} bytes, too small for the allocation check");
        Assert.True(allocated < 16_000_000, $"writing allocated {allocated} bytes for {sink.Bytes} bytes of output: rows were buffered");
        Assert.True(sink.LargestWrite <= 1 << 20, $"largest single write was {sink.LargestWrite} bytes");
        Assert.True(sink.Writes > 100, $"only {sink.Writes} writes: rows were buffered");
    }

    private sealed class CountingStream : Stream
    {
        public long NewLines { get; private set; }
        public long Bytes { get; private set; }
        public int LargestWrite { get; private set; }
        public int Writes { get; private set; }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Writes++;
            Bytes += buffer.Length;
            LargestWrite = Math.Max(LargestWrite, buffer.Length);
            NewLines += buffer.Count((byte)'\n');
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    [SkippableFact]
    public void Manifest_GpgRoundTrip_RestoresOriginal()
    {
        Skip.If(!GpgAvailable(), "gpg is not on PATH");
        Skip.If(GpgIsMsysOnWindows(), "gpg on PATH is an MSYS build; it does not accept a Windows --homedir");

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

    [SkippableFact]
    [UnsupportedOSPlatform("windows")] // Skip.If below skips Windows at runtime; this tells CA1416.
    public void Manifest_GpgEncrypt_WritesNoPlaintextToDiskWhileGpgRuns()
    {
        Skip.If(OperatingSystem.IsWindows(), "the gpg wrapper is a POSIX shell script");
        Skip.If(!GpgAvailable(), "gpg is not on PATH");
        var realGpg = FindOnPath("gpg");
        Skip.If(realGpg is null, "gpg is not on PATH");

        // GNUPGHOME stays short and outside the scanned directories (agent socket path limits).
        var gnupg = Path.Combine(Path.GetTempPath(), "r9g" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(gnupg);
        var inputDir = Directory.CreateDirectory(Path.Combine(_tempDir, "plain-in")).FullName;
        var outputDir = Directory.CreateDirectory(Path.Combine(_tempDir, "plain-out")).FullName;
        var scratchTemp = Directory.CreateDirectory(Path.Combine(_tempDir, "plain-tmp")).FullName;
        var wrapperDir = Directory.CreateDirectory(Path.Combine(_tempDir, "plain-bin")).FullName;
        var logPath = Path.Combine(_tempDir, "gpg-wrapper.log");
        const string marker = "OB17PLAINTEXTMARKER";

        // At each gpg call, record whether the marker (an original value that only the
        // plaintext manifest holds) is on disk in the temp or output directory.
        var wrapperPath = Path.Combine(wrapperDir, "gpg");
        File.WriteAllText(
            wrapperPath,
            "#!/bin/sh\n" +
            $"if grep -rqs '{marker}' '{scratchTemp}' '{outputDir}'; then echo found >> '{logPath}'; else echo clean >> '{logPath}'; fi\n" +
            $"exec '{realGpg}' \"$@\"\n");
        File.SetUnixFileMode(wrapperPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var previousHome = Environment.GetEnvironmentVariable("GNUPGHOME");
        var previousPath = Environment.GetEnvironmentVariable("PATH");
        var previousTmp = Environment.GetEnvironmentVariable("TMPDIR");
        Environment.SetEnvironmentVariable("GNUPGHOME", gnupg);
        try
        {
            File.SetUnixFileMode(gnupg, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            GenerateTestGpgKey("ob17-test@example.invalid", gnupg);

            var inputPath = Path.Combine(inputDir, "input.csv");
            var obfuscatedPath = Path.Combine(outputDir, "obfuscated.csv");
            var restoredPath = Path.Combine(inputDir, "restored.csv");
            var manifestPath = Path.Combine(outputDir, "gpg.obf");
            File.WriteAllLines(inputPath, ["CustomerId,Region", $"{marker},west"], Encoding.UTF8);

            Environment.SetEnvironmentVariable("PATH", wrapperDir + Path.PathSeparator + previousPath);
            Environment.SetEnvironmentVariable("TMPDIR", scratchTemp);
            _obfuscator.ObfuscateCsv(
                inputPath,
                obfuscatedPath,
                manifestPath,
                new ObfuscationOptions { GpgRecipients = ["ob17-test@example.invalid"], StringMode = StringObfuscationMode.Mapping });
            Environment.SetEnvironmentVariable("TMPDIR", previousTmp);
            Environment.SetEnvironmentVariable("PATH", previousPath);

            Assert.Equal(["clean"], File.ReadAllLines(logPath));
            Assert.Empty(Directory.GetFiles(scratchTemp));
            Assert.Equal(["gpg.obf", "obfuscated.csv"], Directory.GetFiles(outputDir).Select(Path.GetFileName).Order());
            Assert.StartsWith("OBF_GPG_V2\n", File.ReadAllText(manifestPath, Encoding.UTF8), StringComparison.Ordinal);

            _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath);
            Assert.Equal(File.ReadAllLines(inputPath, Encoding.UTF8), File.ReadAllLines(restoredPath, Encoding.UTF8));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMPDIR", previousTmp);
            Environment.SetEnvironmentVariable("PATH", previousPath);
            Environment.SetEnvironmentVariable("GNUPGHOME", previousHome);
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
            "OBF_AESGCM_V3\n" + Convert.ToBase64String(new byte[8]),
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
        Assert.False(File.Exists(obfuscatedPath), obfuscatedPath);
    }

    [Fact]
    public void Cli_PassphraseFileFollowedByAnotherFlag_Fails()
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
                "--passphrase-file",
                "--create-output-dir"
            ]);
            Assert.NotEqual(0, exit);
            Assert.Contains("--passphrase-file requires a value.", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Theory]
    [InlineData("--passphrase", "cli-inline-value-1")]
    [InlineData("--PASSPHRASE", "cli-inline-value-2")]
    [InlineData("--passphrase=cli-inline-value-3", null)]
    public void Cli_InlinePassphrase_IsRejectedWithoutEcho(string flag, string? value)
    {
        var inputPath = Path.Combine(_tempDir, "cli-inline-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "cli-inline-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "cli-inline.obf");
        File.WriteAllLines(inputPath, ["CustomerId", "ALPHA"], Encoding.UTF8);

        List<string> args = ["obfuscate", "--input", inputPath, "--output", obfuscatedPath, "--manifest", manifestPath, flag];
        if (value is not null)
            args.Add(value);

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = ObfuscatorCliProgram.Run([.. args]);
            Assert.Equal(1, exit);
            var text = stderr.ToString() + stdout.ToString();
            Assert.DoesNotContain("cli-inline-value", text, StringComparison.Ordinal);
            Assert.Contains("OBFUSCATOR_PASSPHRASE", text, StringComparison.Ordinal);
            Assert.Contains("--passphrase-file", text, StringComparison.Ordinal);
            Assert.Contains("--passphrase-stdin", text, StringComparison.Ordinal);
            Assert.False(File.Exists(obfuscatedPath));
            Assert.False(File.Exists(manifestPath));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Theory]
    [InlineData("obfuscate", "--deterministic-key", "cli-inline-key-1")]
    [InlineData("obfuscate", "--DETERMINISTIC-KEY", "cli-inline-key-2")]
    [InlineData("obfuscate", "--deterministic-key=cli-inline-key-3", null)]
    [InlineData("deobfuscate", "--deterministic-key", "cli-inline-key-4")]
    [InlineData("deobfuscate", "--deterministic-key=cli-inline-key-5", null)]
    public void Cli_InlineDeterministicKey_IsRejectedWithoutEcho(string command, string flag, string? value)
    {
        var inputPath = Path.Combine(_tempDir, "cli-inline-key-input.csv");
        var outputPath = Path.Combine(_tempDir, "cli-inline-key-output.csv");
        var manifestPath = Path.Combine(_tempDir, "cli-inline-key.obf");
        File.WriteAllLines(inputPath, ["CustomerId", "ALPHA"], Encoding.UTF8);

        List<string> args = [command, "--input", inputPath, "--output", outputPath, "--manifest", manifestPath, flag];
        if (value is not null)
            args.Add(value);

        var (exit, text) = RunCliCapturingOutput([.. args]);

        Assert.Equal(1, exit);
        Assert.DoesNotContain("cli-inline-key-", text, StringComparison.Ordinal);
        Assert.Contains("OBFUSCATOR_DETERMINISTIC_KEY", text, StringComparison.Ordinal);
        Assert.Contains("--deterministic-key-file", text, StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));
        Assert.False(File.Exists(manifestPath));
    }

    [Fact]
    public void Cli_DeterministicKeyFromFile_RoundTrips()
    {
        var inputPath = Path.Combine(_tempDir, "key-file-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "key-file-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "key-file.obf");
        var restoredPath = Path.Combine(_tempDir, "key-file-restored.csv");
        var keyPath = Path.Combine(_tempDir, "deterministic-key.txt");
        File.WriteAllLines(inputPath, ["CustomerId", "ALPHA", "BRAVO"], Encoding.UTF8);
        File.WriteAllText(keyPath, "key-from-file\n");

        var exit = ObfuscatorCliProgram.Run(
        [
            "obfuscate",
            "--input", inputPath,
            "--output", obfuscatedPath,
            "--manifest", manifestPath,
            "--string-mode", "deterministic-token",
            "--deterministic-key-file", keyPath
        ]);
        Assert.Equal(0, exit);
        Assert.DoesNotContain("ALPHA", File.ReadAllText(obfuscatedPath, Encoding.UTF8), StringComparison.Ordinal);

        exit = ObfuscatorCliProgram.Run(
        [
            "deobfuscate",
            "--input", obfuscatedPath,
            "--manifest", manifestPath,
            "--output", restoredPath,
            "--deterministic-key-file", keyPath
        ]);
        Assert.Equal(0, exit);
        Assert.Equal(File.ReadAllLines(inputPath, Encoding.UTF8), File.ReadAllLines(restoredPath, Encoding.UTF8));
    }

    [Theory]
    [InlineData("obfuscate", "")]
    [InlineData("obfuscate", "\n")]
    [InlineData("obfuscate", "  \r\n")]
    [InlineData("deobfuscate", "\n")]
    public void Cli_BlankDeterministicKeyFile_IsRejected(string command, string contents)
    {
        var inputPath = Path.Combine(_tempDir, "blank-key-input.csv");
        var outputPath = Path.Combine(_tempDir, "blank-key-output.csv");
        var manifestPath = Path.Combine(_tempDir, "blank-key.obf");
        var keyPath = Path.Combine(_tempDir, "blank-key.txt");
        File.WriteAllLines(inputPath, ["CustomerId", "ALPHA"], Encoding.UTF8);
        File.WriteAllText(keyPath, contents);

        // Auto string mode would otherwise fall back to mapping and report success.
        var (exit, text) = RunCliCapturingOutput(
            [command, "--input", inputPath, "--output", outputPath, "--manifest", manifestPath, "--deterministic-key-file", keyPath]);

        Assert.Equal(1, exit);
        Assert.Contains("Deterministic key is blank.", text, StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));
    }

    [Theory]
    [InlineData("obfuscate", "   ")]
    [InlineData("obfuscate", "\n")]
    [InlineData("deobfuscate", " \r\n")]
    public void Cli_BlankDeterministicKeyEnvironment_IsRejected(string command, string value)
    {
        var inputPath = Path.Combine(_tempDir, "blank-env-key-input.csv");
        var outputPath = Path.Combine(_tempDir, "blank-env-key-output.csv");
        var manifestPath = Path.Combine(_tempDir, "blank-env-key.obf");
        File.WriteAllLines(inputPath, ["CustomerId", "ALPHA"], Encoding.UTF8);
        var previous = Environment.GetEnvironmentVariable("OBFUSCATOR_DETERMINISTIC_KEY");
        Environment.SetEnvironmentVariable("OBFUSCATOR_DETERMINISTIC_KEY", value);
        try
        {
            // Auto string mode would otherwise fall back to mapping and report success.
            var (exit, text) = RunCliCapturingOutput(
                [command, "--input", inputPath, "--output", outputPath, "--manifest", manifestPath]);

            Assert.Equal(1, exit);
            Assert.Contains("Deterministic key is blank.", text, StringComparison.Ordinal);
            Assert.Contains("OBFUSCATOR_DETERMINISTIC_KEY", text, StringComparison.Ordinal);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OBFUSCATOR_DETERMINISTIC_KEY", previous);
        }
    }

    [Fact]
    public void Cli_EmptyDeterministicKeyEnvironment_IsTreatedAsUnset()
    {
        var inputPath = Path.Combine(_tempDir, "empty-env-key-input.csv");
        var outputPath = Path.Combine(_tempDir, "empty-env-key-output.csv");
        var manifestPath = Path.Combine(_tempDir, "empty-env-key.obf");
        File.WriteAllLines(inputPath, ["CustomerId", "ALPHA"], Encoding.UTF8);
        var previous = Environment.GetEnvironmentVariable("OBFUSCATOR_DETERMINISTIC_KEY");
        Environment.SetEnvironmentVariable("OBFUSCATOR_DETERMINISTIC_KEY", "");
        try
        {
            var (exit, _) = RunCliCapturingOutput(
                ["obfuscate", "--input", inputPath, "--output", outputPath, "--manifest", manifestPath]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(outputPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OBFUSCATOR_DETERMINISTIC_KEY", previous);
        }
    }

    [Fact]
    public void Cli_DeterministicKeyFileFollowedByAnotherFlag_Fails()
    {
        var inputPath = Path.Combine(_tempDir, "cli-key-file-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "cli-key-file-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "cli-key-file.obf");
        File.WriteAllLines(inputPath, ["CustomerId,Region", "ALPHA,west"], Encoding.UTF8);

        var (exit, text) = RunCliCapturingOutput(
        [
            "obfuscate",
            "--input", inputPath,
            "--output", obfuscatedPath,
            "--manifest", manifestPath,
            "--deterministic-key-file",
            "--create-output-dir"
        ]);

        Assert.NotEqual(0, exit);
        Assert.Contains("--deterministic-key-file requires a value.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_UnknownCommand_DoesNotEchoToken()
    {
        using var stderr = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(stderr);
        try
        {
            var exit = ObfuscatorCliProgram.Run(["not-a-flag"]);
            Assert.Equal(1, exit);
            var text = stderr.ToString();
            Assert.DoesNotContain("not-a-flag", text, StringComparison.Ordinal);
            Assert.Contains("Unknown command.", text, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Cli_UnexpectedBareToken_DoesNotEchoToken()
    {
        using var stderr = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(stderr);
        try
        {
            var exit = ObfuscatorCliProgram.Run(["obfuscate", "not-a-flag"]);
            Assert.Equal(1, exit);
            var text = stderr.ToString();
            Assert.DoesNotContain("not-a-flag", text, StringComparison.Ordinal);
            Assert.Contains("Options must start with '--'", text, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Theory]
    [InlineData("--gpg-recipent", "someone@example.invalid", "Unknown option '--gpg-recipent' for 'obfuscate'.")]
    [InlineData("--pasphrase-file", "cli-typo-passphrase.txt", "Unknown option '--pasphrase-file' for 'obfuscate'.")]
    [InlineData("--pasphrase=cli-typo-secret-value", null, "Unknown option '--pasphrase' for 'obfuscate'.")]
    [InlineData("--input=cli-typo-inline.csv", null, "Option '--input' for 'obfuscate' does not accept '=' syntax.")]
    public void Cli_Obfuscate_UnknownOption_FailsWithoutWritingOrEchoingValue(string flag, string? value, string expectedMessage)
    {
        var inputPath = Path.Combine(_tempDir, "cli-typo-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "cli-typo-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "cli-typo.obf");
        File.WriteAllLines(inputPath, ["CustomerId", "ALPHA"], Encoding.UTF8);

        List<string> args = ["obfuscate", "--input", inputPath, "--output", obfuscatedPath, "--manifest", manifestPath, flag];
        if (value is not null)
            args.Add(value);

        var (exit, text) = RunCliCapturingOutput([.. args]);

        Assert.Equal(1, exit);
        Assert.Contains(expectedMessage, text, StringComparison.Ordinal);
        Assert.DoesNotContain("cli-typo-secret-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("cli-typo-inline.csv", text, StringComparison.Ordinal);
        Assert.False(File.Exists(obfuscatedPath));
        Assert.False(File.Exists(manifestPath));
    }

    [Theory]
    [InlineData("deobfuscate", "--gpg-recipient", "someone@example.invalid")]
    [InlineData("deobfuscate", "--strict", null)]
    [InlineData("generate", "--seed", "42")]
    [InlineData("generate", "--manifest", "cli-wrong-command.obf")]
    public void Cli_OptionFromAnotherCommand_IsRejected(string command, string flag, string? value)
    {
        var inputPath = Path.Combine(_tempDir, "cli-wrong-command-input.csv");
        var outputPath = Path.Combine(_tempDir, "cli-wrong-command-output.csv");
        var manifestPath = Path.Combine(_tempDir, "cli-wrong-command-in.obf");
        File.WriteAllLines(inputPath, ["CustomerId", "ALPHA"], Encoding.UTF8);

        List<string> args = command == "generate"
            ? ["generate", "--config", inputPath, "--output", outputPath, flag]
            : [command, "--input", inputPath, "--manifest", manifestPath, "--output", outputPath, flag];
        if (value is not null)
            args.Add(value);

        var (exit, text) = RunCliCapturingOutput([.. args]);

        Assert.Equal(1, exit);
        Assert.Contains($"Unknown option '{flag}' for '{command}'.", text, StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));
    }

    [Theory]
    [InlineData("generate")]
    [InlineData("obfuscate")]
    [InlineData("deobfuscate")]
    public void Cli_DocumentedOptions_AreAccepted(string command)
    {
        // Config and input are deliberately missing so each command stops right after
        // parsing, before any generation, encryption, or gpg call.
        var configPath = Path.Combine(_tempDir, "cli-accepted-missing-config.json");
        var inputPath = Path.Combine(_tempDir, "cli-accepted-missing-input.csv");
        var passphrasePath = Path.Combine(_tempDir, "cli-accepted-passphrase.txt");
        File.WriteAllText(passphrasePath, "cli-accepted-passphrase", Encoding.UTF8);
        var keyPath = Path.Combine(_tempDir, "cli-accepted-key.txt");
        File.WriteAllText(keyPath, "cli-accepted-key", Encoding.UTF8);
        var outputPath = Path.Combine(_tempDir, "cli-accepted", "out.csv");
        var manifestPath = Path.Combine(_tempDir, "cli-accepted", "out.obf");

        // Every option the usage text lists for the command; case-insensitive spelling is accepted as before.
        string[] args = command switch
        {
            "generate" => ["generate", "--config", configPath, "--output", outputPath, "--create-output-dir", "--FORCE"],
            "obfuscate" =>
            [
                "obfuscate", "--input", inputPath, "--output", outputPath, "--manifest", manifestPath,
                "--create-output-dir", "--force", "--deterministic-key-file", keyPath, "--string-mode", "mapping",
                "--include", "CustomerId", "--exclude", "Other", "--allow-list", "--seed", "7",
                "--passphrase-file", passphrasePath, "--passphrase-stdin", "--gpg-recipient", "someone@example.invalid",
                "--preserve-blanks", "true", "--Strict"
            ],
            _ =>
            [
                "deobfuscate", "--input", inputPath, "--manifest", manifestPath, "--output", outputPath,
                "--create-output-dir", "--force", "--passphrase-file", passphrasePath, "--passphrase-stdin",
                "--deterministic-key-file", keyPath, "--allow-mismatched-source"
            ]
        };

        var (exit, text) = RunCliCapturingOutput(args);

        Assert.Equal(1, exit);
        Assert.Matches("(?i)not found|could not find file", text);
        Assert.DoesNotContain("Unknown option", text, StringComparison.Ordinal);
        Assert.DoesNotContain("does not accept '=' syntax", text, StringComparison.Ordinal);
        Assert.DoesNotContain("requires a value", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public void Cli_TopLevelHelp_PrintsUsage(string flag)
    {
        var (exit, text) = RunCliCapturingOutput([flag]);

        Assert.Equal(0, exit);
        Assert.Contains("Usage:", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("generate", "--help")]
    [InlineData("obfuscate", "--help")]
    [InlineData("deobfuscate", "-h")]
    [InlineData("OBFUSCATE", "--HELP")]
    public void Cli_HelpAfterCommand_PrintsUsage(string command, string flag)
    {
        var (exit, text) = RunCliCapturingOutput([command, flag]);

        Assert.Equal(0, exit);
        Assert.Contains("Usage:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Unknown option", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--input", "cli-help-missing.csv", "--help")]
    [InlineData("--output", "--help")]
    [InlineData("--pasphrase=cli-help-secret-value", "-h")]
    [InlineData("--passphrase=cli-help-secret-value", "--help")]
    [InlineData("not-a-flag", "--help")]
    public void Cli_HelpAnywhereAfterCommand_WinsWithoutReadingWritingOrEchoing(params string[] rest)
    {
        var outputPath = Path.Combine(_tempDir, "cli-help-out", "out.csv");
        var manifestPath = Path.Combine(_tempDir, "cli-help-out", "out.obf");
        string[] args = ["obfuscate", "--output", outputPath, "--manifest", manifestPath, "--create-output-dir", .. rest];

        var (exit, text) = RunCliCapturingOutput(args);

        Assert.Equal(0, exit);
        Assert.Contains("Usage:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("cli-help-secret-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-flag", text, StringComparison.Ordinal);
        Assert.DoesNotContain("cli-help-missing.csv", text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_tempDir, "cli-help-out")));
    }

    [Fact]
    public void Cli_BareHelpAfterCommand_IsNotHelp()
    {
        var (exit, text) = RunCliCapturingOutput(["obfuscate", "help"]);

        Assert.Equal(1, exit);
        Assert.Contains("Options must start with '--'", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_UnknownCommandWithHelp_StillFails()
    {
        var (exit, text) = RunCliCapturingOutput(["not-a-command", "--help"]);

        Assert.Equal(1, exit);
        Assert.Contains("Unknown command.", text, StringComparison.Ordinal);
    }

    private static (int Exit, string Text) RunCliCapturingOutput(string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var originalIn = Console.In;
        Console.SetOut(stdout);
        Console.SetError(stderr);
        Console.SetIn(new StringReader("cli-stdin-passphrase"));
        try
        {
            var exit = ObfuscatorCliProgram.Run(args);
            return (exit, stderr.ToString() + stdout.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Console.SetIn(originalIn);
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
        Assert.True(manifest.UnparsedValueCounts.TryGetValue("Serial", out var n), "Serial unparsed count");
        Assert.Equal(1, n);

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

        _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath);

        var obfuscatedLines = File.ReadAllLines(obfuscatedPath, Encoding.UTF8);
        Assert.Equal(4, obfuscatedLines.Length);
        Assert.Equal(",", obfuscatedLines[2]);
    }

    [Fact]
    public void Obfuscate_DateAtMaxValue_DoesNotThrow()
    {
        var inputPath = Path.Combine(_tempDir, "date-max-input.csv");
        var obfuscatedPath = Path.Combine(_tempDir, "date-max-obfuscated.csv");
        var manifestPath = Path.Combine(_tempDir, "date-max.obf");
        File.WriteAllLines(inputPath, ["Expires", "9999-12-31"], Encoding.UTF8);

        var manifest = _obfuscator.ObfuscateCsv(inputPath, obfuscatedPath, manifestPath);
        var spec = Assert.Single(manifest.Columns);
        var orig = DateTimeOffset.Parse("9999-12-31", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        var target = orig.UtcTicks + spec.DateShiftTicks;
        DateTimeOffset expected;
        if (target > DateTimeOffset.MaxValue.UtcTicks)
            expected = DateTimeOffset.MaxValue;
        else if (target < DateTimeOffset.MinValue.UtcTicks)
            expected = DateTimeOffset.MinValue;
        else
            expected = new DateTimeOffset(target, TimeSpan.Zero);
        var format = spec.DateFormat ?? "O";
        var obfuscated = File.ReadAllLines(obfuscatedPath, Encoding.UTF8)[1];
        Assert.Equal(expected.ToString(format, CultureInfo.InvariantCulture), obfuscated);

        var restoredPath = Path.Combine(_tempDir, "date-max-restored.csv");
        _obfuscator.DeobfuscateCsv(obfuscatedPath, manifestPath, restoredPath);
        var backTicks = expected.UtcTicks - spec.DateShiftTicks;
        DateTimeOffset restoredExpected;
        if (backTicks > DateTimeOffset.MaxValue.UtcTicks)
            restoredExpected = DateTimeOffset.MaxValue;
        else if (backTicks < DateTimeOffset.MinValue.UtcTicks)
            restoredExpected = DateTimeOffset.MinValue;
        else
            restoredExpected = new DateTimeOffset(backTicks, TimeSpan.Zero);
        var restored = File.ReadAllLines(restoredPath, Encoding.UTF8)[1];
        Assert.Equal(restoredExpected.ToString(format, CultureInfo.InvariantCulture), restored);
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
        File.WriteAllText(manifest, "KEEP-EXISTING-MANIFEST", Encoding.UTF8);
        var before = Convert.ToHexString(SHA256.HashData(payload));
        var manifestBefore = HashFile(manifest);

        Assert.ThrowsAny<Exception>(() =>
            _obfuscator.ObfuscateCsv(input, output, manifest, new ObfuscationOptions { Force = true }));

        Assert.True(File.Exists(output));
        Assert.Equal(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))));
        Assert.Equal(manifestBefore, HashFile(manifest));
        AssertOnlyEntries("preexist-input.csv", "preexist-obfuscated.csv", "preexist.obf");
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
        AssertOnlyEntries("partial-input.csv");
    }

    [Fact]
    public void Obfuscate_OutputEqualsManifest_ThrowsAndPreservesFile()
    {
        var input = Path.Combine(_tempDir, "om-in.csv");
        var output = Path.Combine(_tempDir, "om-out.csv");
        File.WriteAllText(input, "Name\nAlice\n", Encoding.UTF8);
        File.WriteAllText(output, "KEEP-EXISTING-OUTPUT", Encoding.UTF8);
        var before = HashFile(output);

        Assert.Throws<InvalidOperationException>(() =>
            _obfuscator.ObfuscateCsv(input, output, output, new ObfuscationOptions { Force = true }));

        Assert.Equal(before, HashFile(output));
        AssertOnlyEntries("om-in.csv", "om-out.csv");
    }

    [Fact]
    public void Obfuscate_ManifestEqualsInput_ThrowsAndPreservesInput()
    {
        var input = Path.Combine(_tempDir, "mi-in.csv");
        var output = Path.Combine(_tempDir, "mi-out.csv");
        File.WriteAllText(input, "Name\nAlice\n", Encoding.UTF8);
        var before = HashFile(input);

        Assert.Throws<InvalidOperationException>(() =>
            _obfuscator.ObfuscateCsv(input, output, input, new ObfuscationOptions { Force = true }));

        Assert.Equal(before, HashFile(input));
        AssertOnlyEntries("mi-in.csv");
    }

    [Fact]
    public void Obfuscate_ManifestRelativeAliasOfOutput_Throws()
    {
        var input = Path.Combine(_tempDir, "alias-in.csv");
        var output = Path.Combine(_tempDir, "alias-out.csv");
        Directory.CreateDirectory(Path.Combine(_tempDir, "sub"));
        File.WriteAllText(input, "Name\nAlice\n", Encoding.UTF8);

        foreach (var manifest in new[]
                 {
                     Path.Combine(_tempDir, ".", "alias-out.csv"),
                     Path.Combine(_tempDir, "sub", "..", "alias-out.csv")
                 })
        {
            Assert.Throws<InvalidOperationException>(() =>
                _obfuscator.ObfuscateCsv(input, output, manifest, new ObfuscationOptions { Force = true }));
        }

        AssertOnlyEntries("alias-in.csv", "sub");
    }

    [Fact]
    public void Obfuscate_ManifestCaseOnlyAliasOfOutput_Throws()
    {
        var input = Path.Combine(_tempDir, "case-in.csv");
        var output = Path.Combine(_tempDir, "case-out.csv");
        var manifest = Path.Combine(_tempDir, "CASE-OUT.csv");
        File.WriteAllText(input, "Name\nAlice\n", Encoding.UTF8);

        Assert.Throws<InvalidOperationException>(() =>
            _obfuscator.ObfuscateCsv(input, output, manifest, new ObfuscationOptions { Force = true }));

        AssertOnlyEntries("case-in.csv");
    }

    [Fact]
    public void Deobfuscate_OutputEqualsManifest_ThrowsAndPreservesManifest()
    {
        var input = Path.Combine(_tempDir, "do-in.csv");
        var obfuscated = Path.Combine(_tempDir, "do-obf.csv");
        var manifest = Path.Combine(_tempDir, "do.obf");
        File.WriteAllText(input, "Name\nAlice\n", Encoding.UTF8);
        _obfuscator.ObfuscateCsv(input, obfuscated, manifest);
        var before = HashFile(manifest);

        Assert.Throws<InvalidOperationException>(() =>
            _obfuscator.DeobfuscateCsv(obfuscated, manifest, manifest, force: true));

        Assert.Equal(before, HashFile(manifest));
        AssertOnlyEntries("do-in.csv", "do-obf.csv", "do.obf");
    }

    [SkippableFact]
    public void Obfuscate_OutputThroughSymlinkedDirectoryIsInput_ThrowsAndPreservesInput()
    {
        var real = Path.Combine(_tempDir, "real");
        var link = Path.Combine(_tempDir, "link");
        Directory.CreateDirectory(real);
        var input = Path.Combine(real, "sd-in.csv");
        File.WriteAllText(input, "Name\nAlice\n", Encoding.UTF8);
        CreateSymbolicLinkOrSkip(() => Directory.CreateSymbolicLink(link, real));
        var before = HashFile(input);

        Assert.Throws<InvalidOperationException>(() =>
            _obfuscator.ObfuscateCsv(
                input,
                Path.Combine(link, "sd-in.csv"),
                Path.Combine(_tempDir, "sd.obf"),
                new ObfuscationOptions { Force = true }));

        Assert.Equal(before, HashFile(input));
        AssertOnlyEntries("link", "real");
        Assert.Equal(["sd-in.csv"], Directory.EnumerateFileSystemEntries(real).Select(Path.GetFileName));
    }

    [SkippableFact]
    public void Obfuscate_ManifestIsRelativeSymlinkToInput_Throws()
    {
        var input = Path.Combine(_tempDir, "sl-in.csv");
        var output = Path.Combine(_tempDir, "sl-out.csv");
        var manifest = Path.Combine(_tempDir, "sl.obf");
        File.WriteAllText(input, "Name\nAlice\n", Encoding.UTF8);
        CreateSymbolicLinkOrSkip(() => File.CreateSymbolicLink(manifest, "sl-in.csv"));
        var before = HashFile(input);

        Assert.Throws<InvalidOperationException>(() =>
            _obfuscator.ObfuscateCsv(input, output, manifest, new ObfuscationOptions { Force = true }));

        Assert.Equal(before, HashFile(input));
        Assert.Equal("sl-in.csv", new FileInfo(manifest).LinkTarget);
        AssertOnlyEntries("sl-in.csv", "sl.obf");
    }

    [SkippableFact]
    public void Deobfuscate_OutputThroughSymlinkedDirectoryIsManifest_ThrowsAndPreservesManifest()
    {
        var input = Path.Combine(_tempDir, "dl-in.csv");
        var obfuscated = Path.Combine(_tempDir, "dl-obf.csv");
        var manifest = Path.Combine(_tempDir, "dl.obf");
        var link = Path.Combine(_tempDir, "link");
        File.WriteAllText(input, "Name\nAlice\n", Encoding.UTF8);
        _obfuscator.ObfuscateCsv(input, obfuscated, manifest);
        CreateSymbolicLinkOrSkip(() => Directory.CreateSymbolicLink(link, _tempDir));
        var before = HashFile(manifest);

        Assert.Throws<InvalidOperationException>(() =>
            _obfuscator.DeobfuscateCsv(obfuscated, manifest, Path.Combine(link, "dl.obf"), force: true));

        Assert.Equal(before, HashFile(manifest));
        AssertOnlyEntries("dl-in.csv", "dl-obf.csv", "dl.obf", "link");
    }

    // Creating a symlink needs a privilege or Developer Mode on Windows.
    private static void CreateSymbolicLinkOrSkip(Action create)
    {
        try
        {
            create();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Skip.If(true, $"cannot create a symbolic link here: {ex.Message}");
        }
    }

    [Fact]
    public void Obfuscate_BlankPassphrase_WithForce_PreservesBothExistingOutputs()
    {
        var input = Path.Combine(_tempDir, "ms-in.csv");
        var output = Path.Combine(_tempDir, "ms-out.csv");
        var manifest = Path.Combine(_tempDir, "ms.obf");
        File.WriteAllText(input, "Name\nAlice\n", Encoding.UTF8);
        File.WriteAllText(output, "KEEP-EXISTING-OUTPUT", Encoding.UTF8);
        File.WriteAllText(manifest, "KEEP-EXISTING-MANIFEST", Encoding.UTF8);
        var outputBefore = HashFile(output);
        var manifestBefore = HashFile(manifest);

        Assert.ThrowsAny<Exception>(() =>
            _obfuscator.ObfuscateCsv(input, output, manifest, new ObfuscationOptions { Force = true, Passphrase = " " }));

        Assert.Equal(outputBefore, HashFile(output));
        Assert.Equal(manifestBefore, HashFile(manifest));
        AssertOnlyEntries("ms-in.csv", "ms-out.csv", "ms.obf");
    }

    [Fact]
    public void Obfuscate_ManifestStageFailure_WithForce_PreservesExistingOutput()
    {
        // The CSV stages successfully; the manifest cannot be staged because its directory is missing.
        var input = Path.Combine(_tempDir, "ms2-in.csv");
        var output = Path.Combine(_tempDir, "ms2-out.csv");
        var manifest = Path.Combine(_tempDir, "missing-dir", "ms2.obf");
        File.WriteAllText(input, "Name\nAlice\n", Encoding.UTF8);
        File.WriteAllText(output, "KEEP-EXISTING-OUTPUT", Encoding.UTF8);
        var outputBefore = HashFile(output);

        Assert.ThrowsAny<IOException>(() =>
            _obfuscator.ObfuscateCsv(input, output, manifest, new ObfuscationOptions { Force = true }));

        Assert.Equal(outputBefore, HashFile(output));
        AssertOnlyEntries("ms2-in.csv", "ms2-out.csv");
    }

    [Fact]
    public void Obfuscate_ManifestPublishFailure_WithForce_RestoresExistingOutput()
    {
        var input = Path.Combine(_tempDir, "mp-in.csv");
        var output = Path.Combine(_tempDir, "mp-out.csv");
        var manifest = Path.Combine(_tempDir, "mp.obf");
        File.WriteAllText(input, "Name\nAlice\n", Encoding.UTF8);
        File.WriteAllText(output, "KEEP-EXISTING-OUTPUT", Encoding.UTF8);
        Directory.CreateDirectory(manifest);
        var outputBefore = HashFile(output);

        Assert.ThrowsAny<Exception>(() =>
            _obfuscator.ObfuscateCsv(input, output, manifest, new ObfuscationOptions { Force = true }));

        Assert.Equal(outputBefore, HashFile(output));
        Assert.True(Directory.Exists(manifest));
        Assert.Empty(Directory.EnumerateFileSystemEntries(manifest));
        AssertOnlyEntries("mp-in.csv", "mp-out.csv", "mp.obf");
    }

    [Fact]
    public void Obfuscate_WithForce_OverExistingPair_RoundTripsAndLeavesNoStagingFiles()
    {
        var input = Path.Combine(_tempDir, "fp-in.csv");
        var output = Path.Combine(_tempDir, "fp-out.csv");
        var manifest = Path.Combine(_tempDir, "fp.obf");
        var restored = Path.Combine(_tempDir, "fp-restored.csv");
        File.WriteAllText(input, "Name\nAlice\n", Encoding.UTF8);
        File.WriteAllText(output, "KEEP-EXISTING-OUTPUT", Encoding.UTF8);
        File.WriteAllText(manifest, "KEEP-EXISTING-MANIFEST", Encoding.UTF8);

        _obfuscator.ObfuscateCsv(input, output, manifest, new ObfuscationOptions { Force = true });
        _obfuscator.DeobfuscateCsv(output, manifest, restored);

        Assert.Contains("Alice", File.ReadAllText(restored, Encoding.UTF8), StringComparison.Ordinal);
        AssertOnlyEntries("fp-in.csv", "fp-out.csv", "fp.obf", "fp-restored.csv");
    }

    private static string HashFile(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private void AssertOnlyEntries(params string[] names)
    {
        var actual = Directory.EnumerateFileSystemEntries(_tempDir)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(names.Order(StringComparer.Ordinal).ToArray(), actual);
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

    /// <summary>
    /// Golden is a macOS snapshot. Box-Muller (Math.Log/Sin) last bits differ on
    /// glibc/ucrt, so byte-compare fails on Linux/Windows CI. Non-float fields and
    /// row layout must still match; floats may differ by a few ULPs.
    /// </summary>
    private static void AssertSemiconductorGoldenMatches(string goldenPath, string actualPath)
    {
        var golden = File.ReadAllLines(goldenPath);
        var actual = File.ReadAllLines(actualPath);
        Assert.Equal(golden.Length, actual.Length);
        Assert.Equal(golden[0], actual[0]);
        for (var row = 1; row < golden.Length; row++)
        {
            var g = golden[row].Split(',');
            var a = actual[row].Split(',');
            Assert.True(g.Length == a.Length, $"row {row} field count {g.Length} vs {a.Length}");
            for (var col = 0; col < g.Length; col++)
            {
                if (LooksLikeFloat(g[col]) && LooksLikeFloat(a[col])
                    && double.TryParse(g[col], NumberStyles.Float, CultureInfo.InvariantCulture, out var gd)
                    && double.TryParse(a[col], NumberStyles.Float, CultureInfo.InvariantCulture, out var ad))
                {
                    var tol = Math.Max(1e-12, Math.Abs(gd) * 1e-12);
                    Assert.True(
                        Math.Abs(gd - ad) <= tol,
                        $"row {row} col {col}: {g[col]} vs {a[col]}");
                    continue;
                }

                Assert.Equal(g[col], a[col]);
            }
        }
    }

    private static bool LooksLikeFloat(string field)
        => field.Contains('.', StringComparison.Ordinal)
           || field.Contains('e', StringComparison.OrdinalIgnoreCase);

    private static string? FindOnPath(string fileName)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (dir.Length == 0)
                continue;
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
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

    // Git for Windows ships an MSYS gpg that reports a POSIX "Home:" and treats C:\ paths as relative.
    private static bool GpgIsMsysOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return false;
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
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            foreach (var line in stdout.Split('\n'))
            {
                if (line.StartsWith("Home:", StringComparison.Ordinal))
                    return line["Home:".Length..].TrimStart().StartsWith('/');
            }
            return false;
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
