# Installing the Heroes Profile Uploader

The uploader is one app for Windows, macOS and Linux. Download it from the
[latest release](https://github.com/Heroes-Profile/HeroesProfile.Uploader/releases/latest).

The Windows builds are code-signed by Skill Tree Development LLC, and the Linux downloads come with a
signed `SHA256SUMS`. The Mac builds aren't signed yet while we wait on Apple, so macOS warns you the
first time you open one; the macOS steps below say how to get past that.

Installed copies update themselves: when an update is ready you'll see a banner with **Restart now**.

## Windows

1. Download **`Heroesprofile.Uploader-win-Setup.exe`** from the release and run it.
2. It installs for your user only (no admin needed), adds Start menu and desktop shortcuts, and starts.

**Want to help with rank reading?** `Heroesprofile.Uploader-win-ranks-Setup.exe` is the same app with
an opt-in rank reader for Storm League loading screens. The release notes explain what it does. Turn it
on in **Settings → Read ranks from the loading screen**.

**Coming from the old uploader?** Install the new one first, then let it remove the old one.
- **Your settings carry over:** the first time the new app starts, it copies them from the old one,
  including your Twitch key.
- **Nothing is uploaded twice:** it also copies the old app's record of what's already been uploaded.
- **The old one can go:** the new app then offers to uninstall the old one. You can also do it later
  from **Settings → Apps** ("Heroesprofile"); the new app keeps its own copies either way.
- **Don't run both at once:** they'd both upload every game. The new one warns you if the old one is
  still running.

To uninstall, use **Settings → Apps** in Windows. Your settings and upload history are kept.

## macOS

1. Download the macOS installer for your Mac:
   - **Apple Silicon** (M1 and later): the file with **`osx-arm64`** in its name.
   - **Intel**: the file with **`osx-x64`** in its name.
2. Because it isn't signed yet (we're waiting on Apple), double-clicking shows "can't be opened". Instead:
   - right-click it and choose **Open**, then **Open** again; or
   - open **System Settings → Privacy & Security** and click **Open Anyway**.
3. Heroes of the Storm's replays are found automatically
   (`~/Library/Application Support/Blizzard/Heroes of the Storm/Accounts`).

**Minimize to menu bar** keeps it running as an icon in the menu bar. **Start on login** starts it
minimized when you log in.

**Want to help with rank reading?** Download the installer with **`-ranks`** in its name for your Mac
(`osx-arm64-ranks` or `osx-x64-ranks`). It needs macOS 14 (Sonoma) or later. Turn it on in **Settings →
Read ranks from the loading screen**; the first time a game's loading screen shows, macOS asks whether
the uploader may record the screen. Allow it in **System Settings → Privacy & Security → Screen
Recording**, then restart the uploader.

## Linux

1. Download the **`.AppImage`** from the release.
2. Make it executable and run it:
   - in your file manager: right-click → Properties → Permissions → "Allow executing file as program",
     then double-click it;
   - or in a terminal: `chmod +x Heroesprofile.Uploader*.AppImage && ./Heroesprofile.Uploader*.AppImage`.
3. Heroes of the Storm runs under Wine/Proton, so the first time it asks for your replays: click
   **Open Settings**, then **Browse...** and pick your Wine/Proton prefix (the folder containing
   `drive_c`). See [Where is my prefix?](linux/README.md#where-is-my-prefix)
4. Tick **Show in app menu** to add it to your app launcher.

**Want to help with rank reading?** Download the `.AppImage` with **`linux-ranks`** in its name instead,
and turn it on in **Settings → Read ranks from the loading screen**. It reads the game's window under
Wine or Proton (X11, or XWayland on a Wayland desktop). To read player names it needs Tesseract:

```
sudo apt install tesseract-ocr        # Debian, Ubuntu, Mint
sudo dnf install tesseract            # Fedora
sudo pacman -S tesseract tesseract-data-eng   # Arch
```

Add a language pack for names in other alphabets, e.g. `tesseract-ocr-kor`, `tesseract-ocr-rus`,
`tesseract-ocr-chi-sim` (Debian/Ubuntu names). Without Tesseract, ranks are still read, but cards can only
be matched to players by their heroes.

**Prefer a plain program?** `HeroesProfileUploader-linux-x64.tar.gz` holds the same app as a single
executable: run it with no arguments for the normal window, on any x86_64 distro. It's also the one to
use with no desktop, as a background service; see [the Linux guide](linux/README.md). It doesn't update
itself: when a new version is out it shows an "Update available" link (the service logs it instead).

## Where things are kept

| | Windows | macOS | Linux |
|---|---|---|---|
| Settings (`config.json`) | `%APPDATA%\HeroesProfileUploader` | `~/Library/Application Support/Heroesprofile` | `~/.config/heroesprofile` |
| Upload history and logs | `%APPDATA%\HeroesProfileUploader` | `~/Library/Application Support/Heroesprofile` | `~/.local/share/heroesprofile` |
| Twitch key | encrypted for your Windows user (DPAPI) | your login Keychain | `config.json`, readable only by you |

**Show log** in the app opens the logs folder. Please include the log when you report a problem.
