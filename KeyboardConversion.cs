using System;
using System.Collections.Generic;
using System.Text;

namespace LayoutPreviewPrototype;

/// <summary>Deterministic conversion by physical US keyboard position.</summary>
internal static class KeyboardConversion
{
    // Physical rows, indexed by the VK values used by the low-level hook.
    private const string UsLetters = "qwertyuiop[]asdfghjkl;'zxcvbnm,./";
    private const string RuLetters = "йцукенгшщзхъфывапролджэячсмитьбю.";

    private static readonly Dictionary<uint, char> UsByVk = BuildUsMap();

    public static string Convert(IReadOnlyList<uint> virtualKeys, bool toRussian)
    {
        var result = new StringBuilder(virtualKeys.Count);
        foreach (var vk in virtualKeys)
        {
            if (TryConvert(vk, toRussian, out var value))
                result.Append(value);
        }
        return result.ToString();
    }

    private static bool TryConvert(uint vk, bool toRussian, out char value)
    {
        if (UsByVk.TryGetValue(vk, out var us))
        {
            var index = UsLetters.IndexOf(us);
            value = toRussian && index >= 0 ? RuLetters[index] : us;
            return true;
        }

        // Digits and punctuation that have a stable physical position in both layouts.
        value = vk switch
        {
            >= 0x30 and <= 0x39 => (char)vk,
            0xBA => toRussian ? 'ж' : ';',
            0xBB => '=',
            0xBC => toRussian ? 'б' : ',',
            0xBD => '-',
            0xBE => toRussian ? 'ю' : '.',
            0xBF => toRussian ? '.' : '/',
            0xC0 => '`',
            0xDB => toRussian ? 'х' : '[',
            0xDC => '\\',
            0xDD => toRussian ? 'ъ' : ']',
            0xDE => toRussian ? 'э' : '\'',
            _ => '\0'
        };
        return value != '\0';
    }

    private static Dictionary<uint, char> BuildUsMap()
    {
        var map = new Dictionary<uint, char>();
        AddRow(map, "qwertyuiop", new uint[] { 0x51, 0x57, 0x45, 0x52, 0x54, 0x59, 0x55, 0x49, 0x4F, 0x50 });
        AddRow(map, "asdfghjkl", new uint[] { 0x41, 0x53, 0x44, 0x46, 0x47, 0x48, 0x4A, 0x4B, 0x4C });
        AddRow(map, "zxcvbnm", new uint[] { 0x5A, 0x58, 0x43, 0x56, 0x42, 0x4E, 0x4D });
        map[0xDB] = '['; map[0xDD] = ']'; map[0xBA] = ';'; map[0xDE] = '\'';
        map[0xBC] = ','; map[0xBE] = '.'; map[0xBF] = '/';
        return map;
    }

    private static void AddRow(Dictionary<uint, char> map, string row, IReadOnlyList<uint> virtualKeys)
    {
        for (var i = 0; i < row.Length; i++) map[virtualKeys[i]] = row[i];
    }
}
