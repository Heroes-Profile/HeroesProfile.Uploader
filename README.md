# Heroesprofile.Uploader [![Join Discord Chat](https://img.shields.io/discord/650747275886198815?label=Discord&logo=discord)](https://discord.gg/cADfdFP)[![GitHub Release Downloads](https://img.shields.io/github/downloads/Heroes-Profile/HeroesProfile.Uploader/latest/total.svg)]()



Uploads Heroes of the Storm replays to [heroesprofile.com](https://www.heroesprofile.com/) ([repo link](https://github.com/Heroes-Profile/HeroesProfile.Uploader))

# Installation

* Requires .NET Framework 4.6.2 or higher
* [__Download__](https://github.com/Heroes-Profile/HeroesProfile.Uploader/releases/latest) **"HeroesProfileUploaderSetup.exe"** from [Releases](https://github.com/Heroes-Profile/HeroesProfile.Uploader/releases/latest) page (you don't need to download other files listed there) and run it

*Note:* sometimes the installer is mistakenly marked as a virus by some AV vendors heuristics because they don't like things that install something on your PC in general. If you don't trust the installer you can download a portable "Heroesprofile.zip" and use it instead. In that case you are losing auto updates, start with windows, and shortcuts. Also you'll need to make sure that [.NET 4.6.2](https://www.microsoft.com/en-us/download/details.aspx?id=53344) is installed on your machine.

## Linux

There is a native Linux build: download **"HeroesProfileUploader-linux-x86_64.AppImage"** from the same [Releases](https://github.com/Heroes-Profile/HeroesProfile.Uploader/releases/latest) page, make it executable and run it (a plain `.tar.gz` is there too). It is self-contained and needs no .NET or other libraries installed. Heroes of the Storm keeps running under Wine/Proton; you point the uploader at that prefix and it finds your replays. See [packaging/linux/README.md](packaging/linux/README.md) for setup, start on login, and headless/systemd use.

# Contributing

Coding conventions are as usual for C# except braces, those are in egyptian style ([OTBS](https://en.wikipedia.org/wiki/Indent_style#1TBS)). For repos included as submodules their coding style is used.

All logic is contained in `Heroesprofile.Uploader.Common` to make UI project as thin as possible. `Heroesprofile.Uploader.Windows` is responsible for only OS-specific tasks such as auto update, tray icon, autorun, file locations. `Heroesprofile.Uploader.Linux` does the same for Linux (Avalonia GUI plus a headless CLI).
