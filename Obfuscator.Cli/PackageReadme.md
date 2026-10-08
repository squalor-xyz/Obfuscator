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

Generated data is synthetic. The generator builds every value from the JSON config alone and never reads an input CSV. It uses `System.Random`, which is not a cryptographic generator; set `seed` for reproducible output. Use generated datasets as fixtures, demos, and test inputs. They are not a source of secrets or keys, and they say nothing about whether a real dataset is safe to share.

Obfuscate a CSV and restore it, reading the passphrase and deterministic key from the environment:

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

Supply the manifest passphrase with `OBFUSCATOR_PASSPHRASE`, `--passphrase-file <path>`, or `--passphrase-stdin`. Supply the deterministic key with `OBFUSCATOR_DETERMINISTIC_KEY` or `--deterministic-key-file <path>`. Inline `--passphrase <secret>` and `--deterministic-key <secret>` are rejected because command-line arguments are visible in process lists. Without a passphrase or `--gpg-recipient`, the manifest is written unencrypted. Each command rejects any option it does not use, including options that belong to another command, so a typo such as `--gpg-recipent` fails before any output is written. Options take their value as the next argument; `--option=value` is not accepted. `--help` or `-h` anywhere after a command prints the usage and exits without reading or writing anything.

If you use `--gpg-recipient`, `gpg` must be installed and available on `PATH`.

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
