## Which file do I download?

| Your computer | Download | Or, without installing |
|---|---|---|
| **Windows** | `Heroesprofile.Uploader-win-Setup.exe` | `Heroesprofile.Uploader-win-Portable.zip` |
| **Mac with Apple Silicon** (M1 and later) | `Heroesprofile.Uploader-osx-arm64-Setup.pkg` | `Heroesprofile.Uploader-osx-arm64-Portable.zip` |
| **Mac with Intel** | `Heroesprofile.Uploader-osx-x64-Setup.pkg` | `Heroesprofile.Uploader-osx-x64-Portable.zip` |
| **Linux** (desktop) | `Heroesprofile.Uploader.AppImage` | |
| **Linux** (headless / systemd) | `HeroesProfileUploader-linux-x64.tar.gz` | |

Not sure which Mac you have? Apple menu → **About This Mac**: "Chip: Apple M…" is Apple Silicon,
"Processor: Intel…" is Intel.

Everything updates itself except the Linux tarball, which only logs when a newer release is out.

**Everything else** (`.nupkg`, `releases.*.json`, `RELEASES`, `SHA256SUMS`,
`HeroesProfileUploaderSetup.exe`) is used by automatic updates and existing download links; you don't
need to download it.

These builds aren't code-signed yet, so your OS warns you the first time you open one.
[Install instructions](INSTALL_URL) explain how to get past that on each OS.
