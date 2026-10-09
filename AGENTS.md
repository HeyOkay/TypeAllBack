# LayoutPreview project rules

- This repository is the single source of truth. Do not create versioned project copies.
- The product is a local-only Windows 10/11 WPF utility targeting .NET 10 x64 with PerMonitorV2 DPI.
- The initial supported editor is Windows Notepad.
- Keyboard conversion is deterministic physical EN-US <-> RU mapping. Source punctuation must be converted before any token-boundary decision; whitespace is the generic boundary.
- Preview updates after every key and backspace, never steals focus, never inserts document text, and must track the real caret using the correct Win32 physical-pixel/WPF-DIP coordinate conversion.
- Accept is only Right Arrow when a valid preview is visible or clicking the preview. Space never accepts. Right Arrow remains native when no valid preview exists.
- Accept must replace only the current active layout section of the input session, preserve previous sections and preexisting text, switch the actual Windows keyboard layout, restore the caret, hide the preview, and fail closed when text/caret state is uncertain.
- Preserve native shortcuts, navigation, password/secure contexts, IME composition, and unsupported editors as the architecture expands.
- After every source change run `dotnet build .\\LayoutPreviewPrototype.csproj -c Debug --nologo`; fix compile errors before continuing. If the environment blocks the build, record the concrete blocker.
- Keep meaningful Git checkpoints and never commit build artifacts.
- Runtime visual behavior must be tested with Computer Use when the Windows automation service is available; do not claim a visual test without observing it.
