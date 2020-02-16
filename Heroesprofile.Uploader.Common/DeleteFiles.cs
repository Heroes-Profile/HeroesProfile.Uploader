using System;
using System.Linq;

namespace Heroesprofile.Uploader.Common
{
    [Flags]
    public enum DeleteFiles
    {
        None = 0x00,
        PTR = 0x01,
        Ai = 0x02,
        Custom = 0x04,
        Brawl = 0x08,
        QuickMatch = 0x10,
        UnrankedDraft = 0x20,
        HeroLeague = 0x40,
        TeamLeague = 0x80,
        StormLeague = 0x100,
    }
}