## Which file do I download?

| Your computer | Download | Or, without installing |
|---|---|---|
| **Windows** | `Heroesprofile.Uploader-win-Setup.exe` | `Heroesprofile.Uploader-win-Portable.zip` |
| **Windows, with rank reading** (beta, opt-in) | `Heroesprofile.Uploader-win-ranks-Setup.exe` | `Heroesprofile.Uploader-win-ranks-Portable.zip` |
| **Mac with Apple Silicon** (M1 and later) | `Heroesprofile.Uploader-osx-arm64-Setup.pkg` | `Heroesprofile.Uploader-osx-arm64-Portable.zip` |
| **Mac with Intel** | `Heroesprofile.Uploader-osx-x64-Setup.pkg` | `Heroesprofile.Uploader-osx-x64-Portable.zip` |
| **Linux** (desktop) | `Heroesprofile.Uploader.AppImage` | |
| **Linux** (plain program, or headless / systemd) | `HeroesProfileUploader-linux-x64.tar.gz` | |

Not sure which Mac you have? Apple menu → **About This Mac**: "Chip: Apple M…" is Apple Silicon,
"Processor: Intel…" is Intel.

Both Linux downloads run on any x86_64 distro. The tarball's program is the same desktop app as the
AppImage (run it with no arguments), just without updating itself: it shows an "Update available" link
instead. Everything else updates itself.

## What is "Windows, with rank reading"?

The same Windows uploader, plus an opt-in rank reader. The game doesn't put Storm League ranks in the
replay file, but it does show them on the loading screen, and we want to use that to get real rank
data onto Heroes Profile.

- It's off until you turn on **Read ranks from the loading screen** in Settings.
- When it's on, it captures the game window for the first ~15 seconds of the loading screen. Once the
  game reports the mode, Storm League games send the rank card strips from the loading screen to
  Heroes Profile. Every other mode is thrown away and never sent.
- It doesn't read ranks yet. For now we're collecting samples to build and test the reader. When we
  have enough, the site tells it to stop sending.
- Windows may draw a yellow outline around the game while it captures. On Windows 11 you can hide that
  in Settings; Windows 10 always shows it.
- It updates on its own channel, so a normal install is never moved onto it, and the normal build
  doesn't include any of the capture code.

If you play Storm League on Windows and are happy to help, this is the one to grab. Otherwise use the
normal Windows download.

**Everything else** (`.nupkg`, `releases.*.json`, `RELEASES`, `SHA256SUMS`,
`HeroesProfileUploaderSetup.exe`) is used by automatic updates and existing download links; you don't
need to download it.

## Code signing

The Windows builds are code-signed by Skill Tree Development LLC, the company behind Heroes Profile.
The Linux downloads are covered by `SHA256SUMS`, which is signed with our release key (`SHA256SUMS.asc`).

The Mac builds aren't signed yet, because we're still waiting on Apple. Until then macOS warns you the
first time you open one. The [install instructions](INSTALL_URL) explain how to get past that.
