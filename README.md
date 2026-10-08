# obfuscator

`Obfuscator` is a .NET library with a small CLI for:

- generating synthetic CSV datasets
- generating ordered sweep datasets for test and characterization flows
- reversibly obfuscating CSV data
- restoring obfuscated CSV data from a manifest

Most code should use `Squalor.Obfuscator` directly. The CLI is there for scripting and ad-hoc usage.

## Requirements

What you need depends on how you want to use it:

- library consumer: a .NET 10-compatible SDK/runtime in the app that references `Squalor.Obfuscator`
- CLI user: the .NET 10 SDK/CLI to install the tool with `dotnet tool install`, plus a .NET 10 runtime to run it
- local development in this repo: the .NET 10 SDK
- optional GPG manifest encryption: GnuPG (`gpg`) installed and available on `PATH`

There are no other native dependencies.

## Install

Packages are **not on nuget.org** until a `v*` tag (ancestor of `main`) runs with the `NUGET_API_KEY` secret. That workflow always packs and attaches nupkgs to the GitHub Release.

When nuget.org has a version:

```bash
dotnet add package Squalor.Obfuscator
dotnet tool install --global Squalor.Obfuscator.Cli
obfuscator --help
```

## Quick start

Generate a simple fixed-row dataset from the repo example:

```bash
obfuscator generate \
  --config Obfuscator/ExampleConfig.json \
  --output fake.csv
```

Generate a semiconductor-style sweep dataset (81-row golden; do not overwrite the committed fixture):

```bash
obfuscator generate \
  --config Obfuscator/ExampleConfigSemiconductor.json \
  --create-output-dir \
  --output semi.csv
```

Generate a **demo** sweep for squalplot (adds a `Site` facet; 162 rows). This is not the golden:

```bash
obfuscator generate \
  --config Obfuscator/ExampleConfigSemiconductorDemo.json \
  --create-output-dir \
  --output semi-demo.csv
```

`ExampleConfigSemiconductor.golden.csv` and suite/databall `fixtures/semiconductor-sweep.csv` stay frozen. Do not regenerate them from the demo config.

Obfuscate and restore a CSV. Supply the manifest passphrase through `OBFUSCATOR_PASSPHRASE`, `--passphrase-file <path>`, or `--passphrase-stdin`:

```bash
export OBFUSCATOR_PASSPHRASE
export OBFUSCATOR_DETERMINISTIC_KEY
obfuscator obfuscate \
  --input real.csv \
  --output real.obfuscated.csv \
  --manifest real.obf \
  --create-output-dir \
  --string-mode deterministic-token

obfuscator deobfuscate \
  --input real.obfuscated.csv \
  --manifest real.obf \
  --output real.restored.csv \
  --create-output-dir
```

The CLI rejects inline `--passphrase <secret>` because command-line arguments are visible in process lists. The library `ObfuscationOptions.Passphrase` and `DeobfuscateCsv(passphrase:)` APIs are unchanged.

Existing `--output` and `--manifest` files are refused unless you pass `--force`. `--input`, `--output`, and `--manifest` must be three different files: paths are compared after `Path.GetFullPath`, ignoring case on every OS (symlinks and hard links are not resolved). Obfuscate stages the CSV and the manifest next to their targets and publishes them only after both are complete. If either fails, neither is published and any files they would have replaced are restored. Generate and deobfuscate write their single output to `path.tmp`, then `File.Move`. The input CSV delimiter is stored on the manifest and restored on deobfuscate.

Manifest notes:

- plain (`OBF_PLAIN_V2`) and AES-GCM (`OBF_AESGCM_V3`) manifests use a stable text header, so a manifest written on one OS can be read on another
- new passphrase-protected writes use AES-GCM. `OBF_AES_V2` (CBC) is unsupported; re-obfuscate to GCM. `OBF_GPG_V2` is unchanged.
- deterministic tokens written before manifest version 2.1 (`OBF_TKN_`, AES-CBC with a fixed per-column IV, which leaks shared plaintext prefixes) can still be deobfuscated but are never written; re-obfuscate to get AES-SIV `OBF_TK2_` tokens
- `--gpg-recipient` support is optional and requires `gpg` to be installed and available on `PATH`
- **The manifest is the plaintext.** Mapping mode stores the full original-to-token map. An unencrypted `.obf` next to the obfuscated CSV is not protection.

## Security contract

Obfuscation changes selected cell values to support controlled development, testing, and data workflows. It is reversible when the required manifest and, for deterministic-token mode, the deterministic key are available. This is data transformation, not encryption of the CSV or a guarantee that a dataset is anonymous or safe to share.

