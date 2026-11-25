# JL for Linux

A native Linux front end for [JL](https://github.com/rampaa/JL). It reuses JL's
platform-neutral engine (`JL.Core`: dictionaries, deconjugation, frequency lists,
Anki mining, audio sources, WebSocket capture, stats) unchanged, and replaces the
Windows-only WPF interface (`JL.Windows`) with one written in
[Avalonia](https://avaloniaui.net/), a cross-platform UI toolkit.

## Build and install

You need the .NET 9 SDK (or newer):

| Distro | Command |
|---|---|
| Debian / Ubuntu | `sudo apt install dotnet-sdk-9.0` |
| Fedora | `sudo dnf install dotnet-sdk-9.0` |
| Arch | `sudo pacman -S dotnet-sdk` |

Then, from the repository root:

```sh
./JL.Linux/build.sh --install
```

This builds JL, copies it to `~/.local/share/JL` and adds it to your application menu.
Without `--install`, the build lands in `out/JL-linux/` and you run `out/JL-linux/JL`.
`SELF_CONTAINED=1 ./JL.Linux/build.sh` bundles the .NET runtime so the build runs
on machines without .NET installed.

On first start JL offers to download JMdict, JMnedict and KANJIDIC, as it does on Windows.

### Optional helper programs

JL calls a few standard tools when they are installed:

| For | Install |
|---|---|
| Word audio | `mpv` (recommended) or `ffmpeg` (for `ffplay`) |
| Clipboard capture on Wayland | `wl-clipboard` |
| Screenshot / clipboard-image fields in Anki cards | Wayland: `grim` (Sway, Hyprland), or `spectacle` / `gnome-screenshot`. X11: `maim`, `imagemagick` or `scrot`. Clipboard images on X11: `xclip` |
| Japanese font | `fonts-noto-cjk` / `noto-fonts-cjk` |

## Using it

- **Capture:** JL picks up copied Japanese text (clipboard capture is on by default),
  or text from a WebSocket (Textractor's WebSocket extension, LunaTranslator, etc.).
  Turn WebSocket capture on in Preferences.
- **Look up:** hover over a word. Change this to hold Shift/Ctrl/Alt or to left-click
  in *Preferences → Lookup*.
- **Mining mode:** click the text, middle-click, or move the mouse into the popup.
  The popup then stays put, shows every result, and you can hover words in its
  definitions for nested popups. Leaving the popup, clicking the text again, or Esc closes it.
- **Mine:** the *＋ Mine* button on a result (or `M` / `1`–`9` while the main window has focus).
  Set up decks and fields in *right-click → Anki setup…*, then turn on
  *Enable Anki mining* in Preferences. You need Anki with the AnkiConnect add-on running.
- **Dictionaries:** *right-click → Dictionaries…* to enable, disable, reorder, add
  (Yomitan/Yomichan or Nazeka format) and remove dictionaries. Unzip Yomitan
  dictionaries into a folder first.
- **Backlog:** Up/Down or PageUp/PageDown.

| Key (main window focused) | Action |
|---|---|
| Esc | Close popups |
| Up / Down, PageUp / PageDown | Previous / next text in the backlog |
| Ctrl+V | Paste text manually |
| Ctrl+P / Ctrl+D | Preferences / Dictionaries |
| Ctrl+T | Toggle always-on-top |
| M, 1–9 | Mine the first / nth result in the open popup |
| A | Play audio for the first result |

## Bringing over a Windows setup

Copy `Config/`, `Profiles/` and the dictionary folders from your Windows JL folder
into the Linux JL folder (`~/.local/share/JL` after `--install`). The dictionary list,
Anki setup, frequency lists, audio sources and the settings both versions share
carry over. Dictionary paths inside the JL folder are stored as relative paths, so
they keep working if you keep the same layout. Paths elsewhere on your Windows drive
won't exist on Linux: fix those in the Dictionaries window.

## Wayland notes

Avalonia runs through XWayland, which is installed by default on GNOME, KDE and
wlroots desktops. Everything works there, with two caveats:

- **Clipboard:** with `wl-clipboard` installed, JL is notified of every copy on KDE,
  Sway, Hyprland and other compositors that support the data-control protocol.
  GNOME doesn't support it, so JL falls back to polling the clipboard, and on Wayland
  that can miss text copied from native Wayland apps while JL isn't focused.
  On GNOME, WebSocket capture is the most reliable option. Text copied from games
  running in Wine/Proton (which are X11 clients) is caught either way.
- **Always on top** over fullscreen games depends on the compositor.

## What's different from Windows JL

This port covers JL's core workflow. Not ported yet:

- Global hotkeys (shortcuts work while the main window is focused)
- Text-to-speech audio (Windows uses its built-in voices; other audio sources work)
- Pitch-accent graphs (the downstep number is shown instead, e.g. はし[2])
- Duplicate-card highlighting, the custom word/name dictionary editors, the profile manager,
  frequency dictionary management, the per-dictionary options editor, vertical text, Magpie
  integration, and JL's self-updater (rebuild with `git pull && ./JL.Linux/build.sh --install`)

Settings for these still live in `Config/`, so a copied Windows config keeps them.

## Layout

```
JL.Linux/
  Program.cs, App.cs        entry point, single-instance guard, theme
  LinuxFrontend.cs          JL.Core's IFrontend contract for Linux
  Config/UiConfig.cs        front-end settings (stored in Config/Configs.sqlite)
  GUI/MainWindow.cs         text window, capture, backlog, menu, shortcuts
  GUI/PopupWindow.cs        dictionary popup, mining mode, nested popups
  GUI/LookupEngine.cs       pointer → character → JL.Core lookup
  GUI/*Window.cs            preferences, dictionaries, Anki setup, stats, dialogs
  Services/                 clipboard watcher, audio player, screenshots, shell helpers
  Images/                   image-size reader (shared with JL.Windows)
```
