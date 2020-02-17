using System.Collections.Generic;

namespace Heroesprofile.Uploader.Common.Test
{
    public partial class ManagerTests
    {
        private class MockStorage : IReplayStorage
        {
            private IEnumerable<ReplayFile> InitialFiles { get; }
            public MockStorage(IEnumerable<ReplayFile> initialFiles) => InitialFiles = initialFiles;
            public IEnumerable<ReplayFile> Load() => InitialFiles;
            public void Save(IEnumerable<ReplayFile> files) { }
        }
    }
}
