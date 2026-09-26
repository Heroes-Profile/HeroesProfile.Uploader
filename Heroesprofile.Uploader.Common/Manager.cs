using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Threading;
using NLog;
using Nito.AsyncEx;
using System.Diagnostics;
using Heroes.ReplayParser;
using System.Collections.Concurrent;

namespace Heroesprofile.Uploader.Common
{
    public class Manager : INotifyPropertyChanged
    {
        /// <summary>
        /// Upload thead count
        /// </summary>
        public const int MaxThreads = 1;
        //public const int MaxThreads = 1;

        /// <summary>The two values <see cref="Status"/> takes - a named constant so callers compare
        /// against these instead of hardcoding the strings themselves.</summary>
        public const string UploadingStatus = "Uploading...";
        public const string IdleStatus = "Idle";

        /// <summary>
        /// Replay list
        /// </summary>
        public ObservableCollectionEx<ReplayFile> Files { get; private set; } = new ObservableCollectionEx<ReplayFile>();

        private static Logger _log = LogManager.GetCurrentClassLogger();
        private bool _initialized = false;
        private AsyncCollection<ReplayFile> processingQueue = new AsyncCollection<ReplayFile>(new ConcurrentStack<ReplayFile>());
        private readonly IReplayStorage _storage;
        private IUploader _uploader;
        private IAnalyzer _analyzer;
        private IMonitor _monitor;
        private ILiveMonitor _live_monitor;
        private ILiveProcessor _liveProcessor;
        private TimeSpan _waitTime = TimeSpan.FromSeconds(3);
        public event PropertyChangedEventHandler PropertyChanged;

        // Signaled (set) = not paused. Starts signaled so a Manager that's never paused behaves
        // exactly as before - this is the only state UploadLoop waits on besides the queue itself.
        private readonly AsyncManualResetEvent _resumed = new AsyncManualResetEvent(true);

