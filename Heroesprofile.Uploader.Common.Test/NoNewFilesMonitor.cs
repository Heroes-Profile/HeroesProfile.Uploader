using System;
using System.Collections.Generic;
using System.Linq;

namespace Heroesprofile.Uploader.Common.Test
{
    public partial class ManagerTests
    {
        private class NoNewFilesMonitor : IMonitor
        {
            public event EventHandler<EventArgs<string>> ReplayAdded;

            public IEnumerable<string> ScanReplays() => Enumerable.Empty<string>();
            public void Start() { }
            public void Stop() { }
        }
    }
}
