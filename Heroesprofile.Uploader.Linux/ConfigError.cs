using System;

namespace Heroesprofile.Uploader.Linux
{
    /// <summary>
    /// A user-facing setup problem (bad config, unresolvable prefix). The message is written straight
    /// to the console with no stack trace - it's meant to be read and acted on, not debugged.
    /// </summary>
    public class ConfigError : Exception
    {
        public ConfigError(string message) : base(message) { }
    }
}
