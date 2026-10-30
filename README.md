# obfuscator

<img src="assets/obfuscator-512.png" alt="Obfuscator" width="128" />

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

Obfuscate and restore a CSV, reading the passphrase and deterministic key from the environment:

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

Supply the manifest passphrase with `OBFUSCATOR_PASSPHRASE`, `--passphrase-file <path>`, or `--passphrase-stdin`. Supply the deterministic key with `OBFUSCATOR_DETERMINISTIC_KEY` or `--deterministic-key-file <path>`. Inline `--passphrase <secret>` and `--deterministic-key <secret>` are rejected because command-line arguments are visible in process lists. Without a passphrase or `--gpg-recipient`, the manifest is written unencrypted. Each command rejects any option it does not use, including options that belong to another command, so a typo such as `--gpg-recipent` fails before any output is written. Options take their value as the next argument; `--option=value` is not accepted. `--help` or `-h` anywhere after a command prints the usage and exits without reading or writing anything. The library `ObfuscationOptions.Passphrase` and `DeobfuscateCsv(passphrase:)` APIs take secrets directly; read them from the environment or a secret store rather than hard-coding them.

Existing `--output` and `--manifest` files are refused unless you pass `--force`. `--input`, `--output`, and `--manifest` must be three different files: paths are compared after `Path.GetFullPath` and after resolving symbolic links in every existing path component, ignoring case on every OS. Hard links are not detected: outputs are published by rename, so a hard-linked input keeps its original content. Obfuscate stages the CSV and the manifest next to their targets and publishes them only after both are complete. If either fails, neither is published and any files they would have replaced are restored. Generate and deobfuscate write their single output to `path.tmp`, then `File.Move`. The input CSV delimiter is stored on the manifest and restored on deobfuscate.

Manifest compatibility:

- `OBF_PLAIN_V2` (unencrypted), `OBF_AESGCM_V3` (passphrase), and `OBF_GPG_V2` (GPG) manifests start with a stable text header, so a manifest written on one OS can be read on another
- `OBF_AES_V2` (AES-CBC) manifests from earlier versions are no longer read; re-obfuscate with a passphrase to get an AES-GCM manifest
- deterministic tokens written before manifest version 2.1 (`OBF_TKN_`, AES-CBC with a fixed per-column IV, which leaks shared plaintext prefixes) can still be deobfuscated but are never written; re-obfuscate to get AES-SIV `OBF_TK2_` tokens
- floating-point columns in manifests before version 2.2 used a `double` transform and restore only approximately (the last digits can differ); re-obfuscate to get the exact decimal transform
- GPG manifests need GnuPG (`gpg`) on `PATH` both to write and to read them

## Security contract

Obfuscation changes selected cell values for controlled development, testing, and data workflows. It is reversible when the manifest and, for columns transformed with a deterministic key, that key are available. Floating-point columns are transformed with exact decimal arithmetic and restore to the same numeric value, though not always the same text: `1.50` comes back as `1.5` and `1,000.5` as `1000.5`. A floating-point cell outside the exact range (more than 1000 significant digits, or a magnitude beyond about 10^±1100, in the cell or in its transformed value) falls back to string obfuscation like any value that does not parse as its column's inferred kind, so `--strict` rejects it. It transforms data; it does not encrypt the CSV or guarantee that a dataset is anonymous or safe to share.

**What the output reveals.** The output keeps the CSV structure, including column names and row order. Excluded columns, preserved blank cells, and other untransformed values remain visible. Transformations can preserve relationships: repeated values remain equal, numeric transformations generally preserve ordering or its reversal, date shifts preserve time intervals except when values hit supported bounds, and deterministic tokens reveal equality and frequency within a run. Floating-point output is exact, so a cell's digits show roughly how many decimal places the original had, and a small value in a column with a large shift gives a long cell. A recipient with auxiliary information may infer original values even when direct values have changed.

**Deterministic keys and tokens.** A deterministic key is stretched with PBKDF2-SHA256 (600,000 iterations) over the key and a random per-manifest salt, and each transform's key is derived from that with HKDF-SHA256. Because of the salt, the same key does not produce the same tokens or transforms across separate obfuscation runs. Deterministic tokens use AES-SIV (RFC 5297) deterministic authenticated encryption with a per-column key: equal values in a column give equal tokens, any other difference changes the whole token, and an edited token fails to deobfuscate. Tokens reveal each value's UTF-8 length rounded up to 16 bytes. Deobfuscating deterministic-token columns requires the same key. Without a key, numeric and date transforms come from `System.Random` (not a cryptographic generator; a seed makes them reproducible), mapping tokens are random GUIDs, and the manifest records what is needed to reverse them.

