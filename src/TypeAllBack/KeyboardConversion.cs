namespace LayoutPreviewPrototype;
internal enum KeyboardLayout { English, Russian }
internal readonly record struct PhysicalKey(uint VirtualKey, bool Shift, bool CapsLock);
internal static class KeyboardConversion
{
    private const string Letters = "QWERTYUIOPASDFGHJKLZXCVBNM";
    private const string Russian = "йцукенгшщзфывапролдячсмить";
    public static bool TryConvert(PhysicalKey key, KeyboardLayout layout, out char value)
    {
        int index = Letters.IndexOf((char)key.VirtualKey);
        if (index >= 0)
        {
            value = layout == KeyboardLayout.Russian ? Russian[index] : char.ToLowerInvariant(Letters[index]);
            if (key.Shift ^ key.CapsLock) value = char.ToUpperInvariant(value);
            return true;
        }
        string pair = key.VirtualKey switch
        {
            >= 0x30 and <= 0x39 => layout == KeyboardLayout.English
                ? $"{(char)key.VirtualKey}{")!@#$%^&*("[(int)key.VirtualKey - 0x30]}"
                : $"{(char)key.VirtualKey}{")!\"№;%:?*("[(int)key.VirtualKey - 0x30]}",
            0x20 => "  ",
            0xBA => layout == KeyboardLayout.Russian ? "жЖ" : ";:",
            0xBB => "=+",
            0xBC => layout == KeyboardLayout.Russian ? "бБ" : ",<",
            0xBD => "-_",
            0xBE => layout == KeyboardLayout.Russian ? "юЮ" : ".>",
            0xBF => layout == KeyboardLayout.Russian ? ".," : "/?",
            0xC0 => layout == KeyboardLayout.Russian ? "ёЁ" : "`~",
            0xDB => layout == KeyboardLayout.Russian ? "хХ" : "[{",
            0xDC => layout == KeyboardLayout.Russian ? "\\/" : "\\|",
            0xDD => layout == KeyboardLayout.Russian ? "ъЪ" : "]}",
            0xDE => layout == KeyboardLayout.Russian ? "эЭ" : "'\"",
            _ => ""
        };
        if (pair.Length == 0) { value = default; return false; }
        bool upper = char.IsLetter(pair[0]) ? key.Shift ^ key.CapsLock : key.Shift;
        value = pair[upper ? 1 : 0]; return true;
    }
}