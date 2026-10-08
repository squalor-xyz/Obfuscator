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

`GenerateCsvFromConfig` accepts any path to a compatible JSON config file. The example repo includes sample configs, but they are not shipped inside the package.

Manifest notes:

- plain and AES-encrypted manifests can be moved between Windows, macOS, and Linux
- GPG-encrypted manifests require `gpg` to be installed and available on `PATH`
- deterministic tokens written before manifest version 2.1 (`OBF_TKN_`, AES-CBC with a fixed per-column IV, which leaks shared plaintext prefixes) can still be deobfuscated but are never written; re-obfuscate to get AES-SIV `OBF_TK2_` tokens

## Security contract

Obfuscation changes selected cell values for controlled development, testing, and data workflows. It is reversible when the required manifest and, for deterministic-token mode, the deterministic key are available. It transforms data; it does not encrypt the CSV or guarantee that a dataset is anonymous or safe to share.

The output retains CSV structure, including column names and row order. Excluded columns, preserved blank cells, and other untransformed values remain visible. Transformations can preserve relationships: repeated values remain equal, numeric transformations generally preserve ordering or its reversal, date shifts preserve time intervals except when values hit supported bounds, and deterministic tokens reveal equality and frequency within a run. Tokens use the per-manifest salt, so the same key does not create stable tokens across separate obfuscation runs. Deterministic tokens use AES-SIV (RFC 5297) deterministic authenticated encryption with a per-column key: equal values in a column give equal tokens, any other difference changes the whole token, and an edited token fails to deobfuscate. Tokens also reveal each value's UTF-8 length rounded up to 16 bytes. Auxiliary information may let a recipient infer original values.

Treat the manifest as sensitive source data. Plain manifests can contain original values in mapping mode and include information needed to reverse transformations. AES-GCM or GPG can encrypt the manifest when configured; neither encrypts the CSV or protects the original input. The unkeyed manifest checksum detects accidental edits, not malicious tampering. Review each dataset's selected and retained columns, values, structure, and likely auxiliary information before sharing. The project makes no guarantee against reidentification or a determined recipient. The current implementation needs no external cryptography dependency.

## Data generation

Generation configs support:

- `rowMode: "fixed"` for independent rows
- `rowMode: "sweep"` for explicit sweep expansion
- `rowMode: "max"` for repeating a sweep pattern up to `nRows`

Sweep configs can also define:

- `sweepAxes` for outer-to-inner loop order
- per-column `values` for discrete sweep points
- `generatedIdMode: "outer-group"` and `generatedIdMode: "inner-step"` for sweep identifiers

## CLI

```bash
dotnet tool install --global Squalor.Obfuscator.Cli
```
