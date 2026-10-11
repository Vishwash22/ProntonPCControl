# FocusLock

A small Windows parental-control app that locks the PC to **one app** (for example a Zoom class)
for a set time. While it is locked, the child can use that app normally but cannot switch to
another app, minimize it, or close it.

## Download

[`dist/FocusLock.exe`](dist/FocusLock.exe) is a ready-to-run build for 64-bit Windows 10/11. It needs
no installer and no .NET install. To update, close FocusLock and replace the file with the new one.
Your PIN and settings in `%AppData%\FocusLock` are kept.

## Using it

1. Open the app the child should use, for example by joining the Zoom meeting.
2. Start `FocusLock.exe`, set a parent PIN (first run only), and press **Refresh list**.
3. Pick the app's window, choose how many minutes (0 = until a parent unlocks), and choose what
   happens when the time is up: unlock, lock Windows (sign-in screen), or keep the screen blocked.
4. Optionally tick **Watch only** to also block all typing and clicking inside the app, so the
   child can only watch it (the mouse pointer still moves, which keeps Zoom's controls visible).
5. Press **Start lock**. The app is maximized and a small countdown appears at the top of the screen.

To unlock early, press **Ctrl + Alt + Shift + U** and enter the PIN. Three wrong PINs in a row
make the prompt wait 30 seconds, and the wait gets longer with each further round of three.

## What is blocked while locked

- Switching apps: Alt+Tab, Alt+Esc, the Windows key and every Win+ shortcut, Ctrl+Esc.
- Closing or minimizing: Alt+F4, Ctrl+F4, Ctrl+W, Ctrl+Q, Alt+Space, Alt+Q (Zoom's "leave meeting"),
  clicks on the window's Minimize and Close buttons and its title-bar menu.
- Clicks anywhere outside the app (desktop, notifications, other windows). The taskbar is hidden.
- In **Watch only** mode: every key, mouse click and scroll, except the parent unlock shortcut.
- Task Manager, and the Lock, Sign out and Change password options on the Ctrl+Alt+Del screen.

Every window of the chosen app stays usable, so Zoom's chat, dialogs and screen-share toolbar
still work. If the app minimizes or loses focus, it is brought back within a quarter of a second.
If the app is closed anyway (for example with Zoom's own **Leave** button), a full-screen
"ask a parent" screen covers everything until a parent unlocks or the time runs out.

Settings and the PIN hash are stored in `%AppData%\FocusLock`. All changes to Windows are undone
when the lock ends. If FocusLock crashes or the PC loses power, they are undone the next time it starts.

## Forgot the PIN?

There is no recovery code, but you can always get back in.

**No lock is running:**

1. Close FocusLock (if it stays open, it can save the old PIN again).
2. Delete `%AppData%\FocusLock\settings.json`. Paste `%AppData%\FocusLock` into File Explorer's
   address bar to get there.
3. Start FocusLock and set a new PIN. Your app, minutes and other choices are reset too.

**A lock is running:**

- If it has a timer and *When the time is up* is set to unlock or lock Windows, wait for it to end.
- Otherwise, restart the PC with the power button, or press Ctrl+Alt+Del and use the power icon in
  the bottom-right corner. The lock does not come back after a restart.
- After the restart, **open FocusLock once**. It switches Task Manager and the Lock and Sign out
  options back on, which stay off until you do. Then reset the PIN as above.

Do not delete the whole `%AppData%\FocusLock` folder after a forced restart: its `restore.json`
file records which Windows settings to put back. If it is already gone, delete these values with
Registry Editor to get Task Manager and the Lock and Sign out options back:

- `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Policies\System`:
  `DisableTaskMgr`, `DisableLockWorkstation`, `DisableChangePassword`
- `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer`: `NoLogoff`

Wrong PINs never lock you out for good: after every three wrong tries the prompt makes you wait,
starting at 30 seconds and growing each time.

## Limits

FocusLock runs as the normal Windows user and needs no admin rights. This keeps it simple, but it
is meant to keep a young child on task, not to stop a determined one:

- The power button and the Shut down option on the Ctrl+Alt+Del screen still work, and the lock
  does not resume after a restart.
- Buttons *inside* the app, such as Zoom's **Leave**, cannot be blocked. FocusLock shows its
  blocking screen when that happens.
- Touch-screen edge swipes are not blocked.
- Windows that run as administrator are not affected by the keyboard and mouse blocking.

For a stronger lock, give the child a standard (non-admin) Windows account. Windows' built-in
[Assigned Access (kiosk mode)](https://learn.microsoft.com/windows/configuration/assigned-access/)
can also limit an account to a single app permanently.

## Building

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Builds on Windows,
Linux or macOS, but runs only on Windows 10 or 11.

```sh
# Development build
dotnet build FocusLock

# One self-contained FocusLock.exe that runs without installing .NET
dotnet publish FocusLock -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish
```
