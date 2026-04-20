using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Squalor.Obfuscator;

internal static class ManifestCrypto
{
    private const string PlainHeader = "OBF_PLAIN_V2";
    private const string AesHeader = "OBF_AES_V2";
    private const string HeaderSeparator = "\n";
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static void WriteManifest(
        string path,
        string json,
        string? passphrase,
        IReadOnlyList<string>? gpgRecipients)
    {
        if (!string.IsNullOrWhiteSpace(passphrase))
        {
            var encrypted = EncryptAes(json, passphrase);
            File.WriteAllText(path, $"{AesHeader}{HeaderSeparator}{encrypted}", Utf8NoBom);
        }
        else
        {
            File.WriteAllText(path, $"{PlainHeader}{HeaderSeparator}{json}", Utf8NoBom);
        }

        if (gpgRecipients is { Count: > 0 })
            GpgEncryptInPlace(path, gpgRecipients);
    }

    public static string ReadManifest(string path, string? passphrase)
    {
        if (LooksLikeBinary(path))
            return GpgDecryptToString(path);

        var text = File.ReadAllText(path, Encoding.UTF8);
        text = text.TrimStart('\uFEFF');

        if (TryReadHeaderPayload(text, PlainHeader, out var plainPayload))
            return plainPayload;

        if (TryReadHeaderPayload(text, AesHeader, out var aesPayload))
        {
            if (string.IsNullOrWhiteSpace(passphrase))
                throw new InvalidOperationException("Manifest is AES-encrypted. Passphrase required.");

            return DecryptAes(aesPayload, passphrase);
        }

        return text;
    }

    private static string EncryptAes(string plaintext, string passphrase)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        var salt = RandomNumberGenerator.GetBytes(16);
        aes.Key = DeriveKey(passphrase, salt, 150_000);

        using var ms = new MemoryStream();
        ms.Write(salt);
        ms.Write(aes.IV);

        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs, Encoding.UTF8))
        {
            sw.Write(plaintext);
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    private static string DecryptAes(string base64, string passphrase)
    {
        var bytes = Convert.FromBase64String(base64);
        var salt = bytes[..16];
        var iv = bytes[16..32];
        var cipher = bytes[32..];

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        aes.Key = DeriveKey(passphrase, salt, 150_000);
        aes.IV = iv;

        using var ms = new MemoryStream(cipher);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var sr = new StreamReader(cs, Encoding.UTF8);
        return sr.ReadToEnd();
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations)
    {
        var key = new byte[32];
        Rfc2898DeriveBytes.Pbkdf2(
            password: passphrase,
            salt: salt,
            iterations: iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            destination: key);
        return key;
    }

    private static bool LooksLikeBinary(string path)
    {
        using var fs = File.OpenRead(path);
        var buffer = new byte[Math.Min(16, (int)fs.Length)];
        fs.ReadExactly(buffer);

        if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            return false;

        return buffer.Any(b => b == 0);
    }

    private static bool TryReadHeaderPayload(string text, string header, out string payload)
    {
        payload = string.Empty;

        if (!text.StartsWith(header, StringComparison.Ordinal))
            return false;

        if (text.Length == header.Length)
        {
            payload = string.Empty;
            return true;
        }

        var next = text[header.Length];
        if (next == '\n')
        {
            payload = text[(header.Length + 1)..];
            return true;
        }

        if (next == '\r' && text.Length > header.Length + 1 && text[header.Length + 1] == '\n')
        {
            payload = text[(header.Length + 2)..];
            return true;
        }

        return false;
    }

    private static void GpgEncryptInPlace(string path, IReadOnlyList<string> recipients)
    {
        var output = path + ".gpg";

        var psi = new ProcessStartInfo
        {
            FileName = "gpg",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        psi.ArgumentList.Add("--yes");
        psi.ArgumentList.Add("--batch");
        psi.ArgumentList.Add("--trust-model");
        psi.ArgumentList.Add("always");
        psi.ArgumentList.Add("--encrypt");

        foreach (var r in recipients)
        {
            psi.ArgumentList.Add("--recipient");
            psi.ArgumentList.Add(r);
        }

        psi.ArgumentList.Add("--output");
        psi.ArgumentList.Add(output);
        psi.ArgumentList.Add(path);

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("Failed to start gpg. Make sure GPG is installed and available on PATH.", ex);
        }

        if (process is null)
            throw new InvalidOperationException("Failed to start gpg. Make sure GPG is installed and available on PATH.");

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"gpg encrypt failed. Make sure GPG is installed and available on PATH.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
        }

        File.Delete(path);
        File.Move(output, path);
    }

    private static string GpgDecryptToString(string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "gpg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        psi.ArgumentList.Add("--yes");
        psi.ArgumentList.Add("--batch");
        psi.ArgumentList.Add("--decrypt");
        psi.ArgumentList.Add(path);

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("Failed to start gpg. Make sure GPG is installed and available on PATH.", ex);
        }

        if (process is null)
            throw new InvalidOperationException("Failed to start gpg. Make sure GPG is installed and available on PATH.");

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"gpg decrypt failed. Make sure GPG is installed and available on PATH.{Environment.NewLine}{stderr}");

            return stdout;
        }
    }
}
