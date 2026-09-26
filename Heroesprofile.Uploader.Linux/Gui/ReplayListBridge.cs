using Avalonia.Threading;
using Heroesprofile.Uploader.Common;
using Heroesprofile.Uploader.Linux.Gui.ViewModels;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;

namespace Heroesprofile.Uploader.Linux.Gui
{
    /// <summary>
    /// Bridges Manager.Files (an ObservableCollectionEx&lt;ReplayFile&gt; that Manager mutates from
    /// background threads - the upload loop, the folder rescanner) onto a UI-thread
    /// ObservableCollection&lt;ReplayRowViewModel&gt; the replay list binds directly to, and logs
    /// each replay's outcome at Info once it reaches a terminal status - the GUI's answer to "upload
    /// results must be visible" (the Windows app only logs at Debug, inside Uploader.cs itself).
    /// </summary>
    internal sealed class ReplayListBridge
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        // Everything Analyzer/Uploader can leave a file at once it's done with it - i.e. not None
        // (not yet looked at) and not InProgress (currently uploading).
        private static readonly HashSet<UploadStatus> TerminalStatuses = new HashSet<UploadStatus> {
            UploadStatus.Success, UploadStatus.Duplicate, UploadStatus.UploadError, UploadStatus.AiDetected,
            UploadStatus.CustomGame, UploadStatus.PtrRegion, UploadStatus.Incomplete, UploadStatus.TooOld,
        };

        public ObservableCollection<ReplayRowViewModel> Rows { get; } = new ObservableCollection<ReplayRowViewModel>();

        private readonly Manager _manager;
        private readonly Dictionary<ReplayFile, ReplayRowViewModel> _byFile = new Dictionary<ReplayFile, ReplayRowViewModel>();
        private readonly HashSet<ReplayFile> _logged = new HashSet<ReplayFile>();

        public ReplayListBridge(Manager manager)
        {
            _manager = manager;
            manager.Files.CollectionChanged += (_, e) => Dispatcher.UIThread.Post(() => OnCollectionChanged(e));
            manager.Files.ItemPropertyChanged += (sender, e) => {
                if (e.PropertyName != nameof(ReplayFile.UploadStatus)) {
                    return;
                }
                var file = (ReplayFile)sender;
                Dispatcher.UIThread.Post(() => OnStatusChanged(file));
            };
            Rebuild();
        }

        private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action) {
                case NotifyCollectionChangedAction.Add:
                    for (var i = 0; i < e.NewItems.Count; i++) {
                        var file = (ReplayFile)e.NewItems[i];
                        if (_byFile.ContainsKey(file)) {
                            // Already have a row for it (e.g. a Rebuild() raced this same Add) - don't duplicate it.
                            continue;
                        }
                        // Manager mutates Files from a background thread; by the time this Post'd handler
                        // runs, more items may have arrived and NewStartingIndex may no longer be in range.
                        var index = Math.Clamp(e.NewStartingIndex + i, 0, Rows.Count);
                        Rows.Insert(index, GetOrCreate(file));
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    foreach (ReplayFile file in e.OldItems) {
                        if (_byFile.TryGetValue(file, out var row)) {
                            _byFile.Remove(file);
                            Rows.Remove(row);
                        }
                    }
                    break;

                default:
                    // Reset (Manager.Start's initial AddRange) or anything else unexpected - safest to
                    // just re-derive the whole list from source.
                    Rebuild();
                    break;
            }
        }

        private void Rebuild()
        {
            var snapshot = TakeSnapshot();
            _byFile.Clear();
            Rows.Clear();
            foreach (var file in snapshot) {
                Rows.Add(GetOrCreate(file));
            }
        }

        /// <summary>
        /// Manager.Files is an ObservableCollectionEx Manager mutates (Insert/AddRange) from its own
        /// background threads with no lock - ObservableCollectionEx adds none, and Common shouldn't
        /// grow one just for this. A plain `foreach` over it here can therefore race a mutation and
        /// throw InvalidOperationException ("collection was modified"); retrying is safe and cheap -
        /// worst case is redoing an in-memory copy a couple of times until nothing mutates mid-loop.
        /// </summary>
        private List<ReplayFile> TakeSnapshot()
        {
            while (true) {
                try {
                    var snapshot = new List<ReplayFile>();
                    foreach (var file in _manager.Files) {
                        snapshot.Add(file);
                    }
                    return snapshot;
                }
                catch (InvalidOperationException) {
                    // Files changed mid-enumeration - try again.
                }
            }
        }

        private ReplayRowViewModel GetOrCreate(ReplayFile file)
        {
            if (!_byFile.TryGetValue(file, out var row)) {
                row = new ReplayRowViewModel(file);
                _byFile[file] = row;
            }
            return row;
        }

        private void OnStatusChanged(ReplayFile file)
        {
            if (_byFile.TryGetValue(file, out var row)) {
                row.RefreshFromFile();
            }
            LogOutcomeIfTerminal(file);
        }

        private void LogOutcomeIfTerminal(ReplayFile file)
        {
            // _logged.Add returns false (and does nothing) once a file's already been logged - a
            // replay only needs to report its outcome once per run.
            if (!TerminalStatuses.Contains(file.UploadStatus) || !_logged.Add(file)) {
                return;
            }
            _log.Info($"{Path.GetFileName(file.Filename)}: {file.UploadStatus}");
        }
    }
}
