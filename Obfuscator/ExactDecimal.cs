// SPDX-License-Identifier: MPL-2.0
/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Squalor.Obfuscator;

// An exact decimal number, Mantissa x 10^Exponent, so the floating-column transform
// y = x * Scale + Shift can be inverted without rounding. Always normalized: the mantissa
// has no trailing zeros, and zero is (0, 0).
internal readonly struct ExactDecimal
{
    // Bounds keep a hostile cell (e.g. "1e-99999999" or a megabyte of digits) from forcing a huge
    // BigInteger. Every finite double fits well inside them; anything outside is left unparsed.
    private const int MaxDigits = 1000;
    private const int MaxMagnitude = 1100;

    // Plain notation pads with at most this many zeros; beyond it, write d.dddE+n.
    private const int MaxPlainPaddingZeros = 15;

    public BigInteger Mantissa { get; }
    public int Exponent { get; }

    public ExactDecimal(BigInteger mantissa, int exponent)
    {
        if (mantissa.IsZero)
        {
            exponent = 0;
        }
        else
        {
            while (true)
            {
                var quotient = BigInteger.DivRem(mantissa, 10, out var remainder);
                if (!remainder.IsZero)
                    break;
                mantissa = quotient;
                exponent++;
            }
        }

        Mantissa = mantissa;
        Exponent = exponent;
    }

    // Accepts a subset of what double.TryParse takes with NumberStyles.Float | AllowThousands and
    // the invariant culture: surrounding whitespace, a leading sign, digits with ',' group separators
    // in the integer part, an optional '.' fraction, and an optional e/E exponent. No NaN or Infinity.
    public static bool TryParse(string? text, out ExactDecimal result)
    {
        result = default;
        if (text is null)
            return false;

        var s = text.AsSpan().Trim();
        var i = 0;
        var negative = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
        {
            negative = s[i] == '-';
            i++;
        }

        var digits = new StringBuilder();
        var intDigits = 0;
        while (i < s.Length && (char.IsAsciiDigit(s[i]) || (s[i] == ',' && intDigits > 0)))
        {
            if (s[i] != ',')
            {
                digits.Append(s[i]);
                intDigits++;
            }
            i++;
        }

        var fracDigits = 0;
        if (i < s.Length && s[i] == '.')
        {
            i++;
            while (i < s.Length && char.IsAsciiDigit(s[i]))
            {
                digits.Append(s[i]);
                fracDigits++;
                i++;
            }
        }

        if (intDigits + fracDigits == 0)
            return false;

        var exponent = 0;
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            i++;
            var expNegative = false;
            if (i < s.Length && (s[i] == '+' || s[i] == '-'))
            {
                expNegative = s[i] == '-';
                i++;
            }

            var expStart = i;
            while (i < s.Length && char.IsAsciiDigit(s[i]))
            {
                // Bounded so the accumulator cannot overflow; the magnitude check below rejects it.
                if (i - expStart >= 6)
                    return false;
                exponent = (exponent * 10) + (s[i] - '0');
                i++;
            }

            if (i == expStart)
                return false;
            if (expNegative)
                exponent = -exponent;
        }

        if (i != s.Length)
            return false;

        var significant = digits.ToString().TrimStart('0');
        if (significant.Length > MaxDigits)
            return false;

        var mantissa = significant.Length == 0
            ? BigInteger.Zero
            : BigInteger.Parse(significant, NumberStyles.None, CultureInfo.InvariantCulture);
        var value = new ExactDecimal(negative ? -mantissa : mantissa, exponent - fracDigits);
        if (!value.Mantissa.IsZero
            && (value.Exponent < -MaxMagnitude || value.Exponent + DigitCount(value.Mantissa) > MaxMagnitude))
            return false;

        result = value;
        return true;
    }

    public static ExactDecimal Parse(string text)
        => TryParse(text, out var value) ? value : throw new FormatException("Not an exact decimal number.");

    public ExactDecimal Add(ExactDecimal other)
    {
        var exponent = Math.Min(Exponent, other.Exponent);
        return new ExactDecimal(
            (Mantissa * BigInteger.Pow(10, Exponent - exponent)) + (other.Mantissa * BigInteger.Pow(10, other.Exponent - exponent)),
            exponent);
    }

    public ExactDecimal Subtract(ExactDecimal other) => Add(new ExactDecimal(-other.Mantissa, other.Exponent));

    public ExactDecimal Multiply(ExactDecimal other) => new(Mantissa * other.Mantissa, Exponent + other.Exponent);

    // Succeeds only when the quotient is itself a finite decimal. The divisor's factors of 2 and 5 are
    // absorbed by scaling the dividend by 10 at most bit-length times; any other factor must divide exactly.
    public bool TryDivideExact(ExactDecimal divisor, out ExactDecimal quotient)
    {
        quotient = default;
        if (divisor.Mantissa.IsZero)
            return false;

        var dividend = Mantissa;
        var exponent = Exponent - divisor.Exponent;
        var attempts = (long)divisor.Mantissa.GetBitLength();
        while (true)
        {
            var q = BigInteger.DivRem(dividend, divisor.Mantissa, out var remainder);
            if (remainder.IsZero)
            {
                quotient = new ExactDecimal(q, exponent);
                return true;
            }

            if (attempts-- <= 0)
                return false;
            dividend *= 10;
            exponent--;
        }
    }

    public override string ToString()
    {
        if (Mantissa.IsZero)
            return "0";

        var sign = Mantissa.Sign < 0 ? "-" : string.Empty;
        var digits = BigInteger.Abs(Mantissa).ToString(CultureInfo.InvariantCulture);
        var padding = Exponent >= 0 ? Exponent : Math.Max(0, -Exponent - digits.Length);

        if (padding > MaxPlainPaddingZeros)
        {
            var scientificExponent = Exponent + digits.Length - 1;
            var fraction = digits.Length > 1 ? "." + digits[1..] : string.Empty;
            return string.Create(CultureInfo.InvariantCulture,
                $"{sign}{digits[0]}{fraction}E{(scientificExponent < 0 ? "-" : "+")}{Math.Abs(scientificExponent)}");
        }

        if (Exponent >= 0)
            return sign + digits + new string('0', Exponent);

        var pointAt = digits.Length + Exponent;
        return pointAt > 0
            ? sign + digits[..pointAt] + "." + digits[pointAt..]
            : sign + "0." + new string('0', -pointAt) + digits;
    }

    private static int DigitCount(BigInteger value) => BigInteger.Abs(value).ToString(CultureInfo.InvariantCulture).Length;
}
