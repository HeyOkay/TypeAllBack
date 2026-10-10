using System.Security.Cryptography;
using System.Text;

namespace LayoutPreviewPrototype;

/// <summary>
/// Keeps the accepted layout's following text out of a fresh preview session until the
/// user deliberately moves the caret, edits backwards, starts a new line, or changes layout.
/// </summary>
internal sealed class AcceptedInputGuard
{
    private string? _documentFingerprint;
    private int _caret;
    private KeyboardLayout _layout;
    private string _typedAfterAccept = "";

    public bool IsArmed => _documentFingerprint is not null;

    public void Arm(string document, int caret, KeyboardLayout layout)
    {
        _documentFingerprint = Fingerprint(document);
        _caret = caret;
        _layout = layout;
        _typedAfterAccept = "";
    }

    public bool ShouldSuppress(string document, int selectionStart, int selectionEnd,
        KeyboardLayout currentLayout, PhysicalKey key, bool explicitBoundary)
    {
        if (_documentFingerprint is null) return false;

        if (explicitBoundary || currentLayout != _layout ||
            Fingerprint(document) != _documentFingerprint)
        {
            Disarm();
            return false;
        }

        // Backspace edits the accepted text normally, but must not immediately start a
        // fresh preview session on the next character. Predict the post-key document so
        // the guard remains aligned with the native editor after it processes the key.
        if (key.VirtualKey == 0x08)
        {
            if (selectionStart != selectionEnd)
            {
                _documentFingerprint = Fingerprint(document.Remove(selectionStart, selectionEnd - selectionStart));
                _caret = selectionStart;
                _typedAfterAccept = "";
                return true;
            }

            if (selectionEnd == 0) { Disarm(); return false; }

            _documentFingerprint = Fingerprint(document.Remove(selectionEnd - 1, 1));
            _caret = selectionEnd - 1;
            if (_typedAfterAccept.Length > 0)
                _typedAfterAccept = _typedAfterAccept[..^1];
            return true;
        }

        // Other non-printable keys start a new editing context.
        if (!KeyboardConversion.TryConvert(key, currentLayout, out var typed))
        {
            Disarm();
            return false;
        }

        string updated = document.Remove(selectionStart, selectionEnd - selectionStart)
            .Insert(selectionStart, typed.ToString());
        _documentFingerprint = Fingerprint(updated);
        _caret = selectionStart + typed.ToString().Length;
        _typedAfterAccept += typed;
        return true;
    }

    public void Disarm()
    {
        _documentFingerprint = null;
        _typedAfterAccept = "";
    }

    private static string Fingerprint(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(text)));
}
