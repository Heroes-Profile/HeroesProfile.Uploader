# Heroes Profile Uploader for Linux

Uploads your Heroes of the Storm replays to [Heroes Profile](https://www.heroesprofile.com) from Linux.
HotS keeps running under Wine/Proton (Lutris, Steam, Bottles, plain Wine); the uploader runs natively
and reads the replays from that Wine prefix. It's the same app as on Windows and macOS (see
[INSTALL.md](../INSTALL.md) for those).

## Quick start

1. Download the **`.AppImage`** from the
   [latest release](https://github.com/Heroes-Profile/HeroesProfile.Uploader/releases/latest).
   Nothing else to install: no .NET, no extra libraries. Works on any current x86_64 distro.
   Move it somewhere permanent first (for example `~/Applications`): it updates itself where it is.
2. Make it executable and start it. Either right-click → Properties → Permissions → "Allow executing
   file as program" and double-click it, or:

   ```sh
   chmod +x Heroesprofile.Uploader*.AppImage
   ./Heroesprofile.Uploader*.AppImage
   ```

3. It tells you it can't find your replays yet. Click **Open Settings**, then **Browse...** and pick
   your Wine/Proton prefix: the folder that contains `drive_c`.
4. Tick **Show in app menu** to add it to your app launcher. The menu entry runs the AppImage where
   you put it, so it keeps working through updates.

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
- **Minimize to tray**: minimizing the window keeps it running in the tray (closing it quits).
  On GNOME you need the AppIndicator extension to see the tray icon; KDE, Cinnamon, XFCE and most
  other desktops have a tray already.
- **Pause uploading**, in the tray icon's menu, stops new uploads until you resume.
- **Updates** install themselves: the app checks every hour, downloads the new version, and shows
  a banner. Click **Restart now** or just restart it later. To turn this off, set
  `"AutoUpdate": false` in the config file.

### 2. Background service (no window)

The same program can run with no window at all. Use this if you don't want a window or tray icon, or
on a machine you only reach over SSH. Run it as a systemd user service so it starts on login and
runs in the background. You don't need to keep a terminal open.

First set up the prefix, either by running the desktop app once, or by creating
`~/.config/heroesprofile/config.json` yourself:

```json
{ "ReplayPath": "/path/to/your/prefix" }
```

(Config files from older versions that say `"prefix"` still work.)

The service uses the plain program rather than the AppImage. Download
`HeroesProfileUploader-linux-x64.tar.gz` from the same release, unpack it, install the program to
`~/.local/bin`, and enable the service:

```sh
tar -xzf HeroesProfileUploader-linux-x64.tar.gz
./HeroesProfileUploader/heroesprofile-uploader install
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
service writes a warning to its log (`journalctl`). To update, download and unpack the new tarball and:

```sh
systemctl --user stop heroesprofile-uploader
./HeroesProfileUploader/heroesprofile-uploader install
systemctl --user start heroesprofile-uploader
```

You can also run it directly in a terminal. It keeps running until you press Ctrl+C:

```sh
heroesprofile-uploader run                            # upload everything new, keep watching
heroesprofile-uploader scan --dry-run                 # just list what would upload (uploads nothing)
heroesprofile-uploader run --prefix /path/to/prefix   # use a different prefix than the config file
```

## Checking your download

Every release has a `SHA256SUMS` file listing the AppImage's and tarball's checksums, and
`SHA256SUMS.asc`, a signature over it made with the project's release key:

- **Key:** [heroesprofile-uploader.asc](heroesprofile-uploader.asc) (Heroes Profile Uploader Releases)
- **Fingerprint:** `B559 8B38 AB5A 6EB8 B9E7  3CE0 F614 3744 E68A 9BFA`

Download `SHA256SUMS` and `SHA256SUMS.asc` into the folder with your download, then:

```sh
gpg --import heroesprofile-uploader.asc                 # once
gpg --verify SHA256SUMS.asc SHA256SUMS                  # "Good signature from Heroes Profile Uploader Releases"
sha256sum -c --ignore-missing SHA256SUMS                # your file: OK
```

Check that the fingerprint `gpg` prints matches the one above. The AppImage's own updates are checked
automatically, so this is only for the file you download yourself.

## Files

| What | Where |
|---|---|
| Settings | `~/.config/heroesprofile/config.json` |
| Log | `~/.local/share/heroesprofile/logs/log.txt` (the app's **Show log** button opens it) |
| Upload history | `~/.local/share/heroesprofile/` |
| Installed program (service/tarball only) | `~/.local/bin/heroesprofile-uploader` |

## Uninstall

Untick **Show in app menu** and **Start on login**, then delete the AppImage. For the tarball
install, run `heroesprofile-uploader uninstall` instead; it removes the program, the menu entry and
the start-on-login entry. If you set up the service, also run
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

The AppImage (how `.github/workflows/release-desktop.yml` builds it):

```sh
dotnet tool restore
dotnet publish Heroesprofile.Uploader.Desktop -c Release -r linux-x64 --self-contained -o publish
dotnet vpk pack --packId Heroesprofile.Uploader --packVersion 3.0.0 --packDir publish --runtime linux-x64 \
  --channel linux --mainExe heroesprofile-uploader --icon Heroesprofile.Uploader.Desktop/Gui/Assets/icons/256.png -o releases
```

The plain program for the tarball:

```sh
dotnet publish Heroesprofile.Uploader.Desktop -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

Needs the .NET 10 SDK (`global.json` pins the version). `Directory.Build.props` switches off the
replay parser's GitVersionTask for `dotnet` builds, where its MSBuild task can't load. Add
`-p:RestoreLockedMode=false` if a publish complains that `packages.lock.json` doesn't match (NU1004):
a platform-specific publish restores extra runtime packages the lock files don't list.
