TypeAllBack 0.1.4 — Chrome freeze hotfix

- Fixed the reported Chrome freeze when opening a GitHub profile with TypeAllBack running, without clicking or typing. The reporter confirmed that the fix resolved the problem.
- Replaced desktop-wide UI Automation focus subscriptions with native Windows focus notifications.
- Chromium windows are no longer probed through UI Automation before keyboard input; confirmed editable fields continue to refresh.
- Ordinary Chromium web documents are rejected before whole-document text attributes are queried. The protection applies across websites, without a GitHub-specific exception.

603 regression checks passed. Windows 10/11 x64; .NET is included.

Update through the tray menu: “Проверить обновления сейчас”. The application downloads and verifies the installer, closes, installs the update and restarts. Settings are retained. If using the original 0.1.0 build, install the MSI once to enable the built-in updater.
