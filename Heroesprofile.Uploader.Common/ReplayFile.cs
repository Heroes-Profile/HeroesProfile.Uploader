using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace Heroesprofile.Uploader.Common
{
    [Serializable]
    public class ReplayFile : INotifyPropertyChanged
    {
        [XmlIgnore]
        public string Fingerprint { get; set; }
        public string Filename { get; set; }
        public DateTime Created { get; set; }

        private bool _deleted;
        public bool Deleted
        {
            get {
                return _deleted;
            }
            set {
                if (_deleted == value) {
                    return;
                }

                _deleted = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Deleted)));
            }
        }

        UploadStatus _uploadStatus = UploadStatus.None;
        public UploadStatus UploadStatus
        {
            get {
                return _uploadStatus;
            }
            set {
                if (_uploadStatus == value) {
                    return;
                }

                _uploadStatus = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UploadStatus)));
            }
        }

        int _replayId;

        /// <summary>
        /// The replay's id on Heroes Profile (heroesprofile.com/Match/Single/&lt;id&gt;), from the upload or
        /// the duplicate check; 0 when not known (not uploaded, or recorded before the app kept it).
        /// </summary>
        public int ReplayId
        {
            get {
                return _replayId;
            }
            set {
                if (_replayId == value) {
                    return;
                }

                _replayId = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReplayId)));
            }
        }

        public ReplayFile() { } // Required for serialization

        public ReplayFile(string filename)
        {
            Filename = filename;
            Created = File.GetCreationTime(filename);
        }

        public override string ToString()
        {
            return Filename;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public class ReplayFileComparer : IEqualityComparer<ReplayFile>
        {
            public bool Equals(ReplayFile x, ReplayFile y)
            {
                return x.Filename == y.Filename && x.Created == y.Created;
            }

            public int GetHashCode(ReplayFile obj)
            {
                return obj.Filename.GetHashCode() ^ obj.Created.GetHashCode();
            }
        }
    }
}
