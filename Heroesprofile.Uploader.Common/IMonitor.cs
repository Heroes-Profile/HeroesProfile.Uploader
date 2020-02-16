using System;
using System.Collections.Generic;

namespace Heroesprofile.Uploader.Common
{
    public interface IMonitor
    {
        event EventHandler<EventArgs<string>> ReplayAdded;

        IEnumerable<string> ScanReplays();
        void Start();
        void Stop();
    }
}