**Manifest protection.** Treat the manifest as sensitive source data: mapping mode stores every distinct original value it maps, and every manifest holds what is needed to reverse the transformations. By default the manifest is written unencrypted (`OBF_PLAIN_V2`), and an unencrypted manifest next to the obfuscated CSV is not protection. A passphrase encrypts the manifest with AES-256-GCM under a key from PBKDF2-SHA256 (600,000 iterations, random salt). GPG recipients encrypt it with `gpg`; with both a passphrase and recipients, GPG wraps the AES-GCM manifest. Neither encrypts the CSV, protects the original input, or hides what can be inferred from the output. The manifest's unkeyed checksum detects accidental edits, not malicious tampering.

**Before sharing.** Use obfuscation for controlled workflows where these residual disclosures are acceptable. Review each dataset's selected and retained columns, values, structure, and likely auxiliary information before sharing it. The project makes no guarantee against reidentification or a determined recipient. Cryptography uses only the .NET base class library; AES-SIV is implemented in this project on the .NET AES block cipher and tested against the RFC 4493 and RFC 5297 vectors.

## Data generation modes

Generation config is explicit via `rowMode`:

- `fixed`: generate exactly `nRows` independent rows
- `sweep`: generate every combination defined by `sweepAxes`
- `max`: generate at least `nRows`; if the sweep expansion is smaller, the sweep pattern repeats
- `test-results`: generate a wide production test-results file (preamble, annotation rows, one row per device) from a nested `testResults` object; `nRows` is the device count

Generated data is synthetic. The generator builds every value from the JSON config alone and never reads an input CSV. It uses `System.Random`, which is not a cryptographic generator; set `seed` for reproducible output. Use generated datasets as fixtures, demos, and test inputs. They are not a source of secrets or keys, and they say nothing about whether a real dataset is safe to share.

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

### Test-results mode

`rowMode: "test-results"` writes the shape of a production test-program results file, so parsers can be tested on simulated data instead of real files. It is configured by a nested `testResults` object (see `Obfuscator/ExampleConfigTestResults.json`) and `nRows` is the device count: one data row per device. `columns` and `sweepAxes` stay empty. Output is written as raw bytes, not CSV: nothing is quoted, every field is space-padded (left-aligned) to a fixed width, and every line ends with the configured line ending (`lineEnding`: `crlf` by default, or `lf`), including the last. Rows are streamed to the file, so 100k devices by 200 test columns does not hold the file in memory. The output is deterministic for a `seed`.

The parts, in file order, each optional except the test columns:

