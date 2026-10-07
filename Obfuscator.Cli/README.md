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

Supply the manifest passphrase with `OBFUSCATOR_PASSPHRASE`, `--passphrase-file <path>`, or `--passphrase-stdin`. Inline `--passphrase <secret>` is rejected because command-line arguments are visible in process lists. Prefer `OBFUSCATOR_DETERMINISTIC_KEY` over `--deterministic-key` for the same reason.

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

- `--deterministic-key <secret>` derives transforms from the key plus the **per-manifest salt**. The same key does not produce the same tokens across two separate obfuscate runs.
- `--string-mode auto|mapping|deterministic-token`
- `--include <colA,colB>` and `--exclude <colA,colB>`
- `--allow-list` means only included columns are obfuscated.
- `--seed <int>` makes non-keyed transforms reproducible.
- `--passphrase-file` / `--passphrase-stdin` / `OBFUSCATOR_PASSPHRASE` encrypt the manifest with AES-GCM. Inline `--passphrase` is not accepted.
- `--gpg-recipient <recipient>` can be repeated to GPG-encrypt the manifest in place.
- `--preserve-blanks true|false`
- `--create-output-dir` creates missing parent directories for `--output` and `--manifest`

Manifest notes:

- plain and AES-encrypted manifests can be written on one OS and read on another
- `--gpg-recipient` requires `gpg` to be installed and available on `PATH`

## Security contract

Obfuscation changes selected cell values for controlled development, testing, and data workflows. It is reversible when the required manifest and, for deterministic-token mode, the deterministic key are available. It transforms data; it does not encrypt the CSV or guarantee that a dataset is anonymous or safe to share.

The output retains CSV structure, including column names and row order. Excluded columns, preserved blank cells, and other untransformed values remain visible. Transformations can preserve relationships: repeated values remain equal, numeric transformations generally preserve ordering or its reversal, date shifts preserve time intervals except when values hit supported bounds, and deterministic tokens reveal equality and frequency within a run. Tokens use the per-manifest salt, so the same key does not create stable tokens across separate obfuscation runs. Deterministic tokens use AES-CBC with a fixed per-column IV, which also leaks shared plaintext block prefixes. Auxiliary information may let a recipient infer original values.

Treat the manifest as sensitive source data. Plain manifests can contain original values in mapping mode and include information needed to reverse transformations. AES-GCM or GPG can encrypt the manifest when configured; neither encrypts the CSV or protects the original input. The unkeyed manifest checksum detects accidental edits, not malicious tampering. Review each dataset's selected and retained columns, values, structure, and likely auxiliary information before sharing. The project makes no guarantee against reidentification or a determined recipient. The current implementation needs no external cryptography dependency.

## Config notes

Generation config supports:

- `rowMode: "fixed"` for exact `nRows`
- `rowMode: "sweep"` for full sweep expansion
- `rowMode: "max"` for repeating a sweep pattern until `nRows` is reached
- `sweepAxes` to define outer-to-inner sweep order
- per-column `values` for discrete sweep points
- `generatedIdMode: "outer-group"` and `generatedIdMode: "inner-step"` for sweep IDs
- `--create-output-dir` to create missing parent directories for output files instead of failing

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
- Works without a deterministic key.
- Better for lower-cardinality columns.

`deterministic-token`

- Reversible using the shared deterministic key, without storing every distinct value in the manifest.
- Better for high-cardinality string columns.
- Produces stable tokens for the same input value within the same column and key.
- Requires passing the same `--deterministic-key` during deobfuscation.

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
