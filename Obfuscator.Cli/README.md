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
  --deterministic-key your-stable-secret-key \
  --string-mode deterministic-token \
  --passphrase strong-passphrase
```

Restore an obfuscated CSV:

```bash
obfuscator deobfuscate \
  --input real.obfuscated.csv \
  --manifest real.obf \
  --output real.restored.csv \
  --create-output-dir \
  --deterministic-key your-stable-secret-key \
  --passphrase strong-passphrase
```

Supported commands:

- `generate --config <file.json> --output <file.csv>`
- `obfuscate --input <file.csv> --output <file.csv> --manifest <file.obf>`
- `deobfuscate --input <file.csv> --manifest <file.obf> --output <file.csv>`

Useful obfuscation options:

- `--deterministic-key <secret>` enables stable deterministic transforms across runs.
- `--string-mode auto|mapping|deterministic-token`
- `--include <colA,colB>` and `--exclude <colA,colB>`
- `--allow-list` means only included columns are obfuscated.
- `--seed <int>` makes non-keyed transforms reproducible.
- `--passphrase <secret>` AES-encrypts the manifest.
- `--gpg-recipient <recipient>` can be repeated to GPG-encrypt the manifest in place.
- `--preserve-blanks true|false`
- `--create-output-dir` creates missing parent directories for `--output` and `--manifest`

Manifest notes:

- plain and AES-encrypted manifests can be written on one OS and read on another
- `--gpg-recipient` requires `gpg` to be installed and available on `PATH`

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

var manifest = obfuscator.ObfuscateCsv(
    inputCsvPath: "real.csv",
    outputCsvPath: "real.obfuscated.csv",
    obfPath: "real.obf",
    options: new ObfuscationOptions
    {
        DeterministicKey = "your-stable-secret-key",
        StringMode = StringObfuscationMode.DeterministicToken,
        Passphrase = "strong-passphrase",
        IncludeColumns = new List<string> { "CustomerId", "Revenue", "CreatedUtc", "Region" },
        ExcludeColumns = new List<string> { "NonSensitiveFlag" }
    });

obfuscator.DeobfuscateCsv(
    obfuscatedCsvPath: "real.obfuscated.csv",
    obfPath: "real.obf",
    outputCsvPath: "real.restored.csv",
    passphrase: "strong-passphrase",
    deterministicKey: "your-stable-secret-key");
```

## Notes

- The installed tool command name is `obfuscator`.
- The NuGet tool package id is `Squalor.Obfuscator.Cli`.
- The CLI is mainly for scripts and manual runs; most code should use the library directly.
- `mapping` mode scans the full input to build a complete reversible map for string columns.
- `deterministic-token` mode reduces manifest growth for high-cardinality string columns.
