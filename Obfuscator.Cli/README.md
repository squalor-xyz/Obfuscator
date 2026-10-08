# Obfuscator CLI

The CLI is the command-line wrapper around the `Squalor.Obfuscator` library.

## Requirements

- .NET 10 SDK/CLI to install the tool with `dotnet tool install`
- .NET 10 runtime to run the installed tool
- GnuPG (`gpg`) on `PATH` only if you use `--gpg-recipient`

If you are developing in this repo, use the .NET 10 SDK.

## Install

As a .NET tool:

```bash
dotnet tool install --global Squalor.Obfuscator.Cli
```

For local development from the repo:

```bash
dotnet run --project Obfuscator.Cli -- --help
```

## Commands

Generate fixture CSV data:

```bash
obfuscator generate --config ./config.json --output fake.csv
```

Generate a semiconductor-style sweep dataset:

```bash
obfuscator generate --config ./semiconductor-config.json --create-output-dir --output ~/tmp/semi.csv
```

Obfuscate a CSV and write a manifest:

```bash
obfuscator obfuscate \
  --input real.csv \
  --output real.obfuscated.csv \
  --manifest real.obf \
  --create-output-dir \
  --string-mode deterministic-token
```

Supply the manifest passphrase with `OBFUSCATOR_PASSPHRASE`, `--passphrase-file <path>`, or `--passphrase-stdin`. Supply the deterministic key with `OBFUSCATOR_DETERMINISTIC_KEY` or `--deterministic-key-file <path>`. Inline `--passphrase <secret>` and `--deterministic-key <secret>` are rejected because command-line arguments are visible in process lists. Without a passphrase or `--gpg-recipient`, the manifest is written unencrypted. Each command rejects any option it does not use, including options that belong to another command, so a typo such as `--gpg-recipent` fails before any output is written. Options take their value as the next argument; `--option=value` is not accepted. `--help` or `-h` anywhere after a command prints the usage and exits without reading or writing anything.

Restore an obfuscated CSV:

```bash
obfuscator deobfuscate \
  --input real.obfuscated.csv \
  --manifest real.obf \
  --output real.restored.csv \
  --create-output-dir
```

Supported commands:

- `generate --config <file.json> --output <file.csv>`
- `obfuscate --input <file.csv> --output <file.csv> --manifest <file.obf>`
- `deobfuscate --input <file.csv> --manifest <file.obf> --output <file.csv>`

Useful obfuscation options:

- `--deterministic-key-file <path>` / `OBFUSCATOR_DETERMINISTIC_KEY` derive transforms from the key plus the **per-manifest salt**. The same key does not produce the same tokens across two separate obfuscate runs.
- `--string-mode auto|mapping|deterministic-token`
- `--include <colA,colB>` and `--exclude <colA,colB>`
- `--allow-list` means only included columns are obfuscated.
- `--seed <int>` makes non-keyed transforms reproducible.
- `--passphrase-file` / `--passphrase-stdin` / `OBFUSCATOR_PASSPHRASE` encrypt the manifest with AES-GCM. Inline `--passphrase` is not accepted.
- `--gpg-recipient <recipient>` can be repeated to GPG-encrypt the manifest in place.
- `--preserve-blanks true|false`
- `--strict` fails when a value does not parse as its column's inferred kind, instead of falling back to string obfuscation.
- `--create-output-dir` creates missing parent directories for `--output` and `--manifest`
- `--force` overwrites an existing `--output` or `--manifest` file; without it, existing files are refused.

Deobfuscation options:

- `--passphrase-file` / `--passphrase-stdin` / `OBFUSCATOR_PASSPHRASE` decrypt an AES-GCM manifest.
- `--deterministic-key-file <path>` / `OBFUSCATOR_DETERMINISTIC_KEY` supply the key used at obfuscation. Inline `--deterministic-key` is not accepted.
- `--allow-mismatched-source` restores a CSV whose file name differs from the one recorded in the manifest.
- `--create-output-dir` and `--force` behave as for obfuscation.

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

## Config notes

Generation config supports:

