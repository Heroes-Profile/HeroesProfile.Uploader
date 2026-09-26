using System;
using System.Collections.Generic;
using System.Linq;

namespace Heroesprofile.Uploader.Common
{
    public enum UploadStatus
    {
        None,
        Success,
        InProgress,
        UploadError,
        Duplicate,
        AiDetected,
        CustomGame,
        PtrRegion,
        Incomplete,
        TooOld,
        // Added after the others so the numbers of existing values don't change.
        Brawl,
    }
}
