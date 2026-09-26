# Heroesprofile.Uploader [![Join Discord Chat](https://img.shields.io/discord/650747275886198815?label=Discord&logo=discord)](https://discord.gg/cADfdFP)[![GitHub Release Downloads](https://img.shields.io/github/downloads/Heroes-Profile/HeroesProfile.Uploader/latest/total.svg)]()



Uploads Heroes of the Storm replays to [heroesprofile.com](https://www.heroesprofile.com/) ([repo link](https://github.com/Heroes-Profile/HeroesProfile.Uploader))

# Installation

One app for **Windows, macOS and Linux**. Download it from the
[Releases](https://github.com/Heroes-Profile/HeroesProfile.Uploader/releases/latest) page:

| | Download |
|---|---|
| Windows | `Heroesprofile.Uploader-win-Setup.exe` |
| macOS | the `.pkg` for your Mac: `osx-arm64` for Apple Silicon (M1 and later), `osx-x64` for Intel |
| Linux | the `.AppImage`; or `HeroesProfileUploader-linux-x64.tar.gz` for a headless/systemd setup |

Nothing else needs installing (no .NET), and it updates itself. See
[packaging/INSTALL.md](packaging/INSTALL.md) for step-by-step instructions, including opening the app
while builds aren't code-signed yet. On Linux, Heroes of the Storm runs under Wine/Proton and you point
the uploader at that prefix; see [packaging/linux/README.md](packaging/linux/README.md).

**Coming from the old Windows uploader?** Install the new one and quit the old one. On its first start
the new app copies your settings over (including your Twitch key) and knows what's already been
uploaded, so nothing is uploaded twice.

# Contributing

Coding conventions are as usual for C# except braces, those are in egyptian style ([OTBS](https://en.wikipedia.org/wiki/Indent_style#1TBS)). For repos included as submodules their coding style is used.

All upload logic is in `Heroesprofile.Uploader.Common`, keeping the app project thin. `Heroesprofile.Uploader.Desktop` is the app itself: an Avalonia GUI plus a headless CLI (`run`, `scan --dry-run`), with everything that differs per OS behind `Platform/IPlatform` and updates handled by Velopack (`Updates/AppUpdater`). Open `Heroesprofile.Uploader.slnx` to work on it.

Needs the .NET 10 SDK (`global.json` pins it). `dotnet build Heroesprofile.Uploader.slnx` and `dotnet test --project Heroesprofile.Uploader.Tests` build and test everything; CI does the same on Windows, Linux and macOS for every pull request. Package versions all live in `Directory.Packages.props`, and the app's version in `Directory.Build.props` (`HeroesProfileVersion`). Releases are built by the manual **Release (Desktop)** workflow (`.github/workflows/release-desktop.yml`).

To try a build without touching your real setup, set `HEROESPROFILE_UPLOADER_HOME` to a scratch folder (settings, upload history and logs go there instead), and `HEROESPROFILE_UPLOADER_UPDATE_SOURCE` to a folder of `vpk pack` output to test updates from it instead of GitHub.
