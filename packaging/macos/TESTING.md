# Testing the macOS build

The macOS version isn't released yet. These steps are for testers helping check it on a real Mac
with Heroes of the Storm installed. The general checklist is in [../BETA-TESTING.md](../BETA-TESTING.md);
this page adds the Mac-specific parts.

## Get the build

This build is for Apple Silicon Macs (M1 and later). An Intel build comes later.

1. Open the latest successful **CI** run on the `OSAgnosticUpdate` branch:
   <https://github.com/Heroes-Profile/HeroesProfile.Uploader/actions/workflows/ci.yml?query=branch%3AOSAgnosticUpdate>
2. Under **Artifacts**, download `heroesprofile-uploader-macos-arm64-unsigned` and unzip it.
3. It isn't signed yet, so macOS will refuse to open it until you clear the "downloaded from the
   internet" flag. In Terminal, in the folder you unzipped it to:

   ```sh
   chmod +x heroesprofile-uploader
   xattr -d com.apple.quarantine heroesprofile-uploader
   ./heroesprofile-uploader
   ```

   Leave the Terminal window open while testing; closing it closes the uploader.

## What to check

Tick off what works and send us anything that doesn't, along with your log file:
`~/Library/Application Support/Heroesprofile/logs/log.txt`.

**Window and settings**
- [ ] The window opens and lists your replays. It finds
      `~/Library/Application Support/Blizzard/Heroes of the Storm/Accounts` by itself.
- [ ] **Settings** opens, and the replay folder shows "Found: …".
- [ ] Switching **Theme** between Light, Dark and Follow system changes the window straight away.
- [ ] Paste your Twitch uploader key in Settings and click **Check key**. Then quit the uploader
      (Cmd+Q), start it again, and check the key is still there. It's kept in your Keychain:
      Keychain Access should list an item called **Heroes Profile Uploader**.

**Menu bar and login**
- [ ] Tick **Minimize to menu bar**, then minimize the window. It disappears, and a Heroes Profile
      icon appears in the menu bar. Clicking the icon, or the uploader's Dock icon, brings it back.
- [ ] Tick **Start on login**. The file `~/Library/LaunchAgents/com.heroesprofile.uploader.plist`
      should appear. (Starting it at login only works properly once the app ships as a real `.app`.)

**A real game (most important)**
1. Tick **Prematch Page** and **Postmatch Page**.
2. Start a game. **While the loading screen shows**, run this in a second Terminal window and send
   us what it prints:

   ```sh
   find /private/var/folders -name replay.server.battlelobby 2>/dev/null
   ```

   This tells us exactly where the Mac client writes the lobby file.
- [ ] The pre-match page opens in your browser during the loading screen.
- [ ] After the game, the new replay appears in the list and changes to **Success**.
- [ ] The post-match page opens.
