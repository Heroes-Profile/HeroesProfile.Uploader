# Installing the Heroes Profile Uploader (new app, beta)

This is the new version of the uploader, one app for Windows, macOS and Linux. It's in **beta**: builds
are published as **pre-releases** named like `v3.0.0-beta.2` on the
[Releases](https://github.com/Heroes-Profile/HeroesProfile.Uploader/releases) page, and they aren't
code-signed yet. So your OS will warn you the first time you open one; the steps below say how to get
past that.

It looks and works like the current Windows uploader. Installed copies update themselves: when an
update is ready you'll see a banner with **Restart now**.

## Windows

1. Download **`Heroesprofile.Uploader-win-Setup.exe`** from the release and run it.
2. If you see "Windows protected your PC", click **More info**, then **Run anyway**.
3. It installs for your user only (no admin needed), adds Start menu and desktop shortcuts, and starts.

**Coming from the current uploader?**
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
2. Because it isn't signed yet, double-clicking shows "can't be opened". Instead:
   - right-click it and choose **Open**, then **Open** again; or
   - open **System Settings → Privacy & Security** and click **Open Anyway**.
3. Heroes of the Storm's replays are found automatically
   (`~/Library/Application Support/Blizzard/Heroes of the Storm/Accounts`).

**Minimize to menu bar** keeps it running as an icon in the menu bar. **Start on login** starts it
minimized when you log in.

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

**No desktop, or running it as a background service?** Use
`HeroesProfileUploader-linux-x64.tar.gz` instead; see [the Linux guide](linux/README.md). That version
doesn't update itself.

## Where things are kept

| | Windows | macOS | Linux |
|---|---|---|---|
| Settings (`config.json`) | `%APPDATA%\HeroesProfileUploader` | `~/Library/Application Support/Heroesprofile` | `~/.config/heroesprofile` |
| Upload history and logs | `%APPDATA%\HeroesProfileUploader` | `~/Library/Application Support/Heroesprofile` | `~/.local/share/heroesprofile` |
| Twitch key | encrypted for your Windows user (DPAPI) | your login Keychain | `config.json`, readable only by you |

**Show log** in the app opens the logs folder. Please include the log when you report a problem.
