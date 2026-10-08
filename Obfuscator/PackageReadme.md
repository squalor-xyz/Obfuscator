# Squalor.Obfuscator

`Squalor.Obfuscator` is a .NET library for generating CSV fixtures and performing reversible CSV obfuscation/deobfuscation.

## Requirements

- a .NET 10-compatible SDK/runtime in the app that references this package
- GnuPG (`gpg`) on `PATH` only if you use GPG-encrypted manifests

## Install

```bash
dotnet add package Squalor.Obfuscator
```

## Example

```csharp
using Squalor.Obfuscator;

var obfuscator = new Obfuscator();

obfuscator.GenerateCsvFromConfig("ExampleConfig.json", "fake.csv");

obfuscator.ObfuscateCsv(
    inputCsvPath: "real.csv",
    outputCsvPath: "real.obfuscated.csv",
    obfPath: "real.obf",
    options: new ObfuscationOptions
    {
        DeterministicKey = Environment.GetEnvironmentVariable("OBFUSCATOR_DETERMINISTIC_KEY"),
        Passphrase = Environment.GetEnvironmentVariable("OBFUSCATOR_PASSPHRASE"),
        StringMode = StringObfuscationMode.DeterministicToken
    });
```

Read the passphrase and deterministic key from the environment or a secret store, as above; do not hard-code them. With no `Passphrase` and no `GpgRecipients`, the manifest is written unencrypted. `DeobfuscateCsv` takes the same passphrase and, for deterministic-token columns, the same key.

`GenerateCsvFromConfig` accepts any path to a compatible JSON config file. The example repo includes sample configs, but they are not shipped inside the package.

Manifest compatibility:

- `OBF_PLAIN_V2` (unencrypted), `OBF_AESGCM_V3` (passphrase), and `OBF_GPG_V2` (GPG) manifests start with a stable text header, so a manifest written on one OS can be read on another
- `OBF_AES_V2` (AES-CBC) manifests from earlier versions are no longer read; re-obfuscate with a passphrase to get an AES-GCM manifest
- deterministic tokens written before manifest version 2.1 (`OBF_TKN_`, AES-CBC with a fixed per-column IV, which leaks shared plaintext prefixes) can still be deobfuscated but are never written; re-obfuscate to get AES-SIV `OBF_TK2_` tokens
- GPG manifests need GnuPG (`gpg`) on `PATH` both to write and to read them

## Security contract

Obfuscation changes selected cell values for controlled development, testing, and data workflows. It is reversible when the manifest and, for columns transformed with a deterministic key, that key are available. It transforms data; it does not encrypt the CSV or guarantee that a dataset is anonymous or safe to share.

**What the output reveals.** The output keeps the CSV structure, including column names and row order. Excluded columns, preserved blank cells, and other untransformed values remain visible. Transformations can preserve relationships: repeated values remain equal, numeric transformations generally preserve ordering or its reversal, date shifts preserve time intervals except when values hit supported bounds, and deterministic tokens reveal equality and frequency within a run. A recipient with auxiliary information may infer original values even when direct values have changed.

**Deterministic keys and tokens.** A deterministic key is stretched with PBKDF2-SHA256 (600,000 iterations) over the key and a random per-manifest salt, and each transform's key is derived from that with HKDF-SHA256. Because of the salt, the same key does not produce the same tokens or transforms across separate obfuscation runs. Deterministic tokens use AES-SIV (RFC 5297) deterministic authenticated encryption with a per-column key: equal values in a column give equal tokens, any other difference changes the whole token, and an edited token fails to deobfuscate. Tokens reveal each value's UTF-8 length rounded up to 16 bytes. Deobfuscating deterministic-token columns requires the same key. Without a key, numeric and date transforms come from `System.Random` (not a cryptographic generator; a seed makes them reproducible), mapping tokens are random GUIDs, and the manifest records what is needed to reverse them.

**Manifest protection.** Treat the manifest as sensitive source data: mapping mode stores every distinct original value it maps, and every manifest holds what is needed to reverse the transformations. By default the manifest is written unencrypted (`OBF_PLAIN_V2`), and an unencrypted manifest next to the obfuscated CSV is not protection. A passphrase encrypts the manifest with AES-256-GCM under a key from PBKDF2-SHA256 (600,000 iterations, random salt). GPG recipients encrypt it with `gpg`; with both a passphrase and recipients, GPG wraps the AES-GCM manifest. Neither encrypts the CSV, protects the original input, or hides what can be inferred from the output. The manifest's unkeyed checksum detects accidental edits, not malicious tampering.

**Before sharing.** Use obfuscation for controlled workflows where these residual disclosures are acceptable. Review each dataset's selected and retained columns, values, structure, and likely auxiliary information before sharing it. The project makes no guarantee against reidentification or a determined recipient. Cryptography uses only the .NET base class library; AES-SIV is implemented in this project on the .NET AES block cipher and tested against the RFC 4493 and RFC 5297 vectors.

## Data generation

Generation configs support:

- `rowMode: "fixed"` for independent rows
- `rowMode: "sweep"` for explicit sweep expansion
- `rowMode: "max"` for repeating a sweep pattern up to `nRows`

Sweep configs can also define:

- `sweepAxes` for outer-to-inner loop order
- per-column `values` for discrete sweep points
- `generatedIdMode: "outer-group"` and `generatedIdMode: "inner-step"` for sweep identifiers

Generated data is synthetic. The generator builds every value from the JSON config alone and never reads an input CSV. It uses `System.Random`, which is not a cryptographic generator; set `seed` for reproducible output. Use generated datasets as fixtures, demos, and test inputs. They are not a source of secrets or keys, and they say nothing about whether a real dataset is safe to share.

Generation configs are validated before any rows are generated or output is written. A config is rejected when:

- a sweep axis is repeated (names compare case-insensitively), or an axis is also a `generatedIdMode` column
- an axis repeats a value; on numeric columns values compare numerically, so `5` and `5.0` are duplicates
- a numeric axis value does not parse as a finite number or lies outside `totalRangeMin`..`totalRangeMax`
- `totalRangeMin` is greater than `totalRangeMax`, or an integer column's range does not fit its data type
- only one of `idealRangeMin`/`idealRangeMax` is set, they are reversed, or they lie outside the total range
- `truePercentage` or `percentageInIdealRange` is outside 0..100, or `randomStringLength` is negative
- `dateMinUtc`/`dateMaxUtc` do not parse, or the minimum is after the maximum (missing bounds default to 2020-01-01 and 2020-01-31)
- `tracksWith`/`tracksInverselyWith` names a missing, non-numeric, or later column, or the column itself
- a `generatedIdMode` column is used with `rowMode: "fixed"`
- the sweep expansion (the product of every axis's value count) or `nRows` exceeds `Array.MaxLength` (2,147,483,591) rows. The generator indexes rows with `int` and holds the row plan in memory, so this is the largest size it can represent. It is not a recommended size, and very large sweeps may still exhaust memory.

## CLI

```bash
dotnet tool install --global Squalor.Obfuscator.Cli
```
