using System;
using System.Collections.Generic;

namespace Heroesprofile.Uploader.Common
{
    public interface PreMatchIMonitor
    {
        event EventHandler<EventArgs<string>> TempBattleLobbyCreated;
        event EventHandler<EventArgs<string>> StormSaveCreated;
        void Start();
        void Stop();
        bool IsRunning();
    }
}