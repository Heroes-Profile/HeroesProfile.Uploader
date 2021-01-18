using System;
using System.Collections.Generic;

namespace Heroesprofile.Uploader.Common
{
    public interface LiveIMonitor
    {
        event EventHandler<EventArgs<string>> TempBattleLobbyCreated;
        event EventHandler<EventArgs<string>> StormSaveCreated;
        void StartBattleLobby();
        void StartStormSave();


        void StopBattleLobbyWatcher();
        void StopStormSaveWatcher();

        bool IsBattleLobbyRunning();
        bool IsStormSaveRunning();

    }
}