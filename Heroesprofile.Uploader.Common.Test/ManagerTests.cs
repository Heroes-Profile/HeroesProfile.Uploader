using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Common.Test
{
    [TestClass]
    public partial class ManagerTests
    {
        private Task ShortRandomDelay()
        {
            var r = new Random();
            var delay = r.Next(100, 200);
            return Task.Delay(delay);
        }
        private static IEnumerable<ReplayFile> ThreeInOrder
        {
            get {
                var one = new ReplayFile("one") {
                    Created = new DateTime(2020, 1, 1, 0, 0, 1)
                };
                var two = new ReplayFile("two") {
                    Created = new DateTime(2020, 1, 1, 0, 0, 10)
                };
                var three = new ReplayFile("three") {
                    Created = new DateTime(2020, 1, 1, 0, 0, 20)
                };
                var initialFiles = new List<ReplayFile>() { one, two, three };
                return initialFiles;
            }
        }

        [TestMethod]
        [Ignore("Known intermittant failure: multiple uploads are started in parallel and don't always start in order")]
        public async Task InitialFilesStartInOrder()
        {
            var initialFiles = ThreeInOrder;

            var manager = new Manager(new MockStorage(initialFiles));
            var uploadTester = new MockUploader();
            
            var promise = new TaskCompletionSource<int>();
            Task done = promise.Task;

            var uploadsSeen = 0;
            var l = new object();
            ReplayFile lastUploadStarted = null;
            uploadTester.SetUploadCallback(async rf => {
                if (lastUploadStarted != null) {
                    try {
                        Assert.IsTrue(rf.Created >= lastUploadStarted.Created, $"upload started out of order, {lastUploadStarted} started after {rf}");
                    } catch (Exception e) {
                        promise.TrySetException(e);
                    }
                }
                lastUploadStarted = rf;
                await ShortRandomDelay();
                var isDone = false;
                lock (l) {
                    uploadsSeen++;
                    isDone = uploadsSeen >= 3;
                }
                if (isDone) {
                    promise.TrySetResult(uploadsSeen);
                }
            });

            manager.Start(new NoNewFilesMonitor(), new MockAnalizer(), uploadTester);
            await done;
        }



        [TestMethod]
        [Ignore("Known intermittant failure: multiple uploads are started in parallel and don't always end in order")]
        public async Task InitialFilesEndInorder() {
            var initialFiles = ThreeInOrder;

            var manager = new Manager(new MockStorage(initialFiles));
            var uploadTester = new MockUploader();
            var promise = new TaskCompletionSource<int>();
            Task done = promise.Task;

            var uploadsSeen = 0;
            var l = new object();
            ReplayFile lastUploadFinished = null;
            uploadTester.SetUploadCallback(async rf => {
                await ShortRandomDelay();
                if (lastUploadFinished != null) {
                    try {
                        Assert.IsTrue(rf.Created >= lastUploadFinished.Created, $"upload completed out of order, {lastUploadFinished} completed after {rf}");
                    }
                    catch (Exception e) {
                        promise.TrySetException(e);
                    }
                }
                lastUploadFinished = rf;
                var isDone = false;
                lock (l) {
                    uploadsSeen++;
                    isDone = uploadsSeen >= 3;
                }
                if (isDone) {
                    promise.TrySetResult(uploadsSeen);
                }
            });

            manager.Start(new NoNewFilesMonitor(), new MockAnalizer(), uploadTester);
            await done;
        }

        [TestMethod]
        public async Task AllInitialFilesProcessed()
        {
            var initialFiles = ThreeInOrder;

            var manager = new Manager(new MockStorage(initialFiles));
            var uploadTester = new MockUploader();
            var done = new TaskCompletionSource<int>();

            var uploadsSeen = 0;
            object l = new object();
            uploadTester.SetUploadCallback(async rf => {
                await ShortRandomDelay();
                lock (l) {
                    uploadsSeen++;
                    if (uploadsSeen >= 3) {
                        done.SetResult(uploadsSeen);
                    }
                }
            });

            manager.Start(new NoNewFilesMonitor(), new MockAnalizer(), uploadTester);
            var finished = await Task.WhenAny(Task.Delay(4000), done.Task);
            await finished;
            Assert.AreEqual(3, uploadsSeen);
        }
    }
}