- `rowMode: "fixed"` for exact `nRows`
- `rowMode: "sweep"` for full sweep expansion
- `rowMode: "max"` for repeating a sweep pattern until `nRows` is reached
- `sweepAxes` to define outer-to-inner sweep order
- per-column `values` for discrete sweep points
- `generatedIdMode: "outer-group"` and `generatedIdMode: "inner-step"` for sweep IDs
- `--create-output-dir` to create missing parent directories for output files instead of failing

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
- a `generatedIdMode` column's `totalRangeMin`..`totalRangeMax` cannot hold the IDs it needs: one per combination of the outer axes' values for `outer-group`, or one per innermost axis value for `inner-step`. IDs start at `totalRangeMin`, and each `outer-group` column counts its groups separately.
- the sweep expansion (the product of every axis's value count) or `nRows` exceeds `Array.MaxLength` (2,147,483,591) rows. The generator indexes rows with `int` and holds the row plan in memory, so this is the largest size it can represent. It is not a recommended size, and very large sweeps may still exhaust memory.

The `ExampleConfig*.json` files live in the repo for reference. When using the installed tool elsewhere, pass your own config file path.

## Data generation examples

Fixed-row example:

```json
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
```

Sweep example:

```json
{
  "rowMode": "sweep",
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
```

Key fields:

- `sweepAxes` controls the nested loop order from outermost to innermost
- per-column `values` supplies the discrete points for each sweep axis
- `generatedIdMode: "outer-group"` generates one ID per outer stimulus group
- `generatedIdMode: "inner-step"` generates a 1-based counter across the innermost axis

## String obfuscation modes

`mapping`

- Reversible by manifest lookup.
- Requires storing one entry per distinct original value in the manifest.
- Works without a deterministic key; with one, tokens are derived from the key and the per-manifest salt instead of random GUIDs.
- Better for lower-cardinality columns.

`deterministic-token`

- Reversible using the deterministic key, without storing every distinct value in the manifest.
- Better for high-cardinality string columns.
- Produces the same token for the same input value within one column of one obfuscation run; the per-manifest salt makes tokens differ across runs, even with the same key.
- Requires a deterministic key; the same key (`OBFUSCATOR_DETERMINISTIC_KEY` or `--deterministic-key-file`) is needed during deobfuscation.

`auto`

- Uses `deterministic-token` when a deterministic key is supplied.
- Falls back to `mapping` otherwise.

## Library example

```csharp
using Squalor.Obfuscator;

var obfuscator = new Obfuscator();

obfuscator.GenerateCsvFromConfig("ExampleConfig.json", "fake.csv");

obfuscator.GenerateCsvFromConfig("ExampleConfigSemiconductor.json", "semi.csv");
obfuscator.GenerateCsvFromConfig("ExampleConfigSemiconductorDemo.json", "semi-demo.csv");

var manifest = obfuscator.ObfuscateCsv(
    inputCsvPath: "real.csv",
    outputCsvPath: "real.obfuscated.csv",
    obfPath: "real.obf",
    options: new ObfuscationOptions
    {
        DeterministicKey = Environment.GetEnvironmentVariable("OBFUSCATOR_DETERMINISTIC_KEY"),
        StringMode = StringObfuscationMode.DeterministicToken,
        Passphrase = Environment.GetEnvironmentVariable("OBFUSCATOR_PASSPHRASE"),
        IncludeColumns = new List<string> { "CustomerId", "Revenue", "CreatedUtc", "Region" },
        ExcludeColumns = new List<string> { "NonSensitiveFlag" }
    });

obfuscator.DeobfuscateCsv(
    obfuscatedCsvPath: "real.obfuscated.csv",
    obfPath: "real.obf",
    outputCsvPath: "real.restored.csv",
    passphrase: Environment.GetEnvironmentVariable("OBFUSCATOR_PASSPHRASE"),
    deterministicKey: Environment.GetEnvironmentVariable("OBFUSCATOR_DETERMINISTIC_KEY"));
```

## Notes

- The installed tool command name is `obfuscator`.
- The NuGet tool package id is `Squalor.Obfuscator.Cli`.
- The CLI is mainly for scripts and manual runs; most code should use the library directly.
- `mapping` mode scans the full input to build a complete reversible map for string columns.
- `deterministic-token` mode reduces manifest growth for high-cardinality string columns.
