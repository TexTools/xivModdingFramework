using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.FileTypes;
using xivModdingFramework.Mods.FileTypes.PMP;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.Helpers
{
    public static class PenumbraAttachHandler
    {
        public static bool IsAttached
        {
            get
            {
                return Transaction != null;
            }
        }
        public static bool IsPaused
        {
            get
            {
                return _LOADING;
            }
        }

        private static ModTransaction Transaction;

        private static string ModFolder;

        private static PMPJson _PmpInfo;
        private static PMPJson PmpInfo
        {
            get => _PmpInfo;
            set {
                _PmpInfo = value;
                ParsePmpInfo();
            }
        }

        /// <summary>
        /// Dictionary of Internal FFXIV File Path => Real Disk/Penumbra location 
        /// </summary>
        private static Dictionary<string, string> PmpFilePaths;

        private static FileSystemWatcher Watcher;

        // Simple locking var/semaphore.
        private static bool _LOADING = false;

        // Debounced functions to trigger each update half.
        private static Action DebouncedWatcherAction = Debounce(TakeWatcherAction, 800);
        private static Action DebouncedUpdatePenumbra = Debounce(UpdatePenumbra, 800);

        // Tracking vars for when we need to write files to Penumbra.
        private static bool _WantsGlobalWrite = false;
        private static HashSet<string> _WantsSingleWrite = new HashSet<string>();

        // Tracking vars for when we need to read files from Penumbra.
        private static bool _WantsGlobalRead;
        private static HashSet<string> _WantsSingleRead = new HashSet<string>();

        #region Baseline Attach/Detach Handling

        /// <summary>
        /// Attach the penumbra handler to the given transaction.
        /// </summary>
        /// <param name="penumbraModFolder"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static async Task<ModTransaction> Attach(string penumbraModFolder, ModTransaction tx = null, bool alreadyLoaded = false)
        {
            if(Transaction != null)
            {
                throw new Exception("Cannot attach to new penumbra mod when one is already attached.");
            }

            penumbraModFolder.Replace("\\", "/");
            if (!penumbraModFolder.EndsWith("/"))
            {
                penumbraModFolder += "/";
            }

            var settings = new ModTransactionSettings()
            {
                StorageType = xivModdingFramework.SqPack.FileTypes.EFileStorageType.UncompressedIndividual,
                Target = ETransactionTarget.PenumbraModFolder,
                TargetPath = penumbraModFolder,
            };

            var files = await TTMP.ModPackToSimpleFileList(penumbraModFolder, false);

            if (files == null)
            {
                throw new Exception("Folder was not a valid Penumbra Mod folder, or the mod had multiple options.\n\nTarget must be a Penumbra Mod with only one valid option.");
            }
            ModFolder = penumbraModFolder;

            if(tx == null)
            {
                tx = await ModTransaction.BeginTransaction(true, null, settings);
            } else if(tx.State == ETransactionState.Preparing)
            {
                tx.Settings = settings;
                tx.Start();
            } else if(tx.State == ETransactionState.Open)
            {
                tx.Settings = settings;
            } else 
            {
                throw new Exception("Cannot attach to actively working or closed transaction.");
            }

            Transaction = tx;
            try
            {
                await ReloadPenumbraModpack(alreadyLoaded);

                Transaction.FileChanged += Transaction_FileChanged;
                Transaction.TransactionStateChanged += Transaction_TransactionStateChanged;

                // Set up file system watch.
                if(Watcher != null)
                {
                    DetatchWatch();
                    Watcher.Dispose();
                }

                Watcher = new FileSystemWatcher(ModFolder);
                Watcher.IncludeSubdirectories = true;
                Watcher.EnableRaisingEvents = true;
                AttachWatch();

                return Transaction;
            }
            catch
            {
                await Detach(true);
                throw;
            }
        }

        /// <summary>
        /// Detatches the penumbra handler from the current transaction.
        /// </summary>
        /// <returns></returns>
        public static async Task Detach(bool closeTransaction = true)
        {
            if (Transaction == null)
            {
                return;
            }

            while (_LOADING)
            {
                await Task.Delay(5);
            }

            Transaction.FileChanged -= Transaction_FileChanged;
            Transaction.TransactionStateChanged -= Transaction_TransactionStateChanged;

            if (closeTransaction && Transaction.State != ETransactionState.Closed)
            {
                await ModTransaction.CancelTransaction(Transaction, true);
            }

            if (Watcher != null)
            {
                DetatchWatch();
                Watcher.Dispose();
                Watcher = null;
            }

            Transaction = null;
            ModFolder = null;
            PmpInfo = null;

        }

        private static async void Transaction_TransactionStateChanged(ModTransaction sender, ETransactionState oldState, ETransactionState newState)
        {
            if(Transaction == null)
            {
                return;
            }

            try
            {
                if (newState == ETransactionState.Invalid || newState == ETransactionState.Closed)
                {
                    await Detach(false);
                }
            } catch (Exception e)
            {
                // Interesting.  Can't do much here without hard-crashing the application.
                // But we should really never get here.
                Trace.WriteLine(e);
            }
        }

        #endregion


        #region Updating Penumbra of TT-Side Changes
        private static void Transaction_FileChanged(string internalFilePath, long newOffset)
        {
            try { 
                if (Transaction == null)
                {
                    return;
                }

                if (_LOADING)
                {
                    return;
                }
                if (IOUtil.IsMetaInternalFile(internalFilePath))
                {
                    return;
                }

                if (!Transaction.ModifiedFiles.Contains(internalFilePath))
                {
                    // File was reset.  We have to rewrite the penumbra JSON files in this case.
                    _WantsGlobalWrite = true;
                }
                // If this file doesn't exist in the penumbra state yet, is a manipulation file, or is deleted, we need to do a full write.
                else if (!PmpFilePaths.ContainsKey(internalFilePath) || internalFilePath.EndsWith(".meta") || internalFilePath.EndsWith(".rgsp") || newOffset == 0)
                {
                    _WantsGlobalWrite = true;
                } else
                {
                    _WantsSingleWrite.Add(internalFilePath);
                }

                DebouncedUpdatePenumbra();
            }
            catch (Exception e)
            {
                Trace.WriteLine(e);
            }


        }

        private static async void UpdatePenumbra()
        {
            while (_LOADING)
            {
                await Task.Delay(5);
            }

            _LOADING = true;
            bool full = _WantsGlobalWrite;
            try
            {
                if (_WantsGlobalWrite)
                {
                    await CommitAndReparse();
                }
                else
                {
                    if(_WantsSingleWrite.Count == 0)
                    {
                        return;
                    }

                    DetatchWatch();
                    try
                    {

                        foreach (var file in _WantsSingleWrite)
                        {
                            if (Transaction == null)
                            {
                                return;
                            }

                            if (!PmpFilePaths.ContainsKey(file))
                            {
                                continue;
                            }

                            var fileData = await Transaction.ReadFile(file, false, false);
                            var realPath = PmpFilePaths[file];

                            var dir = Path.GetDirectoryName(realPath);
                            Directory.CreateDirectory(dir);
                            File.WriteAllBytes(realPath, fileData);
                        }
                    }
                    finally
                    {
                        AttachWatch();
                    }
                }
            }
            catch
            {
                // No-Op
            }
            finally
            {
                await PenumbraRefresh(full);
                _WantsSingleWrite.Clear();
                _WantsGlobalWrite = false;
                _LOADING = false;
            }

        }

        private static async Task CommitAndReparse()
        {
            if (_LOADING)
            {
                await Task.Delay(5);
            }

            if(Transaction == null)
            {
                return;
            }

            _LOADING = true;
            try
            {
                DetatchWatch();
                await ModTransaction.CommitTransaction(Transaction, false);
                await PenumbraRefresh(true);

                var pmpInfo = await PMP.LoadPMP(ModFolder, false);
                PmpInfo = pmpInfo.pmp;
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
            }
            finally
            {
                AttachWatch();
                _LOADING = false;
            }
        }
        private static async Task PenumbraRefresh(bool reload = false)
        {
            try
            {
                if (ModFolder != null)
                {
                    var di = new DirectoryInfo(ModFolder);
                    var folder = di.Name;
                    if (reload)
                    {
                        await PenumbraAPI.ReloadMod(folder);
                    }
                    if (XivCache.FrameworkSettings.PenumbraRedrawMode == FrameworkSettings.EPenumbraRedrawMode.RedrawAll)
                        await PenumbraAPI.Redraw();
                    else if (XivCache.FrameworkSettings.PenumbraRedrawMode == FrameworkSettings.EPenumbraRedrawMode.RedrawSelf)
                        await PenumbraAPI.RedrawSelf();
                }
            }
            catch
            {
                // No-Op.
            }
        }

        #endregion


        #region Reading Changes from Penumbra into TT

        /// <summary>
        /// Reload the target penumbra modpack back into the current Tx.
        /// </summary>
        /// <returns></returns>
        private static async Task ReloadPenumbraModpack(bool jsonOnly = false)
        {
            if (Transaction == null
                || Transaction.State != ETransactionState.Open
                || Transaction.Settings.Target != ETransactionTarget.PenumbraModFolder
                || Transaction.Settings.TargetPath != ModFolder)
            {
                return;
            }

            while (_LOADING)
            {
                await Task.Delay(5);
            }

            _LOADING = true;
            try
            {
                var tx = Transaction;
                var penumbraFolder = tx.Settings.TargetPath;


                var pmpAndPath = await PMP.LoadPMP(penumbraFolder, true);
                PmpInfo = pmpAndPath.pmp;

                if (jsonOnly)
                {
                    return;
                }

                // Don't so anything fancy here, just strictly import the files.
                var importSettings = new ModPackImportSettings()
                {
                    AutoAssignSkinMaterials = false,
                    RootConversionFunction = null,
                    SourceApplication = "PenumbraLiveEdit",
                    UpdateEndwalkerFiles = false,
                    UpdatePartialEndwalkerFiles = false,
                };

                // Get full unpacked list.
                var files = await TTMP.ModPackToSimpleFileList(penumbraFolder, true, tx);
                await TTMP.ImportFiles(files, null, importSettings, tx);

                // Now reset any files in this transaction that were not contained in the PMP.
                var allFiles = new HashSet<string>(files.Keys);
                var modifiedFiles = Transaction.ModifiedFiles;

                foreach(var file in modifiedFiles)
                {
                    if (!allFiles.Contains(file) && !IOUtil.IsMetaInternalFile(file))
                    {
                        await Transaction.ResetFile(file);
                    }
                }

            }
            finally
            {
                _LOADING = false;
            }
        }

        public static void WatchFileChanged(string path)
        {
            var sanitizedPath = Path.GetFullPath(path);
            var internalPaths = PmpFilePaths.Where(x => x.Value == sanitizedPath).ToList();

            if (internalPaths.Count == 0)
            {
                // This is a file we don't already have match for.
                // Which could be a JSON or a new user file, either way, we have to reload the full mod.
                _WantsGlobalRead = true;
            }
            else
            {
                foreach (var iPath in internalPaths) {
                    _WantsSingleRead.Add(iPath.Key);
                }
            }
            DebouncedWatcherAction();
        }

        private static async void TakeWatcherAction()
        {
            try
            {
                if(Transaction == null)
                {
                    return;
                }
                if(_WantsGlobalRead == false && _WantsSingleRead.Count == 0)
                {
                    return;
                }

                if (_WantsGlobalRead)
                {
                    _WantsSingleRead.Clear();
                    await ReloadPenumbraModpack();
                }
                else
                {
                    foreach(var file in  _WantsSingleRead)
                    {
                        await ReloadSingleFile(file);
                    }
                }
            }
            catch(Exception ex)
            {
                Trace.WriteLine(ex);
            }
        }

        private static async Task ReloadSingleFile(string file)
        {
            if(Transaction == null)
            {
                return;
            }

            if(!PmpFilePaths.ContainsKey(file))
            {
                // Not sure how we would get here, but no info on how to reload this file.
                // Possible there was a full reload between the queue and now, in which case,
                // this is a moot call.
                Trace.WriteLine("Requested single file reload for file which does not have matching real path: " + file);
                return;
            }

            while (_LOADING)
            {
                await Task.Delay(5);
            }

            _LOADING = true;
            try
            {
                var realPath = PmpFilePaths[file];

                var fileInfo = new FileStorageInformation()
                {
                    StorageType = EFileStorageType.UncompressedIndividual,
                    RealPath = realPath,
                    FileSize = 0, // Don't need this for what we're calling.
                    RealOffset = 0,
                };
                var data = await TransactionDataHandler.GetUncompressedFile(fileInfo);
                if(data == null || data.Length == 0)
                {
                    // Hmm.
                    return;
                }
                await Dat.WriteModFile(data, file, "PenumbraLiveEdit", null, Transaction, false);
            }
            finally
            {
                _LOADING = false;
            }
        }

        #endregion

        public static void Pause()
        {
            Transaction.FileChanged -= Transaction_FileChanged;
            Transaction.TransactionStateChanged -= Transaction_TransactionStateChanged;
            DetatchWatch();
            _LOADING = true;
        }

        public static void Resume()
        {
            Transaction.FileChanged += Transaction_FileChanged;
            Transaction.TransactionStateChanged += Transaction_TransactionStateChanged;
            AttachWatch();
            _LOADING = false;
        }

        private static void ParsePmpInfo()
        {
            PmpFilePaths = new Dictionary<string, string>();
            if (PmpInfo == null)
            {
                return;
            }

            var opt = PmpInfo.DefaultMod as PmpStandardOptionJson;
            if (PmpInfo.Groups.Count > 0)
            {
                opt = PmpInfo.Groups[0].Options[0] as PmpStandardOptionJson;
            }

            if(opt == null)
            {
                throw new NotImplementedException();
            }

            foreach (var swap in opt.Files)
            {
                var xivPath = swap.Key;
                var realPath = Path.GetFullPath(Path.GetFullPath(Path.Combine(ModFolder, swap.Value)));

                PmpFilePaths.Add(xivPath, realPath);
            }
        }

        private static Action Debounce(Action func, int milliseconds = 300)
        {
            CancellationTokenSource cancelTokenSource = null;

            return () =>
            {
                cancelTokenSource?.Cancel();
                cancelTokenSource = new CancellationTokenSource();

                Task.Delay(milliseconds, cancelTokenSource.Token)
                    .ContinueWith(t =>
                    {
                        if (t.IsCompleted && !t.IsCanceled)
                        {
                            func();
                        }
                    }, TaskScheduler.Default);
            };
        }

        #region File Watcher Wrapping
        private static void DetatchWatch()
        {
            if (Watcher != null)
            {
                Watcher.EnableRaisingEvents = false;
                Watcher.Changed -= Watcher_FileChanged;
                Watcher.Created -= Watcher_FileCreated;
                Watcher.Deleted -= Watcher_FileDeleted;
                Watcher.Renamed -= Watcher_FileRenamed;
                Watcher.Error -= Watcher_Error;
            }
        }
        private static void AttachWatch()
        {
            if (Watcher != null && Transaction != null)
            {
                // Safety call to prevent multi-attach.
                DetatchWatch();
                Watcher.EnableRaisingEvents = true;
                Watcher.Changed += Watcher_FileChanged;
                Watcher.Created += Watcher_FileCreated;
                Watcher.Deleted += Watcher_FileDeleted;
                Watcher.Renamed += Watcher_FileRenamed;
                Watcher.Error += Watcher_Error;
            }
        }

        private static void Watcher_Error(object sender, ErrorEventArgs e)
        {
            Trace.WriteLine(e);
        }

        private static void Watcher_FileRenamed(object sender, RenamedEventArgs e)
        {
            if (_LOADING) return;
            if (sender != Watcher) return;
            if (e.FullPath.ToLower().EndsWith(".ttproject")) return;
            _WantsGlobalRead = true;
            DebouncedWatcherAction();
        }

        private static void Watcher_FileDeleted(object sender, FileSystemEventArgs e)
        {
            if (_LOADING) return;
            if (sender != Watcher) return;
            if (e.FullPath.ToLower().EndsWith(".ttproject")) return;
            _WantsGlobalRead = true;
            DebouncedWatcherAction();
        }

        private static void Watcher_FileCreated(object sender, FileSystemEventArgs e)
        {
            if (_LOADING) return;
            if (sender != Watcher) return;
            if (e.FullPath.ToLower().EndsWith(".ttproject")) return;
            _WantsGlobalRead = true;
            DebouncedWatcherAction();
        }

        private static void Watcher_FileChanged(object sender, FileSystemEventArgs e)
        {
            if (_LOADING) return;
            if (sender != Watcher) return;
            if (e.FullPath.ToLower().EndsWith(".ttproject")) return;
            var path = e.FullPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            WatchFileChanged(path);
        }
        #endregion

    }
}
