using System;
using System.Collections.Generic;
using System.Linq;

namespace Heroesprofile.Uploader.Common.Test
{
    public partial class ManagerTests
    {
        private class PreMatchNoNewFilesMonitor : PreMatchIMonitor
        {
            public event EventHandler<EventArgs<string>> TempReplayCreated;
            public void Start() { }
            public void Stop() { }
        }
    }
}
