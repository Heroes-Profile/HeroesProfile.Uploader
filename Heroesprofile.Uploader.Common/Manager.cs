﻿using System;
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


        public bool PreMatchPage { get; set; }
        public bool PostMatchPage { get; set; }


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
                if (PreMatchPage) {
         
                    _live_monitor.StopBattleLobbyWatcher();
                    _live_monitor.StopStormSaveWatcher();
                    _live_monitor = new LiveMonitor();
                }

                var replay = new ReplayFile(e.Data);
                Files.Insert(0, replay);
                processingQueue.Add(replay);

            };

            _monitor.Start();
            StartBattleLobbyWatcherEvent();

            for (int i = 0; i < MaxThreads; i++) {
                Task.Run(UploadLoop).Forget();
            }
        }
        private void StartBattleLobbyWatcherEvent()
        {
            if (PreMatchPage) {
                _live_monitor.TempBattleLobbyCreated += async (_, e) => {

                    _live_monitor.StopBattleLobbyWatcher();
                    _liveProcessor = new LiveProcessor(PreMatchPage);

                    await EnsureFileAvailable(e.Data);
                    var tmpPath = Path.GetTempFileName();
                    await SafeCopy(e.Data, tmpPath, true);
                    await _liveProcessor.StartProcessing(tmpPath);

                
               
                };

                _live_monitor.StartBattleLobby();
            }
        }

        public void Stop()
        {
            _monitor.Stop();
            processingQueue.CompleteAdding();
        }

        private async Task UploadLoop()
        {
            while (await processingQueue.OutputAvailableAsync()) {
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
            _status = Files.Any(x => x.UploadStatus == UploadStatus.InProgress) ? "Uploading..." : "Idle";
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