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

Library package:

```bash
dotnet add package Squalor.Obfuscator
```

CLI package:

```bash
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

Generate a semiconductor-style sweep dataset:

```bash
obfuscator generate \
  --config Obfuscator/ExampleConfigSemiconductor.json \
  --create-output-dir \
  --output semi.csv
```

Obfuscate and restore a CSV. Prefer env vars or a file for secrets (`--passphrase` on the command line is visible in process lists):

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

`--passphrase-file` and `--passphrase-stdin` are also accepted. `--passphrase` still works and is insecure.

Existing `--output` and `--manifest` files are refused unless you pass `--force`. Input and output must be different paths (`Path.GetFullPath`, ordinal). Writes go to `path.tmp` then `File.Move`. The input CSV delimiter is stored on the manifest and restored on deobfuscate.

Manifest notes:

- plain, AES-CBC (`OBF_AES_V2`), and AES-GCM (`OBF_AESGCM_V3`) manifests use a stable text header, so a manifest written on one OS can be read on another
- new passphrase-protected writes use AES-GCM; V2 CBC files remain readable
- `--gpg-recipient` support is optional and requires `gpg` to be installed and available on `PATH`
- **The manifest is the plaintext.** Mapping mode stores the full original-to-token map. An unencrypted `.obf` next to the obfuscated CSV is not protection.
- Deterministic tokens (`DeterministicKey`) are the same for a repeated value **within one run**. Each obfuscate run mints a new manifest salt, so the same key does **not** produce the same token across two files. Joinability requires the salt from that file's manifest. Deterministic tokens still reveal value equality and frequency inside one file. CBC with a fixed per-column IV also leaks shared block prefixes (lot/week/wafer grouping on fixed-width serials). AES-SIV would keep determinism without prefix leakage; it is not implemented yet.

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

The example config paths above are repo-relative. If you are using the published packages outside this repo, point to your own config file.

For CLI output paths:

- use `--create-output-dir` to create missing parent directories for `--output` and `--manifest`

## Local development

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
