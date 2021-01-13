using System;
using System.Collections.Generic;

namespace Heroesprofile.Uploader.Common
{
    public interface LiveIMonitor
    {
        event EventHandler<EventArgs<string>> TempBattleLobbyCreated;
        event EventHandler<EventArgs<string>> StormSaveCreated;
        void Start();
        bool IsRunning();
        void StopBattleLobbyWatcher();
        void StopStormSaveWatcher();
    }
}