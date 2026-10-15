# Beta test checklist

For testers trying the new app before it replaces the current one. Install it using
[INSTALL.md](INSTALL.md). On macOS, [macos/TESTING.md](macos/TESTING.md) has extra Mac-specific steps.

Please report anything that doesn't work, with your OS and your log file (**Show log** opens the logs
folder).

## Every OS

**First start**
- [ ] It finds your replays by itself (Windows, macOS), or after you pick your Wine/Proton prefix in
      **Settings** (Linux). Your replays appear in the list.
- [ ] Windows, coming from the current uploader: your settings came over (the match page boxes, theme,
      webhook, Twitch key), and nothing is uploaded a second time.
- [ ] Windows, coming from the current uploader: it offers to uninstall the old one. After
      **Uninstall**, the old one is gone from **Settings → Apps** and doesn't start when you log in, and
      the new app still has your settings and upload history.

**Uploading**
- [ ] Play a game. After it ends, the new replay appears and changes to **Success**.
- [ ] With **Prematch Page** ticked, the pre-match page opens during the loading screen.
- [ ] With **Postmatch Page** ticked, the post-match page opens after the game.
- [ ] Webhook (optional): set one in Settings. Your Discord/Slack channel gets a message for the match.
- [ ] Twitch extension (optional): paste your key, click **Check key**, tick **Twitch Extension**. Your
      viewers see your lobby and talents.

**Settings and window**
- [ ] Switching the theme (Light / Dark / Follow system) changes the window straight away.
- [ ] Close the app and open it again. It opens where you left it, with your settings kept.
- [ ] With **Minimize to tray** (or **Minimize to menu bar** on Mac) ticked, minimizing hides it to the
      tray / menu bar icon, and clicking that icon brings it back.
- [ ] **Start with windows** / **Start on login**: log out and back in. It starts minimized.
- [ ] Opening it a second time just brings the running one to the front; it doesn't start a second copy.

**Updates**
- [ ] When a newer beta is published, a banner appears within an hour: "An update is downloaded…".
      **Restart now** brings it back on the new version (the title bar shows the version).
- [ ] Or ignore the banner and restart it later. It comes back on the new version.

## Known beta limitations

- **Unsigned builds:** your OS warns the first time you open one (see [INSTALL.md](INSTALL.md)). That
  goes away once the app is signed.
- **Don't run it alongside the current Windows uploader:** both would upload every game. The new app
  warns you if the old one is running, and offers to uninstall it.
- **Linux tarball:** the `.tar.gz` program doesn't update itself; it shows an "Update available" link
  when a newer release is out (as a service, it logs it).