The output retains the CSV structure, including column names and row order. Excluded columns, preserved blank cells, and other untransformed values remain visible. Transformations can also preserve useful relationships: repeated values remain equal; numeric transformations generally preserve ordering or its reversal; date shifts preserve time intervals except when values hit supported bounds; and deterministic tokens reveal equality and frequency within a run. Tokens are derived using the per-manifest salt, so the same key does not create stable tokens across separate obfuscation runs. Deterministic tokens use AES-SIV (RFC 5297) deterministic authenticated encryption with a per-column key: equal values in a column give equal tokens, any other difference changes the whole token, and an edited token fails to deobfuscate. Tokens also reveal each value's UTF-8 length rounded up to 16 bytes. A recipient with auxiliary information may infer original values even when direct values have changed.

Treat the manifest as sensitive source data. Plain manifests can contain original values in mapping mode and include information needed to reverse transformations. AES-GCM or GPG can encrypt the manifest when configured; that does not encrypt the CSV, protect the original input, or hide information inferable from the output. The unkeyed manifest checksum detects accidental edits, not malicious tampering.

Use obfuscation for controlled workflows where these residual disclosures are acceptable. Review selected and retained columns, values, structure, and likely auxiliary information for each dataset before sharing it. The project makes no guarantee against reidentification or a determined recipient. No external cryptography dependency is required by the current implementation; AES-SIV is implemented in-repo on the .NET AES block cipher and checked against the RFC 4493 and RFC 5297 test vectors.

## Data generation modes

Generation config is explicit via `rowMode`:

- `fixed`: generate exactly `nRows` independent rows
- `sweep`: generate every combination defined by `sweepAxes`
- `max`: generate at least `nRows`; if the sweep expansion is smaller, the sweep pattern repeats

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

For sweep configs:

- `sweepAxes` defines the outer-to-inner loop order
- `values` defines the discrete points for each sweep axis
- `generatedIdMode: "outer-group"` creates one ID per outer stimulus group
- `generatedIdMode: "inner-step"` creates a 1-based counter for the innermost sweep axis

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

The example config paths above are repo-relative. If you are using the published packages outside this repo, point to your own config file.

For CLI output paths:

- use `--create-output-dir` to create missing parent directories for `--output` and `--manifest`

## Local development

See [CONTRIBUTING.md](CONTRIBUTING.md) for the roadmap and review workflow (one git worktree per slicer item, handed off for review before merge).

Run the local smoke test:

```bash
./scripts/test-local.sh
```

Run the unit tests directly:

```bash
dotnet test Obfuscator.Tests/Obfuscator.Tests.csproj
```

The local smoke-test script packs both projects with version `0.0.0-local` so temp installs do not look like real releases.

For deeper CLI and config examples, see [Obfuscator.Cli/README.md](/Users/jon/code/squalor-xyz/obfuscator/Obfuscator.Cli/README.md).

## Release flow

Pushing a tag matching `v*` for a commit reachable from `main` triggers the GitHub Actions release workflow. The workflow tests the solution, packs the library and CLI, and creates a GitHub release with the generated `.nupkg` and `.snupkg` files attached.

## Using release artifacts before NuGet.org

Until `nuget.org` publishing is configured, release artifacts are distributed through GitHub Releases.

Each tagged release currently produces:

- `Squalor.Obfuscator.<version>.nupkg`
- `Squalor.Obfuscator.<version>.snupkg`
- `Squalor.Obfuscator.Cli.<version>.nupkg`
- `Squalor.Obfuscator.Cli.<version>.snupkg`

You can download those files from the GitHub Release page and use them locally.

Install the CLI from a downloaded release package:

```bash
dotnet tool install \
  --tool-path ./tools/obfuscator \
  --add-source /path/to/downloaded/packages \
  Squalor.Obfuscator.Cli
```

Consume the library from a downloaded release package:

```bash
dotnet add package Squalor.Obfuscator --source /path/to/downloaded/packages
```

For now, GitHub Releases is the distribution point until `nuget.org` publishing is added.

## Versioning

Released package versions are currently driven by Git tags.

- create a tag like `v0.1.0`
- push the tag to GitHub
- the release workflow strips the leading `v`
- the workflow passes that value to `dotnet pack` as the package `Version`

Example:

```bash
git tag v0.1.0
git push origin v0.1.0
```

If you later add `<VersionPrefix>` to the `.csproj` files, that becomes the default local/package base version when no explicit `Version` is supplied. In the current release workflow, the tag-based `Version` would still override it.

## License

This repository is licensed under the Mozilla Public License 2.0 (`MPL-2.0`). CsvHelper 33.1.0 is `MS-PL OR Apache-2.0`; this project elects Apache-2.0 (see [NOTICE](NOTICE)).