- `preamble`: `Key<padding>:,value` lines. `keyWidth` pads every key to that many bytes (a longer key gets no padding). Each `entries` item is `{key, kind, ...}`: `static` (`value`), `date` (`value` as an ISO 8601 date or date-time read as UTC, printed with .NET `format`, default `yyyy-MM-dd HH:mm:ss`) or `counter` (prints `start`; `step` is accepted and kept for future appended blocks, but does not change the printed value). `inject` items `{key, offset, bytes}` overwrite bytes of that key's padded text, for example `[255]` (invalid UTF-8) or `[19]` (a control character), to simulate a corrupted key.
- `annotations`: labelled rows between the preamble and the header, in the listed order, each with `{label, source}`. `source` is `programLine` (`programLineStart + programLineStep * test column index`), `upperLimit`, `lowerLimit` (both in the test's print format; blank when the test has no such limit) or `unit`. The label plus a trailing `:` fills the first device column; the other device columns are blank.
- `deviceColumns`: `serial` (`name`, `width`, `start`, `step`), `site`, `proberX`, `proberY` (`name`, `width`, `value`, the same text on every row) and `bin` (`name`, `width`, `weights`, a map of bin value to relative weight). Each is optional and they appear in that order. Bins are drawn independently of the test values and of pass or fail.
- `conditionSets`: the test columns. Each item is either `{conditions: [[key, value], ...], tests}` (one condition set) or `{axes: [{key, values}, ...], keyOrder, tests}` (every combination of the axes, the first axis outermost; `keyOrder` lists the axis keys in bracket order and defaults to the declared order). Columns come in order: condition set, then test. A column is named `[k1=v1|k2=v2]Test(unit)`; with no conditions the bracket is left out, and with no unit so are the parentheses.
- Each test is `{name, unit, upperLimit, lowerLimit, distribution, format, nullRate, nullToken, outOfLimitRate, quirk}`. `distribution` is `uniform` (`min`, `max`) or `normal` (`mean`, `stdDev`). `format` is printf-style `%.Nf` or `%.Ne` (N from 0 to 20; the exponent has at least two digits, as in C), formatted with the invariant culture. A value whose printed form falls outside the limits is drawn again, never clamped onto a limit, so values do not pile up on the limits. The check is on the printed value and the limits are inclusive: a value can print exactly on a limit only when rounding puts it there. After 10,000 failed draws in a row for one cell generation fails with no output; validation rejects a distribution that lands inside the limits less than 1% of the time, but a print format too coarse to print any value inside the limits (for example `%.0f` with limits 1.2 to 1.8) is caught only at generation. Limits should be representable in `format`, or values near them are drawn again. `nullRate` is the percent of cells that hold `nullToken` (default `NA.`). `outOfLimitRate` is the percent of the non-null cells drawn just outside the nearer limit. `quirk` is `caseVariant` (the test name in the opposite case) or `repeatUnit` (the header reads `Test(V)(V)`); it changes only the columns of that test entry.
- Widths: a test column is as wide as its header (in UTF-8 bytes) plus `testColumnPadding` (default 4). A cell longer than its column fails generation, and no output file is written.

Excerpt of the example output (shortened to the first columns; the injected key holds bytes `0xFF 0x13`, shown here as `<FF><13>`):

```text
Program         :,sqDemoProgram_rev3
Lot             :,LOT-0042
Sta<FF><13>on         :,bench-07
Started         :,2026-02-03 09:30:00
Run             :,1000
Operator Notes  :,none
Line     :,      ,     ,     ,     ,1200                         ,1203                        ,1206 ...
Upper    :,      ,     ,     ,     ,30.000000000                 ,120.0000                    ,4.500 ...
Lower    :,      ,     ,     ,     ,22.000000000                 ,60.0000                     ,1.000 ...
Unit     :,      ,     ,     ,     ,dB                           ,mA                          ,dB ...
Serial    ,Site  ,X    ,Y    ,Bin  ,[Vdd=3.3|Temp=25]Gain(dB)    ,[Vdd=3.3|Temp=25]Idd(mA)    ,[Mode=low ...
5000      ,1     ,12   ,7    ,1    ,27.688963802                 ,118.5569                    ,1.438 ...
5001      ,1     ,12   ,7    ,1    ,NA.                          ,76.0755                     ,4.108 ...
```

Not covered: several sites per file, appended blocks, or reading such files back.

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
- for `rowMode: "test-results"`: `testResults` is missing, or is set with another `rowMode`, or `columns` or `sweepAxes` is also set; `nRows` (the device count) is below 0 or above `Array.MaxLength`
- a `testResults` value kind (preamble `kind`, annotation `source`, test `distribution` or `quirk`) or `lineEnding` is unknown, or a `format` is anything but `%.Nf` or `%.Ne`
- a width or `testColumnPadding` is negative, an annotation label with its colon does not fit the first device column, or annotations are set with no device columns
- two columns end up with the same name after the condition sets expand (names compare case-sensitively)
- `conditionSets` expand to no test columns, or a test's limit span, uniform range or normal spread is too large to stay finite
- a test's distribution lands inside its limits less than 1% of the time (a uniform range that misses the limits, or a normal far from them)
- a test's `upperLimit` is below its `lowerLimit`, `nullRate` or `outOfLimitRate` is outside 0..100, `outOfLimitRate` is set with no limit, or its distribution parameters are missing or reversed
- `bin` `weights` is empty, has a negative weight, or adds up to 0
- an `inject` names a key that is not in `entries`, has a byte outside 0..255, or does not fit inside the padded key
- a condition set has both or neither of `conditions` and `axes`, repeats an axis key, has an axis with no values, or lists a `keyOrder` that is not each axis key once
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

For deeper CLI and config examples, see [Obfuscator.Cli/README.md](Obfuscator.Cli/README.md).

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
