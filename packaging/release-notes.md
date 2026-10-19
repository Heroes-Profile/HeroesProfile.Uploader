<!-- beta -->
## Testing this beta

This is a prerelease, for testers. Download it from the table below as usual.

**Getting future betas automatically:** open **Settings** in the uploader and press **Ctrl+Z**. That
shows a hidden **Allow beta updates** option; turn it on and the uploader updates to new betas as well
as full releases. Turn it off to go back to full releases only (you move back at the next full release).

**Seeing what the rank reader captures (Mac and Linux):** beta builds with rank reading keep a copy of
what each game's capture caught, on your computer only. Each game gets its own folder, named by date,
time and game mode, holding every captured frame's player-card strips (`frame-00-left.png`,
`frame-00-right.png`, ...) and a `read.txt` listing which frames showed the loading screen and the rank,
name and hero read off each card. Every game mode is kept, not just Storm League, so you can check it in
any game; the newest 20 games are kept. They go in a `HeroesProfileRankCaptures` folder:

- **Linux (AppImage):** in the same folder as the `.AppImage` file.
- **Mac:** in the same folder as `Heroes Profile Uploader.app`, usually `/Applications`.
- **If that folder can't be written to** (e.g. a Mac account that isn't an admin): in the temp folder
  instead, `/tmp/HeroesProfileRankCaptures` on Linux, or `$TMPDIR/HeroesProfileRankCaptures` on Mac
  (open it with `open "$TMPDIR/HeroesProfileRankCaptures"` in Terminal).

The uploader's log also says where it saved each game ("kept a copy of this game's capture in ...").
Windows betas don't keep these copies.
<!-- /beta -->

## Which file do I download?

| Your computer | Download | Or, without installing |
|---|---|---|
| **Windows** | `Heroesprofile.Uploader-win-Setup.exe` | `Heroesprofile.Uploader-win-Portable.zip` |
| **Windows, with rank reading** (beta, opt-in) | `Heroesprofile.Uploader-win-ranks-Setup.exe` | `Heroesprofile.Uploader-win-ranks-Portable.zip` |
| **Mac with Apple Silicon** (M1 and later) | `Heroesprofile.Uploader-osx-arm64-Setup.pkg` | `Heroesprofile.Uploader-osx-arm64-Portable.zip` |
| **Mac with Apple Silicon, with rank reading** (beta, opt-in) | the `.pkg` with `osx-arm64-ranks` in its name | the `-Portable.zip` with `osx-arm64-ranks` in its name |
| **Mac with Intel** | `Heroesprofile.Uploader-osx-x64-Setup.pkg` | `Heroesprofile.Uploader-osx-x64-Portable.zip` |
| **Mac with Intel, with rank reading** (beta, opt-in) | the `.pkg` with `osx-x64-ranks` in its name | the `-Portable.zip` with `osx-x64-ranks` in its name |
| **Linux** (desktop) | `Heroesprofile.Uploader.AppImage` | |
| **Linux, with rank reading** (beta, opt-in) | the `.AppImage` with `linux-ranks` in its name | |
| **Linux** (plain program, or headless / systemd) | `HeroesProfileUploader-linux-x64.tar.gz` | |

Not sure which Mac you have? Apple menu → **About This Mac**: "Chip: Apple M…" is Apple Silicon,
"Processor: Intel…" is Intel.

Both Linux downloads run on any x86_64 distro. The tarball's program is the same desktop app as the
AppImage (run it with no arguments), just without updating itself: it shows an "Update available" link
instead. Everything else updates itself.

## What is "with rank reading"?

The same uploader, plus an opt-in rank reader. The game doesn't put Storm League ranks in the replay
file, but it does show them on the loading screen, so the reader reads them from there and sends them
to Heroes Profile, where they become players' real ranks.

- It's off until you turn on **Read ranks from the loading screen** in Settings.
- When it's on, it captures the game window for the first ~15 seconds of the loading screen and reads
  each player's rank, name and hero from it. Every mode but Storm League is thrown away straight away.
- After a Storm League game, once its replay has uploaded, it sends each player's rank to Heroes Profile
  with that replay. While it's in beta it also sends a few frames of the loading screen, to keep
  checking the reader; the site can switch that off at any time.
- It only looks at the game window, only during the loading screen, and never reads or changes the
  game itself. Each one updates on its own channel, so a normal install is never moved onto it, and the
  normal builds don't include any of the capture code.

What each system needs:

- **Windows:** Windows 10 (2004) or later. Windows may draw a yellow outline around the game while it
  captures; on Windows 11 you can hide that in Settings. Windows 10 always shows it. Names are read in
  every language Windows has installed.
- **Mac:** macOS 14 (Sonoma) or later. The first time it captures, macOS asks whether the uploader may
  record the screen: allow it in **System Settings → Privacy & Security → Screen Recording**, then
  restart the uploader.
- **Linux:** the game under Wine or Proton, on X11 or a Wayland desktop with XWayland (the usual setup).
  Player names are read with **Tesseract**, which now comes with the app (English letters), so there's
  nothing to install. For names in other alphabets, install your distro's `tesseract-ocr` plus language
  packs (e.g. `tesseract-ocr-kor`, `tesseract-ocr-rus`, `tesseract-ocr-chi-sim`); the app uses that one when
  it has more languages.

If you play Storm League and are happy to help, grab the "with rank reading" download for your system.
Otherwise use the normal one.

**Everything else** (`.nupkg`, `releases.*.json`, `RELEASES`, `SHA256SUMS`,
`HeroesProfileUploaderSetup.exe`) is used by automatic updates and existing download links; you don't
need to download it.

## Code signing

The Windows builds are code-signed by Skill Tree Development LLC, the company behind Heroes Profile.
The Linux downloads are covered by `SHA256SUMS`, which is signed with our release key (`SHA256SUMS.asc`).

The Mac builds aren't signed yet, because we're still waiting on Apple. Until then macOS warns you the
first time you open one. The [install instructions](INSTALL_URL) explain how to get past that.
