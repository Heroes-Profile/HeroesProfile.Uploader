using NLog;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Linux
{
    /// <summary>
    /// Keeps the GUI to one instance per user, like the Windows app's SingleInstanceManager: the first
    /// instance listens on a Unix socket, and a second launch (app menu clicked while the tray copy
    /// runs, say) asks it to show its window and exits. Without this, two instances would upload from
    /// the same replay storage, and the second one could apply a staged update over the running first.
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private const string ShowMessage = "show\n";

        private readonly Socket _listener;
        private readonly string _socketPath;

        /// <summary>Raised on a thread-pool thread when another launch asks this instance to show itself.</summary>
        public event Action ActivationRequested;

        private SingleInstance(Socket listener, string socketPath)
        {
            _listener = listener;
            _socketPath = socketPath;
            Task.Run(AcceptLoop);
        }

        // $XDG_RUNTIME_DIR is per-user and private (0700); the data dir is the fallback when it isn't set.
        private static string SocketPath
        {
            get {
                var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
                return !string.IsNullOrEmpty(runtimeDir) && Directory.Exists(runtimeDir)
                    ? Path.Combine(runtimeDir, "heroesprofile-uploader.sock")
                    : Path.Combine(AppConfig.DataDir, "instance.sock");
            }
        }

        /// <summary>
        /// Becomes the running instance, or returns null after asking the one already running to show
        /// itself. With <paramref name="waitForPrevious"/> (a relaunch right after an update), waits a
        /// while for the previous instance to exit instead of handing over to it straight away.
        /// If the check itself fails, runs anyway - a broken lock must never stop the app starting.
        /// </summary>
        public static SingleInstance TryAcquire(bool waitForPrevious)
        {
            var path = SocketPath;
            var deadline = DateTime.UtcNow + (waitForPrevious ? TimeSpan.FromSeconds(15) : TimeSpan.Zero);
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                while (true) {
                    var listener = TryListen(path);
                    if (listener != null) {
                        return new SingleInstance(listener, path);
                    }

                    using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try {
                        client.Connect(new UnixDomainSocketEndPoint(path));
                    }
                    catch (SocketException) {
                        // Nobody listening - left over from an instance that didn't exit cleanly.
                        File.Delete(path);
                        continue;
                    }

                    if (DateTime.UtcNow < deadline) {
                        Thread.Sleep(250);
                        continue;
                    }
                    client.Send(Encoding.ASCII.GetBytes(ShowMessage));
                    return null;
                }
            }
            catch (Exception ex) {
                _log.Warn(ex, "Single-instance check failed - starting anyway.");
                return new SingleInstance(null, null);
            }
        }

        private static Socket TryListen(string path)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try {
                socket.Bind(new UnixDomainSocketEndPoint(path));
                socket.Listen(4);
                return socket;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse) {
                socket.Dispose();
                return null;
            }
        }

        private async Task AcceptLoop()
        {
            if (_listener == null) {
                return;
            }
            var buffer = new byte[16];
            while (true) {
                try {
                    using var client = await _listener.AcceptAsync();
                    var read = await client.ReceiveAsync(buffer, SocketFlags.None);
                    if (Encoding.ASCII.GetString(buffer, 0, read) == ShowMessage) {
                        ActivationRequested?.Invoke();
                    }
                }
                catch (ObjectDisposedException) {
                    return;
                }
                catch (SocketException ex) {
                    if (ex.SocketErrorCode == SocketError.OperationAborted) {
                        return;
                    }
                    _log.Debug(ex, "Single-instance accept failed");
                }
            }
        }

        public void Dispose()
        {
            if (_listener == null) {
                return;
            }
            _listener.Dispose();
            try {
                File.Delete(_socketPath);
            }
            catch (Exception ex) {
                _log.Debug(ex, $"Could not remove {_socketPath}");
            }
        }
    }
}
