# Heroes Profile Uploader for Linux

Uploads your Heroes of the Storm replays to [Heroes Profile](https://www.heroesprofile.com) from Linux.
HotS keeps running under Wine/Proton (Lutris, Steam, Bottles, plain Wine); the uploader runs natively
and reads the replays from that Wine prefix. It uses the same upload code as the Windows app.

## Quick start

1. Download `HeroesProfileUploader-linux-x86_64.AppImage` from the
   [latest release](https://github.com/Heroes-Profile/HeroesProfile.Uploader/releases/latest).
   Nothing else to install: no .NET, no extra libraries. Works on any current x86_64 distro.
2. Make it executable and start it. Either right-click → Properties → Permissions → "Allow executing
   file as program" and double-click it, or:

   ```sh
   chmod +x HeroesProfileUploader-linux-x86_64.AppImage
   ./HeroesProfileUploader-linux-x86_64.AppImage
   ```

3. It asks for your Wine/Proton prefix. Pick the folder that contains `drive_c`.
4. Tick **Show in app menu**. This copies the app to `~/.local/bin/heroesprofile-uploader` and adds
   it to your app menu, so you can delete the downloaded file.

That's it. It uploads any replays you haven't uploaded yet, then uploads each new game as soon as it
finishes, for as long as the uploader is running.

### Where is my prefix?

| Launcher | Prefix |
|---|---|
| Lutris | whatever you chose at install, e.g. `~/Games/battlenet` |
| Steam (non-Steam game via Proton) | `~/.steam/steam/steamapps/compatdata/<appid>` |
| Bottles | `~/.local/share/bottles/bottles/<name>` |
| Plain Wine | `~/.wine` |

A Steam `compatdata/<appid>` folder or the HotS `Accounts` folder itself also work.

## Two ways to run it

Pick **one**. Running both at the same time makes them compete over the same replays.

| | Desktop app | Background service |
|---|---|---|
| Uploads new games automatically | yes | yes |
| Window, stats, tray icon | yes | no |
| Starts on login | optional toggle | yes |
| Updates itself | **yes** | **no**: you update it by hand |

### 1. Desktop app (most people)

What you get from the quick start: a window with your replay list and upload stats.

- **Start on login**: starts the uploader automatically, minimized, when you log in.
- **Minimize to tray**: closing or minimizing the window keeps it running in the tray.
  On GNOME you need the AppIndicator extension to see the tray icon; KDE, Cinnamon, XFCE and most
  other desktops have a tray already.
- **Pause** stops new uploads until you resume.
- **Updates** install themselves: the app checks every hour, downloads the new version, and shows
  a banner. Click **Restart now** or just restart it later. You can turn this off in Settings.

### 2. Background service (no window)

The same program can run with no window at all. Use this if you don't want a window or tray icon, or
on a machine you only reach over SSH. Run it as a systemd user service so it starts on login and
runs in the background. You don't need to keep a terminal open.

First set up the prefix, either by running the desktop app once, or by creating
`~/.config/heroesprofile/config.json` yourself:

```json
{ "prefix": "/path/to/your/prefix" }
```

Then install the app to `~/.local/bin` (skip this if you already ticked **Show in app menu**) and
enable the service:

```sh
./HeroesProfileUploader-linux-x86_64.AppImage install
mkdir -p ~/.config/systemd/user
cat > ~/.config/systemd/user/heroesprofile-uploader.service <<'UNIT'
[Unit]
Description=Heroes Profile replay uploader
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
ExecStart=%h/.local/bin/heroesprofile-uploader run
Restart=on-failure
RestartSec=10

[Install]
WantedBy=default.target
UNIT
systemctl --user daemon-reload
systemctl --user enable --now heroesprofile-uploader
```

Useful commands:

```sh
journalctl --user -u heroesprofile-uploader -f        # watch what it's doing
systemctl --user stop heroesprofile-uploader          # stop it
systemctl --user disable heroesprofile-uploader       # don't start on login any more
```

**The service doesn't update itself.** Only the desktop app does. When a new version is out, the
service writes a warning to its log (`journalctl`). To update, download the new version and:

```sh
systemctl --user stop heroesprofile-uploader
./HeroesProfileUploader-linux-x86_64.AppImage install
systemctl --user start heroesprofile-uploader
```

You can also run it directly in a terminal. It keeps running until you press Ctrl+C:

```sh
heroesprofile-uploader run                            # upload everything new, keep watching
heroesprofile-uploader scan --dry-run                 # just list what would upload (uploads nothing)
heroesprofile-uploader run --prefix /path/to/prefix   # use a different prefix than the config file
```

## Files

| What | Where |
|---|---|
| Settings | `~/.config/heroesprofile/config.json` |
| Log | `~/.local/share/heroesprofile/logs/log.txt` (the app's **Show log** button opens it) |
| Upload history | `~/.local/share/heroesprofile/` |
| Installed app | `~/.local/bin/heroesprofile-uploader` |

## Uninstall

Untick **Show in app menu** (or run `heroesprofile-uploader uninstall`). This removes the app, the menu
entry and the start-on-login entry. If you set up the service, also run
`systemctl --user disable --now heroesprofile-uploader` and delete
`~/.config/systemd/user/heroesprofile-uploader.service`. To remove your settings and history too,
delete `~/.config/heroesprofile`, `~/.local/share/heroesprofile` and `~/.net/heroesprofile-uploader`
(libraries the app unpacks on first start).

## Problems?

- **"Couldn't find Heroes of the Storm in that prefix"**: pick the folder that contains `drive_c`,
  not `drive_c` itself or the game's install folder. You need to have played at least one game, so
  that the `Documents/Heroes of the Storm/Accounts` folder exists inside the prefix.
- **No icon in the taskbar**: tick **Show in app menu**. Some desktops only show the icon for apps in
  the menu.
- Anything else: check the log (see [Files](#files)) and open an issue with it.

## Building from source

```sh
dotnet publish Heroesprofile.Uploader.Linux -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
packaging/linux/build-appimage.sh <path to the published heroesprofile-uploader> HeroesProfileUploader-linux-x86_64.AppImage
```

Needs the .NET 8 SDK. `Directory.Build.props` switches off the replay parser's GitVersionTask on
non-Windows builds, where its MSBuild task can't load.
