using System.Globalization;
using System.Text;

namespace Foldcraft.Core.Icons;

/// <summary>
/// Decides whether text is one emoji. The value ends up in file names' hashes, theme files and
/// the renderer, so it is held to this short list rather than accepted as any text.
/// </summary>
public static class EmojiText
{
    /// <summary>The longest real sequences, such as a family with skin tones, stay well under this.</summary>
    private const int MaxLength = 32;

    private const int ZeroWidthJoiner = 0x200D;
    private const int VariationSelectorText = 0xFE0E;
    private const int VariationSelectorEmoji = 0xFE0F;
    private const int CombiningKeycap = 0x20E3;

    /// <summary>True when the text is exactly one emoji, with any modifiers that belong to it.</summary>
    public static bool IsValid(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength)
        {
            return false;
        }

        var elements = StringInfo.GetTextElementEnumerator(text);

        if (!elements.MoveNext() || elements.MoveNext())
        {
            return false;
        }

        var keycap = text.EnumerateRunes().Any(rune => rune.Value == CombiningKeycap);
        var pictures = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            if (IsPicture(rune.Value))
            {
                pictures++;
            }
            else if (!IsModifier(rune.Value) && !(keycap && IsKeycapBase(rune)))
            {
                return false;
            }
        }

        return pictures > 0 || keycap;
    }

    private static bool IsModifier(int value) =>
        value is ZeroWidthJoiner or VariationSelectorText or VariationSelectorEmoji or CombiningKeycap ||
        value is >= 0xE0020 and <= 0xE007F;

    private static bool IsKeycapBase(Rune rune) => rune.Value is '#' or '*' or (>= '0' and <= '9');

    private static bool IsPicture(int value) =>
        value is >= 0x1F000 and <= 0x1FAFF
            or >= 0x2600 and <= 0x27BF
            or >= 0x2B00 and <= 0x2BFF
            or >= 0x2190 and <= 0x21FF
            or >= 0x2300 and <= 0x23FF
            or >= 0x25A0 and <= 0x25FF
            or >= 0x2900 and <= 0x297F
            or 0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139 or 0x3030 or 0x303D or 0x3297 or 0x3299;
}
