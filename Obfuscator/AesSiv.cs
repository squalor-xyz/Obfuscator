// SPDX-License-Identifier: MPL-2.0
/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/
using System.Security.Cryptography;

namespace Squalor.Obfuscator;

// AES-SIV-CMAC-256 (RFC 5297) built on the BCL AES block cipher, which has no CMAC or SIV API.
// Deterministic authenticated encryption: equal (key, AD, plaintext) give equal output; any
// other difference changes the whole output. Output is the 16-byte synthetic IV followed by
// the CTR ciphertext. Verified against RFC 4493 and RFC 5297 Appendix A vectors in the tests.
internal sealed class AesSiv : IDisposable
{
    public const int KeySizeBytes = 32;
    private const int BlockSize = 16;

    // K1 (leftmost half) keys S2V/CMAC; K2 (rightmost half) keys CTR.
    private readonly Aes _macAes;
    private readonly Aes _ctrAes;
    private readonly byte[] _cmacSubkey1;
    private readonly byte[] _cmacSubkey2;

    public AesSiv(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySizeBytes)
            throw new ArgumentException($"AES-SIV key must be {KeySizeBytes} bytes.", nameof(key));

        _macAes = Aes.Create();
        _macAes.Key = key[..BlockSize].ToArray();
        _ctrAes = Aes.Create();
        _ctrAes.Key = key[BlockSize..].ToArray();

        // RFC 4493 section 2.3: L = AES(K, 0^128), K1 = dbl(L), K2 = dbl(K1).
        var l = _macAes.EncryptEcb(new byte[BlockSize], PaddingMode.None);
        _cmacSubkey1 = Dbl(l);
        _cmacSubkey2 = Dbl(_cmacSubkey1);
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext, params ReadOnlySpan<byte[]> associatedData)
    {
        var v = S2V(associatedData, plaintext);
        var output = new byte[BlockSize + plaintext.Length];
        v.CopyTo(output, 0);
        Ctr(v, plaintext, output.AsSpan(BlockSize));
        return output;
    }

    public byte[] Decrypt(ReadOnlySpan<byte> sivAndCiphertext, params ReadOnlySpan<byte[]> associatedData)
    {
        if (sivAndCiphertext.Length < BlockSize)
            throw new CryptographicException("AES-SIV input is shorter than the synthetic IV.");

        var v = sivAndCiphertext[..BlockSize].ToArray();
        var plaintext = new byte[sivAndCiphertext.Length - BlockSize];
        Ctr(v, sivAndCiphertext[BlockSize..], plaintext);

        var t = S2V(associatedData, plaintext);
        if (!CryptographicOperations.FixedTimeEquals(t, v))
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new CryptographicException("AES-SIV authentication failed.");
        }

        return plaintext;
    }

    public void Dispose()
    {
        _macAes.Dispose();
        _ctrAes.Dispose();
        CryptographicOperations.ZeroMemory(_cmacSubkey1);
        CryptographicOperations.ZeroMemory(_cmacSubkey2);
    }

    // RFC 5297 section 2.4, with the plaintext as the final string Sn.
    private byte[] S2V(ReadOnlySpan<byte[]> associatedData, ReadOnlySpan<byte> plaintext)
    {
        var d = Cmac(new byte[BlockSize]);
        foreach (var ad in associatedData)
            Xor(Dbl(d), Cmac(ad), d);

        byte[] t;
        if (plaintext.Length >= BlockSize)
        {
            // xorend: XOR D into the last 16 bytes of Sn.
            t = plaintext.ToArray();
            var tail = t.AsSpan(t.Length - BlockSize);
            Xor(tail, d, tail);
        }
        else
        {
            t = Dbl(d);
            var padded = Pad(plaintext);
            Xor(t, padded, t);
        }

        return Cmac(t);
    }

    // RFC 4493 AES-CMAC.
    internal byte[] Cmac(ReadOnlySpan<byte> message)
    {
        var blockCount = Math.Max(1, (message.Length + BlockSize - 1) / BlockSize);
        var lastComplete = message.Length > 0 && message.Length % BlockSize == 0;

        var last = new byte[BlockSize];
        var lastStart = (blockCount - 1) * BlockSize;
        if (lastComplete)
            Xor(message[lastStart..], _cmacSubkey1, last);
        else
            Xor(Pad(message[lastStart..]), _cmacSubkey2, last);

        var x = new byte[BlockSize];
        for (var i = 0; i < blockCount - 1; i++)
        {
            Xor(x, message.Slice(i * BlockSize, BlockSize), x);
            _macAes.EncryptEcb(x, x, PaddingMode.None);
        }

        Xor(x, last, x);
        _macAes.EncryptEcb(x, x, PaddingMode.None);
        return x;
    }

    // RFC 5297 section 2.5/2.6: Q = V with bits 63 and 31 cleared, then a 128-bit big-endian counter.
    private void Ctr(ReadOnlySpan<byte> v, ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (input.Length == 0)
            return;

        var blockCount = (input.Length + BlockSize - 1) / BlockSize;
        var counters = new byte[blockCount * BlockSize];
        var q = v.ToArray();
        q[8] &= 0x7f;
        q[12] &= 0x7f;
        for (var i = 0; i < blockCount; i++)
        {
            q.CopyTo(counters, i * BlockSize);
            Increment(q);
        }

        var keystream = _ctrAes.EncryptEcb(counters, PaddingMode.None);
        Xor(input, keystream.AsSpan(0, input.Length), output);
    }

    private static void Increment(byte[] counter)
    {
        for (var i = counter.Length - 1; i >= 0; i--)
        {
            if (++counter[i] != 0)
                break;
        }
    }

    // Multiply by x in GF(2^128) with the RFC polynomial x^128 + x^7 + x^2 + x + 1.
    private static byte[] Dbl(ReadOnlySpan<byte> s)
    {
        var result = new byte[BlockSize];
        for (var i = 0; i < BlockSize - 1; i++)
            result[i] = (byte)((s[i] << 1) | (s[i + 1] >> 7));
        result[BlockSize - 1] = (byte)(s[BlockSize - 1] << 1);
        if ((s[0] & 0x80) != 0)
            result[BlockSize - 1] ^= 0x87;
        return result;
    }

    // 10* padding of a partial block.
    private static byte[] Pad(ReadOnlySpan<byte> partial)
    {
        var padded = new byte[BlockSize];
        partial.CopyTo(padded);
        padded[partial.Length] = 0x80;
        return padded;
    }

    private static void Xor(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, Span<byte> destination)
    {
        for (var i = 0; i < destination.Length; i++)
            destination[i] = (byte)(a[i] ^ b[i]);
    }
}
