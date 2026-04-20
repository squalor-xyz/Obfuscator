# Squalor.Obfuscator.Cli

Requirements:

- .NET 10 SDK/CLI to install the tool
- .NET 10 runtime to run it
- GnuPG (`gpg`) on `PATH` only if you use `--gpg-recipient`

Install the CLI as a .NET tool:

```bash
dotnet tool install --global Squalor.Obfuscator.Cli
```

Then run:

```bash
obfuscator --help
```

Generate a simple dataset:

```bash
obfuscator generate --config ./config.json --output fake.csv
```

Generate a sweep dataset:

```bash
obfuscator generate --config ./semiconductor-config.json --create-output-dir --output ~/tmp/semi.csv
```

Pass your own JSON config files when using the published CLI package. The repository examples are for development/reference and are not included in the installed tool.

If you use `--gpg-recipient`, `gpg` must be installed and available on `PATH`.
