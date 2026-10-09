using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace LayoutPreviewPrototype;

internal static class UiaSmokeTests
{
    public static void Run()
    {
        Exception? failure = null;
        int checks = 0;
        var thread = new Thread(() =>
        {
            var application = new Application();
            const string prefix = "Старый 👩‍💻 текст. ";
            const string suffix = " КОНЕЦ";
            var field = new TextBox { Text = prefix + suffix, FontSize = 20, Margin = new Thickness(16) };
            var second = new TextBox { Text = "second field", Margin = new Thickness(16) };
            var panel = new StackPanel(); panel.Children.Add(field); panel.Children.Add(second);
            var window = new Window { Title = "LayoutPreview UIA regression fixture", Width = 620, Height = 200, Content = panel };
            void Check(bool condition, string name)
            { checks++; if (!condition) throw new InvalidOperationException(name); }
            window.Loaded += async (_, _) =>
            {
                try
                {
                    await Task.Delay(150);
                    window.Activate();
                    field.Focus(); field.CaretIndex = prefix.Length;
                    TextEditor.Start();
                    await Task.Delay(700);
                    Check(Native.GetForegroundWindow() == new WindowInteropHelper(window).Handle,
                        "Test fixture did not acquire foreground focus; runtime checks blocked before input");
                    Check(TextEditor.TryRead(out var initial), "UIA snapshot unavailable: " + await Task.Run(UiaTextEditor.DescribeFocusedElement));
                    Check(initial.AutomationIdentity.Length > 0, "Fixture must use the UIA path");
                    Check(initial.Text == field.Text && initial.SelectionEnd == prefix.Length, "UIA text/caret mismatch");
                    var session = new InputSession(initial.Text, initial.SelectionEnd, KeyboardLayout.English);
                    IntPtr english = new(0x04090409), russian = new(0x04190419);
                    Check(await TextEditor.TrySwitchLayoutAsync(initial, session, english), "English layout switch failed");
                    foreach (char c in "Ghbdtn")
                    {
                        var key = new PhysicalKey(char.ToUpperInvariant(c), char.IsUpper(c), false);
                        int caretIndex = field.CaretIndex;
                        session.Append(key, KeyboardLayout.English, field.Text);
                        field.Text = field.Text.Insert(caretIndex, c.ToString());
                        field.CaretIndex = caretIndex + 1;
                    }
                    await Task.Delay(200);
                    Check(TextEditor.TryRead(out var typed) && session.Matches(typed.Text, typed.SelectionStart, typed.SelectionEnd), "Typed session not reconciled");
                    Check(TextEditor.TryGetCaret(typed, out var caret) && caret.Bottom > caret.Top, "UIA caret rectangle unavailable");
                    Check(await TextEditor.TrySwitchLayoutAsync(typed, session, russian), "Russian layout switch failed");
                    Check(await TextEditor.TryReplaceAsync(typed, session, russian, () => true), "UIA checked range replacement failed");
                    Check(field.Text == prefix + "Привет" + suffix, "Replacement changed emoji prefix/suffix or failed");
                    Check(field.CaretIndex == prefix.Length + "Привет".Length, "Replacement caret not restored");
                    Check(TextEditor.TryRead(out var replaced) && replaced.Layout == russian, "Actual Windows layout not switched");
                    second.Focus(); second.CaretIndex = second.Text.Length;
                    await Task.Delay(200);
                    Check(TextEditor.TryRead(out var other) && !TextEditor.SameEditor(replaced, other), "Same-window input identity not distinguished");
                    string unchanged = second.Text;
                    Check(!await TextEditor.TryReplaceAsync(typed, session, russian, () => true), "Stale session must not edit a different input");
                    Check(second.Text == unchanged, "Other field changed by rejected replacement");
                    Check(!await TextEditor.TryReplaceAsync(other, new InputSession(other.Text, other.SelectionEnd, KeyboardLayout.English), other.Layout, () => false), "Cancelled replacement must be rejected");
                    second.IsReadOnly = true;
                    await Task.Delay(200);
                    Check(!TextEditor.TryRead(out _), "Read-only field must be excluded");
                }
                catch (Exception e) { failure = e; }
                finally { TextEditor.Stop(); window.Close(); application.Shutdown(); }
            };
            application.Run(window);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(15))) throw new TimeoutException("UIA smoke test timed out");
        if (failure != null) throw new InvalidOperationException("UIA smoke test failed", failure);
        Console.WriteLine($"PASS: {checks} UIA runtime checks in an isolated WPF field");
    }
}
