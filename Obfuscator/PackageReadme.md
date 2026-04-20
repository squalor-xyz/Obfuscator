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
        DeterministicKey = "your-stable-secret-key",
        StringMode = StringObfuscationMode.DeterministicToken
    });
```

`GenerateCsvFromConfig` accepts any path to a compatible JSON config file. The example repo includes sample configs, but they are not shipped inside the package.

Manifest notes:

- plain and AES-encrypted manifests can be moved between Windows, macOS, and Linux
- GPG-encrypted manifests require `gpg` to be installed and available on `PATH`

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
