# Hotsapi.Uploader [![Build status](https://ci.appveyor.com/api/projects/status/0tg5u1yev3l8p2qv/branch/master?svg=true)](https://ci.appveyor.com/project/poma/hotsapi-uploader/branch/master) [![Join Discord Chat](https://img.shields.io/discord/650747275886198815?label=Discord&logo=discord)](https://discord.gg/cADfdFP)

Uploads Heroes of the Storm replays to [hotsapi.net](https://hotsapi.net) ([repo link](https://github.com/poma/hotsapi))

![Screenshot](https://hotsapi.net/img/uploader.png)

# Installation

* Requires .NET Framework 4.6.2 or higher
* [__Download__](https://github.com/Poma/Hotsapi.Uploader/releases/latest) **"HotsApiUploaderSetup.exe"** from [Releases](https://github.com/Poma/Hotsapi.Uploader/releases/latest) page (you don't need to download other files listed there) and run it

*Note:* sometimes the installer is mistakenly marked as a virus by some AV vendors heuristics because they don't like things that install something on your PC in general. If you don't trust the installer you can download a portable "HotsApi.zip" and use it instead. In that case you are losing auto updates, start with windows, and shortcuts. Also you'll need to make sure that [.NET 4.6.2](https://www.microsoft.com/en-us/download/details.aspx?id=53344) is installed on your machine.

# Contributing

Coding conventions are as usual for C# except braces, those are in egyptian style ([OTBS](https://en.wikipedia.org/wiki/Indent_style#1TBS)). For repos included as submodules their coding style is used.

All logic is contained in `Hotsapi.Uploader.Common` to make UI project as thin as possible. `Hotsapi.Uploader.Windows` is responsible for only OS-specific tasks such as auto update, tray icon, autorun, file locations.

For the current to do list look in the [Project](https://github.com/poma/Hotsapi.Uploader/projects/1) page