        /// <summary>
        /// Pauses the upload loop between replays - in-flight uploads finish, new ones wait. The
        /// queue keeps filling from the folder watcher while paused. Windows-neutral: nothing sets
        /// this unless a UI wires it up, so behavior is unchanged if it's never touched.
        /// </summary>
        public bool Paused
        {
            get {
                return !_resumed.IsSet;
            }
            set {
                if (value == Paused) {
                    return;
                }
                if (value) {
                    _resumed.Reset();
                } else {
                    _resumed.Set();
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Paused)));
            }
        }

        public bool PreMatchPage { get; set; }
        public bool PostMatchPage { get; set; }

        /// <summary>Live lobby, hero and talent data for the Heroes Profile Twitch extension.</summary>
        public TwitchLiveSession Twitch { get; } = new TwitchLiveSession();

        /// <summary>Either live feature needs the battle lobby watched.</summary>
        private bool WatchesLiveGames => PreMatchPage || Twitch.Enabled;


        private string _status = "";




        /// <summary>
        /// Current uploader status
        /// </summary>
        public string Status
        {
            get {
                return _status;
            }
        }

        private Dictionary<UploadStatus, int> _aggregates = new Dictionary<UploadStatus, int>();
        /// <summary>
        /// List of aggregate upload stats
        /// </summary>
        public Dictionary<UploadStatus, int> Aggregates
        {
            get {
                return _aggregates;
            }
        }

        /// <summary>
        /// Which replays to delete after upload
        /// </summary>
        public DeleteFiles DeleteAfterUpload { get; set; }

        public Manager(IReplayStorage storage)
        {
            this._storage = storage;
            Files.ItemPropertyChanged += (_, __) => { RefreshStatusAndAggregates(); };
            Files.CollectionChanged += (_, __) => { RefreshStatusAndAggregates(); };
        }

        /// <summary>
        /// Start uploading and watching for new replays
        /// </summary>
        public async void Start(IMonitor monitor, ILiveMonitor live_monitor, IAnalyzer analyzer, IUploader uploader, ILiveProcessor liveProcessor)
        {
            if (_initialized) {
                return;
            }
            _initialized = true;

            _uploader = uploader;
            _analyzer = analyzer;
            _liveProcessor = liveProcessor;

            _monitor = monitor;
            _live_monitor = live_monitor;

            var replays = ScanReplays();
            Files.AddRange(replays);
            replays.Where(x => x.UploadStatus == UploadStatus.None).Map(x => processingQueue.Add(x));

            _monitor.ReplayAdded += async (_, e) => {
                await EnsureFileAvailable(e.Data);

                // The game this replay finishes: send the Twitch extension any talent
                // the last storm save missed, then mark it over.
                if (Twitch.Enabled && Twitch.HasGame) {
                    try {
                        await Twitch.EndGame(e.Data);
                    }
                    catch (Exception ex) {
                        _log.Error(ex, "Error closing the Twitch extension game");
                    }
                }

                if (WatchesLiveGames) {
                    RestartLiveWatchers();
                }

                var replay = new ReplayFile(e.Data);
                Files.Insert(0, replay);
                processingQueue.Add(replay);

            };

            ReplayLocation.Changed += (_, __) => ReloadReplayFolder();

            _monitor.Start();
            StartBattleLobbyWatcherEvent();
            StartStormSaveWatcherEvent();

            for (int i = 0; i < MaxThreads; i++) {
                Task.Run(UploadLoop).Forget();
            }
        }
        private void StartBattleLobbyWatcherEvent()
        {
            if (WatchesLiveGames) {
                _live_monitor.TempBattleLobbyCreated += async (_, e) => {

                    _live_monitor.StopBattleLobbyWatcher();
                    _liveProcessor = new LiveProcessor(PreMatchPage, Twitch);

                    var tmpPath = Path.GetTempFileName();
                    try {
                        await EnsureFileAvailable(e.Data);
                        await SafeCopy(e.Data, tmpPath, true);
                        await _liveProcessor.StartProcessing(tmpPath);
                    }
                    catch (Exception ex) {
                        // without this the watcher below never restarts and the exception escapes
                        // an async void handler, which takes the process down with it
                        _log.Error(ex, $"Error processing battlelobby '{e.Data}'");
                    }
                    finally {
                        try {
                            File.Delete(tmpPath);
                        }
                        catch (Exception ex) {
                            _log.Debug($"Could not delete temp battlelobby copy '{tmpPath}': {ex.Message}");
                        }
                        _live_monitor.StartBattleLobby();
                    }
                };

                _live_monitor.StartBattleLobby();
            }
        }

        /// <summary>
        /// Turns the Twitch extension feed on or off without a restart.
        /// </summary>
        public void SetTwitchEnabled(bool enabled)
        {
            if (Twitch.Enabled == enabled) {
                return;
            }
            Twitch.Enabled = enabled;

            if (_initialized) {
                RestartLiveWatchers();
            }
        }

        /// <summary>
        /// Fresh watchers wired for whatever is switched on now. The old monitor is
        /// dropped rather than unhooked, as it always has been after each replay.
        /// </summary>
        private void RestartLiveWatchers()
        {
            _live_monitor.StopBattleLobbyWatcher();
            _live_monitor.StopStormSaveWatcher();
            _live_monitor = new LiveMonitor();
            StartBattleLobbyWatcherEvent();
            StartStormSaveWatcherEvent();
        }

        /// <summary>
        /// The game writes a .StormSave as it goes — after heroes load and as talents
        /// are picked. Only the Twitch extension reads them.
        /// </summary>
        private void StartStormSaveWatcherEvent()
        {
            if (!Twitch.Enabled) {
                return;
            }

            _live_monitor.StormSaveCreated += async (_, e) => {
                var tmpPath = Path.GetTempFileName();
                try {
                    // Created fires before the game has finished writing it.
                    await EnsureFileAvailable(e.Data, testWrite: false);
                    await SafeCopy(e.Data, tmpPath, true);
                    await Twitch.UpdateFromStormSave(tmpPath);
                }
                catch (Exception ex) {
                    _log.Error(ex, $"Error processing storm save '{e.Data}'");
                }
                finally {
                    try {
                        File.Delete(tmpPath);
                    }
                    catch (Exception ex) {
                        _log.Debug($"Could not delete temp storm save copy '{tmpPath}': {ex.Message}");
                    }
                }
            };

            _live_monitor.StartStormSave();
        }

        public void Stop()
        {
            _monitor.Stop();
            processingQueue.CompleteAdding();
        }

        /// <summary>
        /// Point the watchers at the currently configured replay folder and queue up any replays it holds
        /// that we haven't seen yet. Lets a folder change in settings take effect without a restart.
        /// </summary>
        public void ReloadReplayFolder()
        {
            if (!_initialized) {
                return;
            }

            _monitor.Stop();
            _monitor.Start();

            if (_live_monitor.IsStormSaveRunning()) {
                _live_monitor.StopStormSaveWatcher();
                _live_monitor.StartStormSave();
            }

            // scanning can take a while on a big folder, keep it off the caller's (ui) thread
            Task.Run(() => {
                try {
                    var comparer = new ReplayFile.ReplayFileComparer();
                    var known = new HashSet<ReplayFile>(Files, comparer);
                    var found = _monitor.ScanReplays()
                        .Select(x => new ReplayFile(x))
                        .Where(x => !known.Contains(x))
                        .OrderBy(x => x.Created)
                        .ToList();

                    if (!found.Any()) {
                        return;
                    }

                    _log.Info($"Found {found.Count} new replays in {ReplayLocation.Current}");
                    // insert oldest first so the newest ends up at the top of the list
                    found.Map(x => Files.Insert(0, x));
                    found.Where(x => x.UploadStatus == UploadStatus.None).Map(x => processingQueue.Add(x));
                }
                catch (Exception ex) {
                    _log.Error(ex, "Error rescanning replay folder");
                }
            }).Forget();
        }

        private async Task UploadLoop()
        {
            while (await processingQueue.OutputAvailableAsync()) {
                await _resumed.WaitAsync();
                try {
                    var file = await processingQueue.TakeAsync();

                    file.UploadStatus = UploadStatus.InProgress;

                    // test if replay is eligible for upload (not AI, PTR, Custom, etc)
                    var replay = _analyzer.Analyze(file);
                    if (file.UploadStatus == UploadStatus.InProgress && replay != null) {
                        // if it is, upload it
                        await _uploader.Upload(replay, file, PostMatchPage);
                    }
                    SaveReplayList();
                    if (ShouldDelete(file, replay)) {
                        //DeleteReplay(file);
                    }
                }
                catch (Exception ex) {
                    _log.Error(ex, "Error in upload loop");
                }
            }
        }

        private void RefreshStatusAndAggregates()
        {
            _status = Files.Any(x => x.UploadStatus == UploadStatus.InProgress) ? UploadingStatus : IdleStatus;
            _aggregates = Files.GroupBy(x => x.UploadStatus).ToDictionary(x => x.Key, x => x.Count());
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Aggregates)));
        }

        private void SaveReplayList()
        {
            try {
                // save only replays with fixed status. Will retry failed ones on next launch.
                var ignored = new[] { UploadStatus.None, UploadStatus.UploadError, UploadStatus.InProgress };
                _storage.Save(Files.Where(x => !ignored.Contains(x.UploadStatus)));
            }
            catch (Exception ex) {
                _log.Error(ex, "Error saving replay list");
            }
        }

        /// <summary>
        /// Load replay cache and merge it with folder scan results
        /// </summary>
        private List<ReplayFile> ScanReplays()
        {
            var replays = new List<ReplayFile>(_storage.Load());
            var lookup = new HashSet<ReplayFile>(replays);
            var comparer = new ReplayFile.ReplayFileComparer();
            replays.AddRange(_monitor.ScanReplays().Select(x => new ReplayFile(x)).Where(x => !lookup.Contains(x, comparer)));
            return replays.OrderByDescending(x => x.Created).ToList();
        }

        /// <summary>
        /// Delete replay file
        /// </summary>
        private static void DeleteReplay(ReplayFile file)
        {
            try {
                _log.Info($"Deleting replay {file}");
                file.Deleted = true;
                File.Delete(file.Filename);
            }
            catch (Exception ex) {
                _log.Error(ex, "Error deleting file");
            }
        }

        /// <summary>
        /// Ensure that HotS client finished writing replay file and it can be safely open
        /// </summary>
        /// <param name="filename">Filename to test</param>
        /// <param name="timeout">Timeout in milliseconds</param>
        /// <param name="testWrite">Whether to test read or write access</param>
        public async Task EnsureFileAvailable(string filename, bool testWrite = true)
        {
            var timer = Stopwatch.StartNew();

            while (timer.Elapsed < _waitTime) {
                try {
                    if (testWrite) {
                        File.OpenWrite(filename).Close();
                    } else {
                        File.OpenRead(filename).Close();
                    }
                    return;
                }
                catch (IOException) {
                    // File is still in use
                    await Task.Delay(100);
                }
                catch {
                    return;
                }
            }
        }

        /// <summary>
        /// Decide whether a replay should be deleted according to current settings
        /// </summary>
        /// <param name="file">replay file metadata</param>
        /// <param name="replay">Parsed replay</param>
        private bool ShouldDelete(ReplayFile file, Replay replay)
        {
            return
                DeleteAfterUpload.HasFlag(DeleteFiles.PTR) && file.UploadStatus == UploadStatus.PtrRegion ||
                DeleteAfterUpload.HasFlag(DeleteFiles.Ai) && file.UploadStatus == UploadStatus.AiDetected ||
                DeleteAfterUpload.HasFlag(DeleteFiles.Custom) && file.UploadStatus == UploadStatus.CustomGame ||
                file.UploadStatus == UploadStatus.Success && (
                    DeleteAfterUpload.HasFlag(DeleteFiles.Brawl) && replay.GameMode == GameMode.Brawl ||
                    DeleteAfterUpload.HasFlag(DeleteFiles.QuickMatch) && replay.GameMode == GameMode.QuickMatch ||
                    DeleteAfterUpload.HasFlag(DeleteFiles.UnrankedDraft) && replay.GameMode == GameMode.UnrankedDraft ||
                    DeleteAfterUpload.HasFlag(DeleteFiles.HeroLeague) && replay.GameMode == GameMode.HeroLeague ||
                    DeleteAfterUpload.HasFlag(DeleteFiles.TeamLeague) && replay.GameMode == GameMode.TeamLeague ||
                    DeleteAfterUpload.HasFlag(DeleteFiles.StormLeague) && replay.GameMode == GameMode.StormLeague
                );
        }
        private static async Task SafeCopy(string source, string dest, bool overwrite)
        {
            var watchdog = 10;
            var retry = false;
            do {
                try {
                    File.Copy(source, dest, overwrite);
                    retry = false;
                }
                catch (Exception ex) {
                    Debug.WriteLine($"Failed to copy ${source} to ${dest}. Counter at ${watchdog} CAUSED BY ${ex}");
                    if (watchdog <= 0) {
                        throw;
                    }
                    retry = true;
                }
                await Task.Delay(1000);
            } while (watchdog-- > 0 && retry);
        }
    }
}