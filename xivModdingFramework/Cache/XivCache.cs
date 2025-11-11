using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items;
using xivModdingFramework.Items.Categories;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.Mods;
using xivModdingFramework.Resources;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Textures.Enums;

namespace xivModdingFramework.Cache
{

    public class CacheException : Exception
    {
        public CacheException(Exception ex) : base(ex.Message, ex) {
        }

    }

    public class FrameworkSettings
    {
        private string _TempDirectory = Path.GetTempPath();
        public string TempDirectory
        {
            get
            {
                return _TempDirectory;
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _TempDirectory = Path.GetTempPath();
                }
                else
                {
                    value = Path.GetFullPath(value);
                    Directory.CreateDirectory(value);
                    _TempDirectory = value;
                }
            }
        }

        private EModelingTool _ModelingTool = EModelingTool.Blender;
        public EModelingTool ModelingTool
        {
            get => _ModelingTool;
            set
            {
                _ModelingTool = value;
            }
        }

        public XivTexFormat DefaultTextureFormat { get; set; } = XivTexFormat.A8R8G8B8;

        public enum EPenumbraRedrawMode
        {
            RedrawAll,
            RedrawSelf,
            NoRedraw,
        }

        public EPenumbraRedrawMode PenumbraRedrawMode { get; set; } = EPenumbraRedrawMode.RedrawAll;
    }

    /// <summary>
    /// Item Dependency Cache for keeping track of item dependency information.
    /// </summary>
    public static class XivCache
    {
        private static GameInfo _gameInfo;
        private static DirectoryInfo _dbPath;
        private static DirectoryInfo _rootCachePath;
        public static readonly Version CacheVersion = new Version("1.0.3.5");
        private const string dbFileName = "mod_cache.db";
        private const string rootCacheFileName = "item_sets.db";
        private const string creationScript = "CreateCacheDB.sql";
        private const string rootCacheCreationScript = "CreateRootCacheDB.sql";

        public delegate void WriteStateChangedEventHandler(bool newState);
        public static event WriteStateChangedEventHandler GameWriteStateChanged;

        internal static void SetPragmas(SQLiteConnection db)
        {
            if (db.IsReadOnly(db.Database))
            {
                return;
            }

            using (var cmd = new SQLiteCommand("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;", db))
            {
                cmd.ExecuteNonQuery();
            }
        }

        internal static string CacheConnectionString
        {
            get
            {
                return "Data Source=" + _dbPath + ";Pooling=True;Max Pool Size=100;";
            }
        }
        internal static string RootsCacheConnectionString
        {
            get
            {
                return "Data Source=" + _rootCachePath + ";Pooling=True;Max Pool Size=100;";
            }
        }


        // Safety check to make sure we don't redundantly attempt to rebuild the cache.
        private static bool _REBUILDING = false;
        public static bool IsRebuilding
        {
            get
            {
                return _REBUILDING;
            }
        }
        public static GameInfo GameInfo
        {
            get
            {
                return _gameInfo;
            }
        }

        public static FrameworkSettings FrameworkSettings { get; set; } = new FrameworkSettings();

        private static bool _GameWriteEnbled = false;
        public static bool GameWriteEnabled {
            get => _GameWriteEnbled;
            set
            {
                _GameWriteEnbled = value;
                GameWriteStateChanged?.Invoke(_GameWriteEnbled);
            }
        }

        public static bool Initialized
        {
            get
            {
                return _gameInfo != null;
            }
        }

        public static bool CacheWorkerEnabled
        {
            get
            {
                return _cacheWorker != null;
            }
            private set
            {
                // State cannot be changed during a rebuild.
                // This shouldn't normally ever occur anyways, but
                // better to be safe.
                if (_REBUILDING) return;

                if (value && _cacheWorker == null)
                {

                    _CacheWorkerStartupComplete = false;
                    _cacheWorker = new BackgroundWorker
                    {
                        WorkerReportsProgress = true,
                        WorkerSupportsCancellation = true
                    };
                    _cacheWorker.DoWork += ProcessDependencyQueue;
                    _cacheWorker.RunWorkerAsync();
                }
                else if (value == false && _cacheWorker != null)
                {
                    // Sleep until the cache worker actually stops.
                    Trace.WriteLine("Cache Worker CancelAsync() Called");
                    _cacheWorker.CancelAsync();
                }
            }
        }

        public static void SetCacheWorkerStateSync(bool state)
        {
            CacheWorkerEnabled = state;
        }

        public static async Task SetCacheWorkerState(bool state)
        {
            CacheWorkerEnabled = state;
            if(state == false)
            {
                while(_cacheWorker != null)
                {
                    _cacheWorker.CancelAsync();
                    await Task.Delay(10);
                }
            } else
            {
                while (_CacheWorkerStartupComplete == false)
                {
                    await Task.Delay(10);
                }
            }
        }

        public enum CacheRebuildReason
        {
            CacheOK,
            NoCache,
            CacheVersionUpdate,
            FFXIVUpdate,
            LanguageChanged,
            RebuildFlag,
            ManualRequest,
            InvalidData
        }

        public static event EventHandler<CacheRebuildReason> CacheRebuilding;

        private static bool _CacheWorkerStartupComplete;
        private static BackgroundWorker _cacheWorker;


        /// <summary>
        /// Language is not actually required for Cache -reading-, only for cache generation, so it is 
        /// technically an optional parameter if you know you're just reading cache data.
        /// </summary>
        /// <param name="gameDirectory"></param>
        /// <param name="language"></param>
        /// <param name="validateCache"></param>
        public static async Task SetGameInfo(DirectoryInfo gameDirectory = null, XivLanguage language = XivLanguage.None, bool enableCacheWorker = true)
        {
            var gi = new GameInfo(gameDirectory, language);
            await SetGameInfo(gi, enableCacheWorker);
        }
        public static async Task SetGameInfo(GameInfo gameInfo = null, bool enableCacheWorker = true)
        {
            if(ModTransaction.ActiveTransaction != null)
            {
                throw new Exception("Cannot change GameInfo settings while there is an open write-enabled transaction.");
            }

            // We need to either have a valid game directory reference in the static already or need one in the constructor.
            if (_gameInfo == null && (gameInfo == null || gameInfo.GameDirectory == null)) {
                throw new Exception("First call to cache must include a valid game directoy.");
            }

            if (gameInfo != null && !gameInfo.GameDirectory.Exists)
            {
                throw new Exception("Provided Game Directory does not exist.");
            }


            // Sleep and lock this thread until rebuild is done.
            while (_REBUILDING)
            {
                Thread.Sleep(10);
            }

            if (gameInfo != null)
            {
                _gameInfo = gameInfo;
            }

            Modding.CreateModlist();

            if(GameInfo != null)
            {
                var gameWritingTxSettings = new ModTransactionSettings()
                {
                    Target = ETransactionTarget.GameFiles,
                    StorageType = EFileStorageType.CompressedIndividual,
                    TargetPath = XivCache.GameInfo.GameDirectory.FullName
                };
                ModTransaction.SetDefaultTransactionSettings(gameWritingTxSettings);
            }



            _dbPath = new DirectoryInfo(Path.Combine(_gameInfo.GameDirectory.Parent.Parent.FullName, dbFileName));
            _rootCachePath = new DirectoryInfo(Path.Combine(_gameInfo.GameDirectory.Parent.Parent.FullName, rootCacheFileName));

            if (!_REBUILDING)
            {

                var reason = CacheNeedsRebuild();
                if (reason != CacheRebuildReason.CacheOK && !_REBUILDING)
                {
					var ver = 
						reason == CacheRebuildReason.CacheVersionUpdate 
							? new Version(GetMetaValue("cache_version")) : CacheVersion;

					await RebuildCache(ver, reason);
                }
            }

            await XivRaceTree.BuildRaceTree();
            await SetCacheWorkerState(enableCacheWorker);

        }


        /// <summary>
        /// Tests if the cache needs to be rebuilt (and starts the process if it does.)
        /// </summary>
        private static CacheRebuildReason CacheNeedsRebuild()
        {
            Func<CacheRebuildReason> checkValidation = () =>
            {
                try
                {
                    if (!File.Exists(_dbPath.FullName))
                    {
                        return CacheRebuildReason.NoCache;
                    }

                    // FFXIV Updated?  This one always gets highest priority for reason.
                    var val = GetMetaValue("ffxiv_version");
                    Version version = val == null ? null : new Version(val);
                    if (version != _gameInfo.GameVersion)
                    {
                        return CacheRebuildReason.FFXIVUpdate;
                    }

                    // Cache structure updated?
                    val = GetMetaValue("cache_version");
                    version = new Version(val);
                    if (version != CacheVersion)
                    {
                        return CacheRebuildReason.CacheVersionUpdate;
                    }

                    if (_gameInfo.GameLanguage != XivLanguage.None)
                    {
                        // If user changed languages, we need to rebuild, too.
                        val = GetMetaValue("language");
                        if (val != _gameInfo.GameLanguage.ToString())
                        {
                            return CacheRebuildReason.LanguageChanged;
                        }
                    }

                    // Forced rebuild from a failed rebuild before restart.
                    val = GetMetaValue("needs_rebuild");
                    if (val != null)
                    {
                        return CacheRebuildReason.RebuildFlag;
                    }

                    return CacheRebuildReason.CacheOK;
                }
                catch (Exception Ex)
                {
                    return CacheRebuildReason.InvalidData;
                }
            };

            var result = checkValidation();
            if (result != CacheRebuildReason.CacheOK)
            {
                // Ensure we cleaned up after ourselves
                // in preprartion for calling rebuild.
                // Needs to be done in -this- thread before
                // Rebuild is Asynchronously called.
                WaitForSqlCleanup();
            }
            return result;
        }

        /// <summary>
        /// Destroys and rebuilds the cache.
        /// Function is intentionally synchronous to
        /// help ensure it's never accidentally called
        /// without an await.
        /// </summary>
        public static async Task RebuildCache(Version previousVersion, CacheRebuildReason reason = CacheRebuildReason.ManualRequest, ModTransaction tx = null)
        {
            var workerStatus = CacheWorkerEnabled;
            await SetCacheWorkerState(false);
            _REBUILDING = true;
            try
            {


                if (CacheRebuilding != null)
                {
                    // If there are any event listeners, invoke them.
                    CacheRebuilding.Invoke(null, reason);
                }

                Task.Run(async () =>
                {

                    if (_gameInfo.GameLanguage == XivLanguage.None)
                    {
                        throw new NotSupportedException("A valid language must be specified when rebuilding the Cache.");
                    }

                    if (tx == null)
                    {
                        tx = ModTransaction.BeginReadonlyTransaction();
                    }

                    try
                    {
                        CreateCache();

                        var tasks = new List<Task>();

                        var pre = (DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                        tasks.Add(RebuildItemsCache(tx));
                        tasks.Add(RebuildCharactersCache(tx));
                        tasks.Add(RebuildMonstersCache(tx));
                        tasks.Add(RebuildUiCache(tx));
                        tasks.Add(RebuildFurnitureCache(tx));
                        tasks.Add(BuildModdedItemDependencies(tx));

                        // This was originally running only if the reason was cache update,
                        // but if the cache gets messed up in one way or another, and has to
                        // rebuild on a new TT version for any reason other than CacheUpdate
                        // or whatever, it will prevent the migration from occurring properly
                        tasks.Add(MigrateCache(previousVersion));

                        await Task.WhenAll(tasks);

                        var post = (DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;

                        var result = post - pre;

                        SetMetaValue("cache_version", CacheVersion.ToString());
                        SetMetaValue("ffxiv_version", _gameInfo.GameVersion.ToString());
                        SetMetaValue("language", _gameInfo.GameLanguage.ToString());
                        SetMetaValue("build_time", result.ToString());

                    }
                    catch (Exception Ex)
                    {
                        try
                        {
                            // If we failed an update due to a post-patch error, keep us stuck in that state
                            // until a TexTools update and a proper completed rebuild.
                            if (reason == CacheRebuildReason.FFXIVUpdate)
                            {
                                SetMetaValue("ffxiv_version", new Version(0, 0, 0, 0).ToString());
                            }

                            SetMetaValue("needs_rebuild", "1");
                        }
                        catch
                        {
                            // No-op.  We're pretty fucked at this point.
                        }
                        _REBUILDING = false;
                        throw;
                    }
                }).Wait();
            } finally
            {
                _REBUILDING = false;
                await XivCache.SetCacheWorkerState(workerStatus);
            }
        }

        #region Cache Rebuilding

        /// <summary>
        /// Destroys and recreates the base SQL Database.
        /// </summary>
        private static void CreateCache()
        {

            // Sleep until the cache worker actually stops.
            if (_cacheWorker != null)
            {
                _cacheWorker.CancelAsync();
                while (_cacheWorker != null)
                {
                    Thread.Sleep(10);
                }
            }

            // We intentionally don't delete the root cache here.
            // That data is considered inviolate, and should never be changed
            // unless the user specifically requests to rebuild it, or
            // manually replaces the roots DB.
            WaitForSqlCleanup();

            try
            {
                File.Delete(_dbPath.FullName);
                File.Delete(_dbPath.FullName + "-shm");
                File.Delete(_dbPath.FullName + "-wal");
            } catch
            {
                // In some select situations sometimes the DB can still be in use
                // if we were asyncing other things.  Sleep a bit and see if we can succeed after.

                // ( The primary case of this happening is a START OVER without Index backups, where we
                // transition straight from a full modlist disable into a cache rebuild, without waiting
                // on the Cache to finish queueing entries into the DB. )
                Thread.Sleep(1000);
                File.Delete(_dbPath.FullName);
                File.Delete(_dbPath.FullName + "-shm");
                File.Delete(_dbPath.FullName + "-wal");
            }


            var cwd = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                db.BusyTimeout = 3000;
                db.Open();

                SetPragmas(db);

                var lines = File.ReadAllLines(Path.Combine(cwd, "Resources", "SQL", creationScript));
                var sqlCmd = String.Join("\n", lines);

                using (var cmd = new SQLiteCommand(sqlCmd, db))
                {
                    cmd.ExecuteScalar();
                }
                db.Close();
            }

            var backupFile = Path.Combine(cwd, "Resources", "DB", rootCacheFileName);

            if (!File.Exists(_rootCachePath.FullName))
            {

                // If we don't have a root cache file, we can do a few things...
                if (File.Exists(backupFile))
                {
                    // Copy the backup over. - Even if the backup is not for the correct patch of 
                    // FFXIV for this user's region, that's fine.  It's still some reasonable data.
                    // Worst case situation is either 
                    //  A - They're missing some raw set listings in the menus (Not a big deal)
                    //  B - They have some raw set listings in the menus that they don't have yet. (Won't load, not a big deal.)
                    File.Copy(backupFile, _rootCachePath.FullName);
                }
                else
                {
                    // No backup DB to use, just create a blank schema, other functions will fallback to using the
                    // roots from the item list as a partial list.
                    using (var db = new SQLiteConnection(RootsCacheConnectionString))
                    {
                        db.BusyTimeout = 3000;
                        db.Open();

                        SetPragmas(db);

                        var lines = File.ReadAllLines(Path.Combine(cwd, "Resources", "SQL", rootCacheCreationScript));
                        var sqlCmd = String.Join("\n", lines);

                        using (var cmd = new SQLiteCommand(sqlCmd, db))
                        {
                            cmd.ExecuteScalar();
                        }
                    }
                }
            }
            else if (File.Exists(backupFile))
            {
                // If we have both a backup and current file, see if the current is out of date.
                // (If we just updated textools and the download came with a new file)
                var backupDate = File.GetLastWriteTime(backupFile);
                var currentDate = File.GetLastWriteTime(_rootCachePath.FullName);
                if (backupDate > currentDate)
                {
                    // Our backup is newer, copy it through.
                    try
                    {
                        File.Delete(_rootCachePath.FullName);
                        File.Copy(backupFile, _rootCachePath.FullName);
                    } catch
                    {
                        // No-op, non-critical.
                    }
                }
            }
        }

        private static async Task MigrateCache(Version lastCacheVersion) {

            if (lastCacheVersion == null) return;
            if (lastCacheVersion == new Version("0.0.0.0")) return;
            if (lastCacheVersion == new Version()) return;

            if (lastCacheVersion < new Version("1.0.3.3"))
            {
                // Clear user's Skeletons folder from Pre-DT.
                var cwd = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
                var skelFolder = Path.Combine(cwd, "Skeletons");
                IOUtil.RecursiveDeleteDirectory(skelFolder);
            }

        }

        /// <summary>
        /// Populate the ui table.
        /// </summary>
        /// <returns></returns>
        private static async Task RebuildUiCache(ModTransaction tx)
        {
            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                var _ui = new UI();
                List<XivUi> list = new List<XivUi>();
                List<Task<List<XivUi>>> tasks = new List<Task<List<XivUi>>>();
                tasks.Add(_ui.GetActionList(tx));
                tasks.Add(_ui.GetLoadingImageList(tx));
                tasks.Add(_ui.GetMapList(tx));
                tasks.Add(_ui.GetMapSymbolList(tx));
                tasks.Add(_ui.GetOnlineStatusList(tx));
                tasks.Add(_ui.GetStatusList(tx));
                tasks.Add(_ui.GetWeatherList(tx));
                tasks.Add(_ui.GetUldList(tx));
                tasks.Add(_ui.GetPaintingUiImages(tx));
                await Task.WhenAll(tasks);

                var sum = tasks.Sum(x => x.Result.Count);
                list.Capacity = sum;

                foreach(var t in tasks)
                {
                    list.AddRange(t.Result);
                }

                db.BusyTimeout = 3000;
                db.Open();

                SetPragmas(db);

                using (var transaction = db.BeginTransaction())
                {
                    foreach (var item in list)
                    {
                        var query = @"
                            insert into ui ( name,  category,  subcategory,  path,  icon_id,  root,  mapzonecategory) 
                                    values ($name, $category, $subcategory, $path, $icon_id, $root, $mapzonecategory)
                                on conflict do nothing";
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            cmd.Parameters.AddWithValue("name", item.Name);
                            cmd.Parameters.AddWithValue("category", item.SecondaryCategory);
                            cmd.Parameters.AddWithValue("subcategory", item.TertiaryCategory);
                            cmd.Parameters.AddWithValue("path", item.UiPath);
                            cmd.Parameters.AddWithValue("icon_id", item.IconNumber);
                            cmd.Parameters.AddWithValue("mapzonecategory", item.MapZoneCategory);
                            cmd.Parameters.AddWithValue("root", null);  // Unsupported for UI elements for now.
                            cmd.ExecuteScalar();
                        }
                    }
                    transaction.Commit();
                }
            }
        }


        /// <summary>
        /// Populate the monsters table.
        /// </summary>
        /// <returns></returns>
        private static async Task RebuildMonstersCache(ModTransaction tx)
        {
            // Mounts, Minions, etc. are really just monsters.
            var tasks = new List<Task>();
            tasks.Add(RebuildMinionsCache(tx));
            tasks.Add(RebuildMountsCache(tx));
            tasks.Add(RebuildPetsCache(tx));

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Populate the housing table.
        /// </summary>
        /// <returns></returns>
        private static async Task RebuildFurnitureCache(ModTransaction tx)
        {
            using (var db = new SQLiteConnection(CacheConnectionString))
            {

                var _housing = new Housing();
                var list = await _housing.GetUncachedFurnitureList(tx);

                db.BusyTimeout = 3000;
                db.Open();

                SetPragmas(db);

                using (var transaction = db.BeginTransaction())
                {
                    foreach (var item in list)
                    {

                        var query = @"
                            insert into furniture ( name,  category,  subcategory,  primary_id,  icon_id,  root, secondary_id) 
                                          values($name, $category, $subcategory, $primary_id, $icon_id, $root, $secondary_id)";

                        var root = item.GetRootInfo();
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            cmd.Parameters.AddWithValue("name", item.Name);
                            cmd.Parameters.AddWithValue("category", item.SecondaryCategory);
                            cmd.Parameters.AddWithValue("subcategory", item.TertiaryCategory);
                            cmd.Parameters.AddWithValue("icon_id", item.IconId);
                            cmd.Parameters.AddWithValue("primary_id", item.ModelInfo.PrimaryID);
                            cmd.Parameters.AddWithValue("secondary_id", item.ModelInfo.SecondaryID);
                            if (root.IsValid())
                            {
                                cmd.Parameters.AddWithValue("root", root.ToString());
                            } else
                            {
                                cmd.Parameters.AddWithValue("root", null);
                            }
                            cmd.ExecuteScalar();
                        }
                    }
                    transaction.Commit();
                }
            }
        }

        /// <summary>
        /// Populate the mounts table.
        /// </summary>
        /// <returns></returns>
        private static async Task RebuildMountsCache(ModTransaction tx)
        {
            using (var db = new SQLiteConnection(CacheConnectionString))
            {

                var _companions = new Companions();
                var list = await _companions.GetUncachedMountList(tx);

                list.AddRange(await _companions.GetUncachedOrnamentList(tx));

                db.BusyTimeout = 3000;
                db.Open();

                SetPragmas(db);

                using (var transaction = db.BeginTransaction())
                {
                    foreach (var item in list)
                    {

                        var query = @"
                            insert into monsters ( name,  category,  primary_id,  secondary_id,  imc_variant,  model_type,  root,  icon) 
                                           values($name, $category, $primary_id, $secondary_id, $imc_variant, $model_type, $root, $icon)
                            on conflict do nothing";
                        var root = item.GetRootInfo();
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            try
                            {
                                cmd.Parameters.AddWithValue("name", item.Name);
                                cmd.Parameters.AddWithValue("category", item.SecondaryCategory);
                                cmd.Parameters.AddWithValue("primary_id", item.ModelInfo.PrimaryID);
                                cmd.Parameters.AddWithValue("secondary_id", item.ModelInfo.SecondaryID);
                                cmd.Parameters.AddWithValue("imc_variant", item.ModelInfo.ImcSubsetID);
                                cmd.Parameters.AddWithValue("icon", item.IconId);
                                cmd.Parameters.AddWithValue("model_type", ((XivMonsterModelInfo)item.ModelInfo).ModelType.ToString());
                                if (root.IsValid())
                                {
                                    cmd.Parameters.AddWithValue("root", root.ToString());
                                }
                                else
                                {
                                    cmd.Parameters.AddWithValue("root", null);
                                }
                                cmd.ExecuteScalar();
                            }
                            catch (Exception ex)
                            {
                                throw;
                            }
                        }
                    }
                    transaction.Commit();
                }
            }
        }
        /// <summary>
        /// Populate the pets.
        /// </summary>
        /// <returns></returns>
        private static async Task RebuildPetsCache(ModTransaction tx)
        {
            using (var db = new SQLiteConnection(CacheConnectionString))
            {

                var _companions = new Companions();
                var list = await _companions.GetUncachedPetList(tx);

                db.BusyTimeout = 3000;
                db.Open();

                SetPragmas(db);

                using (var transaction = db.BeginTransaction())
                {
                    foreach (var item in list)
                    {

                        var query = @"
                            insert into monsters ( name,  category,  primary_id,  secondary_id,  imc_variant,  model_type,  root,  icon) 
                                           values($name, $category, $primary_id, $secondary_id, $imc_variant, $model_type, $root, $icon)
                            on conflict do nothing";
                        var root = item.GetRootInfo();
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            try
                            {
                                cmd.Parameters.AddWithValue("name", item.Name);
                                cmd.Parameters.AddWithValue("category", item.SecondaryCategory);
                                cmd.Parameters.AddWithValue("primary_id", item.ModelInfo.PrimaryID);
                                cmd.Parameters.AddWithValue("secondary_id", item.ModelInfo.SecondaryID);
                                cmd.Parameters.AddWithValue("imc_variant", item.ModelInfo.ImcSubsetID);
                                cmd.Parameters.AddWithValue("icon", item.IconId);
                                cmd.Parameters.AddWithValue("model_type", ((XivMonsterModelInfo)item.ModelInfo).ModelType.ToString());
                                if (root.IsValid())
                                {
                                    cmd.Parameters.AddWithValue("root", root.ToString());
                                }
                                else
                                {
                                    cmd.Parameters.AddWithValue("root", null);
                                }
                                cmd.ExecuteScalar();
                            }
                            catch (Exception ex)
                            {
                                throw;
                            }
                        }
                    }
                    transaction.Commit();
                }
            }
        }

        /// <summary>
        /// Populate the minions table.
        /// </summary>
        /// <returns></returns>
        private static async Task RebuildMinionsCache(ModTransaction tx)
        {
            using (var db = new SQLiteConnection(CacheConnectionString))
            {

                var _companions = new Companions();
                var list = await _companions.GetUncachedMinionList(tx);

                db.BusyTimeout = 3000;
                db.Open();

                SetPragmas(db);

                using (var transaction = db.BeginTransaction())
                {
                    foreach (var item in list)
                    {

                        var query = @"
                            insert into monsters ( name,  category,  primary_id,  secondary_id,  imc_variant,  model_type,  root,  icon) 
                                           values($name, $category, $primary_id, $secondary_id, $imc_variant, $model_type, $root, $icon)
                            on conflict do nothing";
                        var root = item.GetRootInfo();
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            try {
                                cmd.Parameters.AddWithValue("name", item.Name);
                                cmd.Parameters.AddWithValue("primary_id", item.ModelInfo.PrimaryID);
                                cmd.Parameters.AddWithValue("secondary_id", item.ModelInfo.SecondaryID);
                                cmd.Parameters.AddWithValue("imc_variant", item.ModelInfo.ImcSubsetID);
                                cmd.Parameters.AddWithValue("category", item.SecondaryCategory);
                                cmd.Parameters.AddWithValue("icon", item.IconId);
                                cmd.Parameters.AddWithValue("model_type", ((XivMonsterModelInfo)item.ModelInfo).ModelType.ToString());
                                if (root.IsValid())
                                {
                                    cmd.Parameters.AddWithValue("root", root.ToString());
                                }
                                else
                                {
                                    cmd.Parameters.AddWithValue("root", null);
                                }
                                cmd.ExecuteScalar();
                            }
                            catch (Exception ex) {
                                throw;
                            }
                        }
                    }
                    transaction.Commit();
                }
            }
        }

        private static async Task RebuildCharactersCache(ModTransaction tx)
        {
            var _character = new Character();
            var items = await _character.GetUnCachedCharacterList(tx);
            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                db.BusyTimeout = 3000;
                db.Open();

                SetPragmas(db);

                using (var transaction = db.BeginTransaction())
                {
                    foreach (var item in items)
                    {
                        var query = @"insert into characters ( primary_id, slot, slot_full, name, root, race, secondary_id) 
                                                  values    ( $primary_id,$slot,$slot_full,$name,$root,$race,$secondary_id)";
                        var root = item.GetRootInfo();
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            if (item.ModelInfo != null)
                            {
                                cmd.Parameters.AddWithValue("primary_id", item.ModelInfo.PrimaryID);
                                cmd.Parameters.AddWithValue("secondary_id", item.ModelInfo.SecondaryID);
                            } else
                            {
                                cmd.Parameters.AddWithValue("primary_id", 0);
                                cmd.Parameters.AddWithValue("secondary_id", null);
                            }
                            cmd.Parameters.AddWithValue("slot", item.GetItemSlotAbbreviation());
                            cmd.Parameters.AddWithValue("slot_full", item.SecondaryCategory);
                            cmd.Parameters.AddWithValue("name", item.Name);
                            cmd.Parameters.AddWithValue("race", item.TertiaryCategory);
                            if (root.IsValid())
                            {
                                cmd.Parameters.AddWithValue("root", root.ToString());
                            }
                            else
                            {
                                cmd.Parameters.AddWithValue("root", null);
                            }
                            cmd.ExecuteScalar();
                        }
                    }
                    transaction.Commit();
                }
            }
        }
        private static async Task RebuildItemsCache(ModTransaction tx)
        {
            await RebuildGearCache(tx);
        }

        /// <summary>
        /// Populate the items table.
        /// </summary>
        /// <returns></returns>
        private static async Task RebuildGearCache(ModTransaction tx)
        {
            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                Gear gear = null;
                gear = new Gear();
                var items = await gear.GetUnCachedGearList(tx);

                db.BusyTimeout = 3000;
                db.Open();

                SetPragmas(db);

                using (var transaction = db.BeginTransaction())
                {
                    foreach (var item in items)
                    {
                        var query = @"insert into items ( exd_id,  primary_id,  secondary_id,  imc_variant,  slot,  slot_full,  name,  icon_id, root) 
                                                  values($exd_id, $primary_id, $secondary_id, $imc_variant, $slot, $slot_full, $name, $icon_id, $root)";
                        var root = item.GetRootInfo();
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            cmd.Parameters.AddWithValue("exd_id", item.ExdID);
                            cmd.Parameters.AddWithValue("primary_id", item.ModelInfo.PrimaryID);
                            cmd.Parameters.AddWithValue("secondary_id", item.ModelInfo.SecondaryID);
                            cmd.Parameters.AddWithValue("slot", item.GetItemSlotAbbreviation());
                            cmd.Parameters.AddWithValue("slot_full", item.SecondaryCategory);
                            cmd.Parameters.AddWithValue("imc_variant", item.ModelInfo.ImcSubsetID);
                            cmd.Parameters.AddWithValue("name", item.Name);
                            cmd.Parameters.AddWithValue("icon_id", item.IconId);
                            if(root.IsValid())
                            {
                                cmd.Parameters.AddWithValue("root", root.ToString());
                            }
                            else
                            {
                                cmd.Parameters.AddWithValue("root", null);
                            }
                            cmd.ExecuteScalar();
                        }
                    }
                    transaction.Commit();
                }
            }
        }

        /// <summary>
        /// Builds the initial base/required dependency list, aka
        /// all the file children of the modded files in the modlist.
        /// </summary>
        /// <returns></returns>
        private static async Task BuildModdedItemDependencies(ModTransaction tx = null)
        {
            if (tx == null)
            {
                tx = ModTransaction.BeginReadonlyTransaction();
            }
            var modList = await tx.GetModList();
            var paths = modList.Mods.Keys.ToList();

            QueueDependencyUpdate(paths);
        }

        #endregion

        #region Cached item list accessors

        /// <summary>
        /// Get the ui entries list, optionally with a substring filter.
        /// </summary>
        /// <param name="substring"></param>
        /// <returns></returns>
        internal static async Task<List<XivFurniture>> GetCachedFurnitureList(string substring = null)
        {
            WhereClause where = null;
            if (substring != null)
            {
                where = new WhereClause();
                where.Comparer = WhereClause.ComparisonType.Like;
                where.Column = "name";
                where.Value = "%" + substring + "%";
            }

            return BuildListFromTable("furniture", where, (reader) =>
            {
                return MakeFurniture(reader);
            });
        }


        /// <summary>
        /// Get the ui entries list, optionally with a substring filter.
        /// </summary>
        /// <param name="substring"></param>
        /// <returns></returns>
        internal static async Task<List<XivUi>> GetCachedUiList(string substring = null)
        {
            WhereClause where = null;
            if (substring != null)
            {
                where = new WhereClause();
                where.Comparer = WhereClause.ComparisonType.Like;
                where.Column = "name";
                where.Value = "%" + substring + "%";
            }

            return BuildListFromTable("ui", where, (reader) =>
            {
                return MakeUi(reader);
            });
        }

        /// <summary>
        /// Get the minions entries list, optionally with a substring filter.
        /// </summary>
        /// <param name="substring"></param>
        /// <returns></returns>
        internal static async Task<List<XivMinion>> GetCachedMinionsList(string substring = null)
        {
            var where = new WhereClause();

            var minionClause = new WhereClause();
            minionClause.Column = "category";
            minionClause.Value = XivStrings.Minions;
            minionClause.Join = WhereClause.JoinType.And;
            minionClause.Comparer = WhereClause.ComparisonType.Equal;
            where.Inner.Add(minionClause);

            if (substring != null)
            {
                var w = new WhereClause();
                w.Comparer = WhereClause.ComparisonType.Like;
                w.Join = WhereClause.JoinType.And;
                w.Column = "name";
                w.Value = "%" + substring + "%";
                where.Inner.Add(w);
            }

            try
            {
                return BuildListFromTable("monsters", where, (reader) =>
                {
                    return (XivMinion)MakeMonster(reader);
                });
            } catch(Exception ex)
            {
                throw;
            }
        }

        /// <summary>
        /// Get the pets entries list, optionally with a substring filter.
        /// </summary>
        /// <param name="substring"></param>
        /// <returns></returns>
        internal static async Task<List<XivPet>> GetCachedPetList(string substring = null)
        {
            var where = new WhereClause();

            var petClause = new WhereClause();
            petClause.Column = "category";
            petClause.Value = XivStrings.Pets;
            petClause.Join = WhereClause.JoinType.And;
            petClause.Comparer = WhereClause.ComparisonType.Equal;
            where.Inner.Add(petClause);

            if (substring != null)
            {
                var w = new WhereClause();
                w.Comparer = WhereClause.ComparisonType.Like;
                w.Join = WhereClause.JoinType.And;
                w.Column = "name";
                w.Value = "%" + substring + "%";
                where.Inner.Add(w);
            }

            try
            {
                return BuildListFromTable("monsters", where, (reader) =>
                {
                    return (XivPet)MakeMonster(reader);
                });
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        /// <summary>
        /// Get the mounts entries list, optionally with a substring filter.
        /// </summary>
        /// <param name="substring"></param>
        /// <returns></returns>
        internal static async Task<List<XivMount>> GetCachedMountList(string substring = null, string category = null)
        {
            var where = new WhereClause();

            if(category == null)
            {
                category = XivStrings.Mounts;
            }

            if (category != null)
            {
                var categoryClause = new WhereClause();
                categoryClause.Column = "category";
                categoryClause.Value = category;
                categoryClause.Join = WhereClause.JoinType.And;
                categoryClause.Comparer = WhereClause.ComparisonType.Equal;
                where.Inner.Add(categoryClause);
            }

            if (substring != null)
            {
                var w = new WhereClause();
                w.Comparer = WhereClause.ComparisonType.Like;
                w.Join = WhereClause.JoinType.And;
                w.Column = "name";
                w.Value = "%" + substring + "%";
                where.Inner.Add(w);
            }

            try
            {
                return BuildListFromTable("monsters", where, (reader) =>
                {
                    return (XivMount)MakeMonster(reader);
                });
            }
            catch (Exception ex)
            {
                throw;
            }
        }
        internal static async Task<List<XivCharacter>> GetCachedCharacterList(string substring = null)
        {
            WhereClause where = null;

            if (substring != null)
            {
                where.Comparer = WhereClause.ComparisonType.Like;
                where.Join = WhereClause.JoinType.And;
                where.Column = "name";
                where.Value = "%" + substring + "%";
            }

            List<XivCharacter> mainHands = new List<XivCharacter>();
            var list = BuildListFromTable("characters", where, (reader) =>
            {
                var item = MakeCharacter(reader);

                return item;
            });

            return list;
        }

        /// <summary>
        /// Get the gear entries list, optionally with a substring filter.
        /// </summary>
        /// <param name="substring"></param>
        /// <returns></returns>
        internal static async Task<List<XivGear>> GetCachedGearList(string substring = null)
        {
            WhereClause where = null;

            if (substring != null)
            {
                where.Comparer = WhereClause.ComparisonType.Like;
                where.Join = WhereClause.JoinType.And;
                where.Column = "name";
                where.Value = "%" + substring + "%";
            }


            List<XivGear> mainHands = new List<XivGear>();
            List<XivGear> offHands = new List<XivGear>();
            var list = BuildListFromTable("items", where, (reader) =>
            {
                var item = MakeGear(reader);
                if (item.Name.Contains(XivStrings.Main_Hand))
                {
                    mainHands.Add(item);
                } else if (item.Name.Contains(XivStrings.Off_Hand))
                {
                    offHands.Add(item);
                }

                return item;
            });

            // Assign pairs based on items that came out of the same EXD row.
            foreach(var item in mainHands)
            {
                var pair = offHands.FirstOrDefault(x => x.ExdID == item.ExdID);
                if(pair != null)
                {
                    pair.PairedItem = item;
                    item.PairedItem = pair;
                }
            }
            return list;
        }

        /// <summary>
        /// Creates the appropriate monster type item from a database row.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        internal static IItemModel MakeMonster(CacheReader reader)
        {
            var cat = reader.GetString("category");
            IItemModel item;

            if (cat == XivStrings.Minions)
            {
                item = new XivMinion
                {
                    PrimaryCategory = XivStrings.Companions,
                    SecondaryCategory = reader.GetString("category"),
                    Name = reader.GetString("name"),
                    IconId = (uint) reader.GetInt32("icon"),
                    ModelInfo = new XivMonsterModelInfo
                    {
                        ModelType = (XivItemType)Enum.Parse(typeof(XivItemType), reader.GetString("model_type")),
                        PrimaryID = reader.GetInt32("primary_id"),
                        SecondaryID = reader.GetInt32("secondary_id"),
                        ImcSubsetID = reader.GetInt32("imc_variant"),
                    }
                };

            }
            else if (cat == XivStrings.Pets)
            {
                item = new XivPet
                {
                    PrimaryCategory = XivStrings.Companions,
                    SecondaryCategory = reader.GetString("category"),
                    Name = reader.GetString("name"),
                    ModelInfo = new XivMonsterModelInfo
                    {
                        ModelType = (XivItemType)Enum.Parse(typeof(XivItemType), reader.GetString("model_type")),
                        PrimaryID = reader.GetInt32("primary_id"),
                        SecondaryID = reader.GetInt32("secondary_id"),
                        ImcSubsetID = reader.GetInt32("imc_variant"),
                    }
                };

            }
            else
            {
                item = new XivMount
                {
                    PrimaryCategory = XivStrings.Companions,
                    SecondaryCategory = reader.GetString("category"),
                    Name = reader.GetString("name"),
                    IconId = (uint)reader.GetInt32("icon"),
                    ModelInfo = new XivMonsterModelInfo
                    {
                        ModelType = (XivItemType)Enum.Parse(typeof(XivItemType), reader.GetString("model_type")),
                        PrimaryID = reader.GetInt32("primary_id"),
                        SecondaryID = reader.GetInt32("secondary_id"),
                        ImcSubsetID = reader.GetInt32("imc_variant"),
                    }
                };
            }

            return item;
        }
        
        internal static XivCharacter MakeCharacter(CacheReader reader)
        {
            var mi = new XivModelInfo();

            var item = new XivCharacter
            {
                PrimaryCategory = XivStrings.Character,
                SecondaryCategory = reader.GetString("slot_full"),
                TertiaryCategory = reader.GetString("race"),
                ModelInfo = mi,
            };

            item.Name = reader.GetString("name");
            mi.PrimaryID = reader.GetInt32("primary_id");
            mi.SecondaryID = reader.GetInt32("secondary_id");
            return item;
        }

        /// <summary>
        /// Creates a XivGear entry from a database row.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        internal static XivGear MakeGear(CacheReader reader)
        {
            var primaryMi = new XivModelInfo();

            var item = new XivGear
            {
                ExdID = reader.GetInt32("exd_id"),
                PrimaryCategory = XivStrings.Gear,
                SecondaryCategory = reader.GetString("slot_full"),
                ModelInfo = primaryMi,
            };

            item.Name = reader.GetString("name");
            item.IconId = (uint)reader.GetInt32("icon_id");
            primaryMi.PrimaryID = reader.GetInt32("primary_id");
            primaryMi.SecondaryID = reader.GetInt32("secondary_id");
            primaryMi.ImcSubsetID = reader.GetInt32("imc_variant");

            if (item.IsWeapon)
            {
                item.PrimaryCategory = XivStrings.Weapons;
                var wt = item.WeaponType;
                item.SecondaryCategory = wt.GetNiceName();
            }
            else if (item.SecondaryCategory == XivStrings.Earring
                            || item.SecondaryCategory == XivStrings.Neck
                            || item.SecondaryCategory == XivStrings.Wrists
                            || item.SecondaryCategory == XivStrings.Rings)
            {
                item.PrimaryCategory = XivStrings.Accessories;
            }

            return item;
        }

        /// <summary>
        /// Creates a XivUI item from a database row.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        internal static XivUi MakeUi(CacheReader reader)
        {
            var item = new XivUi
            {
                PrimaryCategory = XivStrings.UI,
                SecondaryCategory = reader.GetString("category"),
                TertiaryCategory = reader.GetString("subcategory"),
                MapZoneCategory = reader.GetString("mapzonecategory"),
                Name = reader.GetString("name"),
                IconNumber = reader.GetInt32("icon_id"),
                UiPath = reader.GetString("path"),
            };
            return item;
        }

        /// <summary>
        /// Creates a XivFurniture item from a database row.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        internal static XivFurniture MakeFurniture(CacheReader reader)
        {
            var item = new XivFurniture
            {
                PrimaryCategory = XivStrings.Housing,
                SecondaryCategory = reader.GetString("category"),
                TertiaryCategory = reader.GetString("subcategory"),
                Name = reader.GetString("name"),
                IconId = (uint)reader.GetInt32("icon_id"),
                ModelInfo = new XivModelInfo()
                {
                    PrimaryID = reader.GetInt32("primary_id"),
                    SecondaryID = reader.GetInt32("secondary_id")
                }
            };
            return item;
        }

        public static async Task<List<IItem>> GetFullItemList()
        {
            var items = new List<IItem>();

            var gear = new Gear();
            var companions = new Companions();
            var housing = new Housing();
            var ui = new UI();
            var character = new Character();


            items.AddRange(await gear.GetGearList());
            items.AddRange(await character.GetCharacterList());
            items.AddRange(await companions.GetMinionList());
            items.AddRange(await companions.GetPetList());
            items.AddRange(await ui.GetUIList());
            items.AddRange(await housing.GetFurnitureList());
            items.AddRange(await companions.GetMountList(null, XivStrings.Mounts));
            items.AddRange(await companions.GetMountList(null, XivStrings.Ornaments));

            return items;
        }

        #endregion

        /// <summary>
        /// Sets a meta value to the cache.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public static void SetMetaValue(string key, string value)
        {
            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                db.BusyTimeout = 3000;
                db.Open();

                SetPragmas(db);

                var query = "insert into meta(key, value) values($key,$value) on conflict(key) do update set value = excluded.value";
                using (var cmd = new SQLiteCommand(query, db))
                {
                    cmd.Parameters.AddWithValue("key", key);
                    cmd.Parameters.AddWithValue("value", value);
                    cmd.ExecuteScalar();
                }
            }
        }
        public static void SetMetaValue(string key, bool value)
        {
            SetMetaValue(key, value ? "True" : "False");
        }
        public static bool GetMetaValueBoolean(string key)
        {
            var st = GetMetaValue(key);

            bool result = false;
            bool success = Boolean.TryParse(st, out result);

            if (!success) return false;
            return result;
        }

        /// <summary>
        /// Retrieves a meta value from the cache.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public static string GetMetaValue(string key)
        {
            string val = null;
            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                db.BusyTimeout = 3000;
                db.Open();

                var query = "select value from meta where key = $key";

                // Double Using statements are important here to ensure
                // that the SQLiteCommand and SQLiteConnection can be 
                // immediately GC'd, and not keep the file handle
                // open in case we want to destroy the DB File.
                using (var cmd = new SQLiteCommand(query, db))
                {
                    cmd.Parameters.AddWithValue("key", key);
                    try
                    {
                        val = (string)cmd.ExecuteScalar();

                    }
                    catch (Exception Ex)
                    {
                        throw;
                        // Meta Table doesn't exist.
                    }
                }

                // Can't hurt to explicitly close it.
                db.Close();
            }

            return val?.ToString();
        }


        /// <summary>
        /// Retreives the child files in the dependency graph for this file.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public static async Task<List<string>> GetChildFiles(string internalFilePath, ModTransaction tx = null)
        {
            var wc = new WhereClause() { Column = "parent", Comparer = WhereClause.ComparisonType.Equal, Value = internalFilePath };
            var list = BuildListFromTable("dependencies_children", wc, (reader) =>
            {
                return reader.GetString("child");
            });

            // Cache said this file has no children.
            if(list.Count == 1 && list[0] == null)
            {
                return new List<string>();
            }

            
            if (list.Count == 0 || list[0] == null)
            {
                // No cache data, have to update.
                list = await XivDependencyGraph.GetChildFiles(internalFilePath, tx);
                await UpdateChildFiles(internalFilePath, list, tx);
            }
            return list;
        }


        /// <summary>
        /// Retreives the parent files in the dependency graph for this file.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public static async Task<List<string>> GetParentFiles(string internalFilePath, ModTransaction tx = null)
        {
            var wc = new WhereClause() { Column = "child", Comparer = WhereClause.ComparisonType.Equal, Value = internalFilePath };
            var list = BuildListFromTable("dependencies_parents", wc, (reader) =>
            {
                return reader.GetString("parent");
            });

            // Cache said this file has no parents.
            if (list.Count == 1 && list[0] == null)
            {
                return new List<string>();
            }

            if (list.Count == 0)
            {
                // 0 Length list means there was no cached data.
                list = await XivDependencyGraph.GetParentFiles(internalFilePath, tx);
                await UpdateParentFiles(internalFilePath, list, tx);
            }
            return list;
        }

        /// <summary>
        /// Retrieves the entire universe of available roots from the database.
        /// If that doesn't exist, fallback pulls the entire list of roots from the item list cache.
        /// </summary>
        /// <returns></returns>
        public static List<XivDependencyRootInfo> GetAllRoots()
        {
            var allRoots = new List<XivDependencyRootInfo>(5000);

            try
            {
                using (var db = new SQLiteConnection(RootsCacheConnectionString))
                {
                    // Time to go root hunting.
                    var query = "select * from roots order by primary_type, primary_id, secondary_type, secondary_id";
                    db.BusyTimeout = 3000;
                    db.Open();


                    using (var cmd = new SQLiteCommand(query, db))
                    {
                        using (var reader = new CacheReader(cmd.ExecuteReader()))
                        {
                            while (reader.NextRow())
                            {
                                var root = new XivDependencyRootInfo();
                                var pTypeString = reader.GetString("primary_type");
                                root.PrimaryType = (XivItemType)Enum.Parse(typeof(XivItemType), pTypeString); ;
                                var sTypeString = reader.GetString("secondary_type");
                                if (!String.IsNullOrEmpty(sTypeString))
                                {
                                    root.SecondaryType = (XivItemType)Enum.Parse(typeof(XivItemType), sTypeString);
                                }
                                root.PrimaryId = reader.GetInt32("primary_id");
                                root.SecondaryId = reader.GetNullableInt32("secondary_id");
                                root.Slot = reader.GetString("slot");
                                allRoots.Add(root);
                            }
                        }
                    }
                }
            }
            catch
            {
                //NoOp.  Just fall through to the item list if the root cache is busted.
            }

            if(allRoots.Count == 0)
            {
                using (var db = new SQLiteConnection(CacheConnectionString))
                {
                    // Gotta do this for all the supporting types...
                    var query = "select root from items";
                    db.BusyTimeout = 3000;
                    db.Open();


                    using (var cmd = new SQLiteCommand(query, db))
                    {
                        using (var reader = new CacheReader(cmd.ExecuteReader()))
                        {
                            while (reader.NextRow())
                            {
                                var rootString = reader.GetString("root");
                                if (String.IsNullOrEmpty(rootString))
                                {
                                    continue;
                                }
                                allRoots.Add(XivDependencyGraph.ExtractRootInfo(rootString));
                            }
                        }
                    }

                    // Just monster and items for now.
                    query = "select root from monsters";

                    using (var cmd = new SQLiteCommand(query, db))
                    {
                        using (var reader = new CacheReader(cmd.ExecuteReader()))
                        {
                            while (reader.NextRow())
                            {
                                var rootString = reader.GetString("root");
                                if (String.IsNullOrEmpty(rootString))
                                {
                                    continue;
                                }
                                allRoots.Add(XivDependencyGraph.ExtractRootInfo(rootString));
                            }
                        }
                    }
                }
            }

            return allRoots;
        }


        /// <summary>
        /// Retrieves the entire universe of available, valid roots from the database.
        /// Returned in the compound dictionary format of [Primary Type] [Primary Id] [Secondary Type] [Secondary Id] [Slot]
        /// </summary>
        /// <returns></returns>
        public static Dictionary<XivItemType, Dictionary<int, Dictionary<XivItemType, Dictionary<int, Dictionary<string, XivDependencyRootInfo>>>>> GetAllRootsDictionary()
        {
            // Primary Type => Dictionary of Primary IDs
            // Primary IDs => Dictionary of Primary Secondary Types
            // Secondary Types => Dictionary of Secondary IDs 
            // Secondary ID => List of Slots
            // => Slot => [Root]

            // This monolith is keyed by
            // [PrimaryType] [PrimaryId] [SecondaryType] [SecondaryId] [Slot]
            var dict = new Dictionary<XivItemType, Dictionary<int, Dictionary<XivItemType, Dictionary<int, Dictionary<string, XivDependencyRootInfo>>>>>();
            var allRoots = GetAllRoots();

            foreach(var root in allRoots)
            {

                if (!dict.ContainsKey(root.PrimaryType))
                {
                    dict.Add(root.PrimaryType, new Dictionary<int, Dictionary<XivItemType, Dictionary<int, Dictionary<string, XivDependencyRootInfo>>>>());
                }
                var pTypeDict = dict[root.PrimaryType];

                if (!pTypeDict.ContainsKey(root.PrimaryId))
                {
                    pTypeDict.Add(root.PrimaryId, new Dictionary<XivItemType, Dictionary<int, Dictionary<string, XivDependencyRootInfo>>>());
                }

                var pIdDict = pTypeDict[root.PrimaryId];
                var safeSecondary = (XivItemType)(root.SecondaryType == null ? XivItemType.none : root.SecondaryType);
                if (!pIdDict.ContainsKey(safeSecondary))
                {
                    pIdDict.Add(safeSecondary, new Dictionary<int, Dictionary<string, XivDependencyRootInfo>>());
                }

                var sTypeDict = pIdDict[safeSecondary];
                var sId = (int)(root.SecondaryId != null ? root.SecondaryId : 0);
                if (!sTypeDict.ContainsKey(sId))
                {
                    sTypeDict.Add(sId, new Dictionary<string, XivDependencyRootInfo>());
                }
                var sIdDict = sTypeDict[sId];

                var safeSlot = root.Slot == null ? "" : root.Slot;
                sIdDict[safeSlot] = root;

            }


            return dict;


        }

        public static async Task<XivDependencyRoot> GetFirstRoot(string internalPath)
        {
            var roots = await XivDependencyGraph.GetDependencyRoots(internalPath, true);
            if(roots.Count > 0)
            {
                return roots[0];
            }
            return null;
        }

        /// <summary>
        /// Attempts to resolve the item's root purely by file path.
        /// Not always accurate, but fast/synchronous.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static XivDependencyRoot GetFilePathRoot(string path)
        {
            return XivDependencyGraph.CreateDependencyRoot(XivDependencyGraph.ExtractRootInfo(path));
        }

        /// <summary>
        /// Very fast, but possibly innacurate method for establishing an item's root information.
        /// Mostly useful for resolving information about materials or models quickly.
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public static XivDependencyRootInfo GetFileNameRootInfo(string fileName, bool validate = true)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            return XivDependencyGraph.ExtractRootInfoFilenameOnly(name, validate);
        }

        public static async Task<List<XivDependencyRoot>> GetRoots(string internalPath)
        {
            return await XivDependencyGraph.GetDependencyRoots(internalPath);
        }



        public static void ResetRootCache()
        {
            var cwd = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);

            WaitForSqlCleanup();
            File.Delete(_rootCachePath.FullName);

            using (var db = new SQLiteConnection(RootsCacheConnectionString))
            {
                db.BusyTimeout = 3000;
                db.Open();

                SetPragmas(db);

                var lines = File.ReadAllLines(Path.Combine(cwd, "Resources", "SQL", rootCacheCreationScript));
                var sqlCmd = String.Join("\n", lines);

                using (var cmd = new SQLiteCommand(sqlCmd, db))
                {
                    cmd.ExecuteScalar();
                }
            }
        }


        /// <summary>
        /// Saves a dependency root to the DB, for item list crawling.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="hash"></param>
        public static void CacheRoot(XivDependencyRootInfo root, SQLiteCommand cmd)
        {
            cmd.Parameters.AddWithValue("primary_type", root.PrimaryType.ToString());
            cmd.Parameters.AddWithValue("primary_id", root.PrimaryId);

            // The ifs help make sure these are recorded as null in the SQLite db and not emptystrings/0's
            if (root.SecondaryType != null)
            {
                cmd.Parameters.AddWithValue("secondary_type", root.SecondaryType.ToString());
                cmd.Parameters.AddWithValue("secondary_id", (int)root.SecondaryId);
            } else
            {
                cmd.Parameters.AddWithValue("secondary_type", null);
                cmd.Parameters.AddWithValue("secondary_id", null);

            }
            if (root.Slot != null)
            {
                cmd.Parameters.AddWithValue("slot", root.Slot);
            } else
            {
                cmd.Parameters.AddWithValue("slot", null);

            }
            cmd.Parameters.AddWithValue("root_path", root.ToString());
            try
            {
                cmd.ExecuteScalar();
            } catch (Exception ex) {
                Console.Write(ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Retrieves all the parent files for all the files in the modlist.
        /// Note - This operates off the cached data and assumes/requires that the cache worker
        /// has finished with its operations.
        /// </summary>
        /// <param name="files"></param>
        /// <param name="cachedOnly"></param>
        /// <returns></returns>
        public static async Task<Dictionary<string, List<string>>> GetModListParents(ModTransaction tx = null)
        {
            if(tx == null)
            {
                tx = ModTransaction.BeginReadonlyTransaction();
            }

            var modList = await tx.GetModList();
            var dict = new Dictionary<string, List<string>>(modList.Mods.Count);
            if(modList.Mods.Count == 0)
            {
                return dict;
            }

            bool anyValidMods = false;

            var query = "select child, parent from dependencies_parents where child in(";
            var files = modList.Mods.Keys.ToList();
            foreach(var file in files)
            {

                anyValidMods = true;
                query += "'" + file + "',";
            }


            // They had mod entries, but all of them are empty.
            if(!anyValidMods)
            {
                return dict;
            }

            query = query.Substring(0, query.Length - 1);
            query += ") order by child";

            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                db.BusyTimeout = 3000;
                db.Open();


                using (var cmd = new SQLiteCommand(query, db))
                {
                    using (var reader = new CacheReader(cmd.ExecuteReader()))
                    {
                        while (reader.NextRow())
                        {
                            var child = reader.GetString("child");
                            var parent = reader.GetString("parent");

                            if(parent != null)
                            {
                                if(!dict.ContainsKey(child))
                                {
                                    dict.Add(child, new List<string>());
                                }

                                dict[child].Add(parent);
                            }
                        }
                    }
                }
            }

            return dict;
        }

        /// <summary>
        /// Retreives the sibling files in the dependency graph for this file.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public static async Task<List<string>> GetSiblingFiles(string internalFilePath, ModTransaction tx = null)
        {
            return await XivDependencyGraph.GetSiblingFiles(internalFilePath, tx);
        }

        /// <summary>
        /// Retrieves the dependency roots for the given file.
        /// 
        /// For everything other than texture files, this will always be,
        /// A list of length 1 (valid), or 0 (not in the dependency tree)
        /// For textures this can be more than 1.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public static async Task<List<XivDependencyRoot>> GetDependencyRoots(string internalFilePath)
        {
            var roots = await XivDependencyGraph.GetDependencyRoots(internalFilePath);
            return roots;
        }

        /// <summary>
        /// Re-scans the *ENTIRE* list of possible roots in the 040000 DAT file.
        /// This operation takes a LONG time, and should not be done unless specifically
        /// user initiated.
        /// </summary>
        /// <returns></returns>
        public static async Task RebuildAllRoots()
        {
            await XivDependencyGraph.CacheAllRealRoots();
        }

        /// <summary>
        /// Pretty basic recursive function that returns a hashset of all the child files,
        /// including the original caller.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public static async Task<HashSet<string>> GetChildrenRecursive(string file, ModTransaction tx = null)
        {
            var files = new HashSet<string>();
            files.Add(file);

            var baseChildren = await XivCache.GetChildFiles(file, tx);
            if (baseChildren == null || baseChildren.Count == 0)
            {
                // No children, just us.
                return files;
            }
            else
            {
                // We have child files.
                foreach (var child in baseChildren)
                {
                    // Recursively get their children.
                    var children = await GetChildrenRecursive(child, tx);
                    foreach (var subchild in children)
                    {
                        // Add the results to the list.
                        files.Add(subchild);
                    }
                }
            }
            return files;
        }



        /// <summary>
        /// Updates the file children in the dependencies cache.
        /// Returns the children that were written to the DB.
        /// </summary>
        /// <param name="internalFilePath"></param>
        public static async Task<List<string>> UpdateChildFiles(string internalFilePath, List<string> children = null, ModTransaction tx = null)
        {
            var level = XivDependencyGraph.GetDependencyLevel(internalFilePath);
            if (level == XivDependencyLevel.Invalid || level == XivDependencyLevel.Texture)
            {
                return new List<string>();
            }

            // Just updating a single file.
            if (children == null)
            {
                children = await XivDependencyGraph.GetChildFiles(internalFilePath, tx);
            }

            var oldCacheChildren = new List<string>();

            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                db.BusyTimeout = 3000;
                db.Open();


                using (var transaction = db.BeginTransaction())
                {
                    // Clear out our old children.
                    var query = "delete from dependencies_children where parent = $parent";
                    using (var cmd = new SQLiteCommand(query, db))
                    {
                        cmd.Parameters.AddWithValue("parent", internalFilePath);
                        cmd.ExecuteScalar();
                    }

                    // Find all the files that currently point to us as a parent in the cache.
                    query = "select child from dependencies_parents where parent = $parent";
                    using (var cmd = new SQLiteCommand(query, db))
                    {
                        cmd.Parameters.AddWithValue("parent", internalFilePath);
                        using (var reader = new CacheReader(cmd.ExecuteReader()))
                        {
                            while (reader.NextRow())
                            {
                                oldCacheChildren.Add(reader.GetString("child"));
                            }
                        }
                    }

                    // And purge all of those cached file's parents (we'll queue them up for recalculation after the transaction is done)
                    query = "delete from dependencies_parents where child in (select child as c_file from dependencies_parents where parent = $parent)";
                    using (var cmd = new SQLiteCommand(query, db))
                    {
                        cmd.Parameters.AddWithValue("parent", internalFilePath);
                        cmd.ExecuteScalar();
                    }

                    if (children == null || children.Count == 0)
                    {
                        // Null indicator says we updated the data, but there were no parents.
                        children = new List<string> { null };
                    }

                    // Insert our new data.
                    query = "insert into dependencies_children (parent, child) values ($parent, $child)";
                    using (var cmd = new SQLiteCommand(query, db))
                    {
                        foreach (var child in children)
                        {
                            cmd.Parameters.AddWithValue("parent", internalFilePath);
                            cmd.Parameters.AddWithValue("child", child);
                            try
                            {
                                cmd.ExecuteScalar();
                            }
                            catch (Exception ex)
                            {
                                throw;
                            }
                        }
                    }

                    // And for all the new children, purge their old parent information, as it is no longer valid.
                    query = "delete from dependencies_parents where child in (select child as c_file from dependencies_children where parent = $parent)";
                    using (var cmd = new SQLiteCommand(query, db))
                    {
                        cmd.Parameters.AddWithValue("parent", internalFilePath);
                        cmd.ExecuteScalar();
                    }

                    var toUpdate = new HashSet<string>();
                    foreach (var file in oldCacheChildren)
                    {
                        toUpdate.Add(file);
                    }
                    foreach (var file in children)
                    {
                        toUpdate.Add(file);
                    }

                    // Queue up all the files that we parent-purged into the parent queue.
                    foreach (var c in toUpdate)
                    {
                        if (c == null) continue;

                        query = "insert into  dependencies_parents_queue (file) values ($file) on conflict do nothing";
                        using (var insertCmd = new SQLiteCommand(query, db))
                        {
                            insertCmd.Parameters.AddWithValue("file", c);
                            insertCmd.ExecuteScalar();
                        }
                    }


                    transaction.Commit();
                }
            }

            return children;
        }

        /// <summary>
        /// Updates the file parents in the dependencies cache.
        /// </summary>
        /// <param name="internalFilePath"></param>
        private static async Task UpdateParentFiles(string internalFilePath, List<string> parents = null, ModTransaction tx = null)
        {
            var level = XivDependencyGraph.GetDependencyLevel(internalFilePath);
            if (level == XivDependencyLevel.Invalid || level == XivDependencyLevel.Root)
            {
                return;
            }

            // Just updating a single file.
            if (parents == null)
            {
                parents = await XivDependencyGraph.GetParentFiles(internalFilePath, tx);
            }

            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                db.BusyTimeout = 3000;
                db.Open();


                using (var transaction = db.BeginTransaction())
                {
                    // Clear out all old parents
                    var query = "delete from dependencies_parents where child = $child";
                    using (var cmd = new SQLiteCommand(query, db))
                    {
                        cmd.Parameters.AddWithValue("child", internalFilePath);
                        cmd.ExecuteScalar();
                    }

                    if (parents == null || parents.Count == 0)
                    {
                        // Null indicator says we updated the data, but there were no parents.
                        parents = new List<string> { null };
                    }


                    query = "insert into dependencies_parents (parent, child) values ($parent, $child)";
                    using (var cmd = new SQLiteCommand(query, db))
                    {
                        foreach (var parent in parents)
                        {
                            cmd.Parameters.AddWithValue("parent", parent);
                            cmd.Parameters.AddWithValue("child", internalFilePath);
                            try
                            {
                                cmd.ExecuteScalar();
                            }
                            catch (Exception ex)
                            {
                                throw;
                            }
                        }
                    }
                    transaction.Commit();
                }
            }
        }

        public static void WaitForSqlCleanup()
        {
            SQLiteConnection.ClearAllPools();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>
        /// Queues dependency information pre-calculation for the given file(s).
        /// </summary>
        /// <param name="files"></param>
        /// <returns></returns>
        public static void QueueDependencyUpdate(string file)
        {
            QueueDependencyUpdate(new List<string>() { file });
        }

        /// <summary>
        /// Queues dependency information pre-calculation for the given file(s).
        /// </summary>
        /// <param name="files"></param>
        /// <returns></returns>
        public static void QueueDependencyUpdate(IEnumerable<string> files) {

            // We need to...
            // 1. Purge and requeue the parent calculation for all files we had listed as children previously.
            // 2. Purge our own child file references.
            // 3. Add this file to both dependency queues.
            // 4. Add our affected children to the parent queue.
            // 5. Add this root to the items sets file if it didn't already exist.

            try
            {
                using (var db = new SQLiteConnection(CacheConnectionString))
                {
                    db.BusyTimeout = 3000;
                    db.Open();

                    using (var transaction = db.BeginTransaction())
                    {
                        foreach (var file in files)
                        {

                            var oldCacheChildren = new List<string>();

                            // Clear out our old children.
                            var query = "delete from dependencies_children where parent = $parent";
                            using (var cmd = new SQLiteCommand(query, db))
                            {
                                cmd.Parameters.AddWithValue("parent", file);
                                cmd.ExecuteScalar();
                            }

                            // Find all the files that currently point to us as a parent in the cache.
                            query = "select child from dependencies_parents where parent = $parent";
                            using (var cmd = new SQLiteCommand(query, db))
                            {
                                cmd.Parameters.AddWithValue("parent", file);
                                using (var reader = new CacheReader(cmd.ExecuteReader()))
                                {
                                    while (reader.NextRow())
                                    {
                                        oldCacheChildren.Add(reader.GetString("child"));
                                    }
                                }
                            }

                            // And purge all of those cached file's parents.
                            query = "delete from dependencies_parents where child in (select child as c_file from dependencies_parents where parent = $parent)";
                            using (var cmd = new SQLiteCommand(query, db))
                            {
                                cmd.Parameters.AddWithValue("parent", file);
                                cmd.ExecuteScalar();
                            }

                            // Now add us to both queues.
                            query = "insert into dependencies_children_queue (file) values ($file) on conflict do nothing";
                            using (var insertCmd = new SQLiteCommand(query, db))
                            {
                                insertCmd.Parameters.AddWithValue("file", file);
                                insertCmd.ExecuteScalar();
                            }

                            query = "insert into dependencies_parents_queue (file) values ($file) on conflict do nothing";
                            using (var insertCmd = new SQLiteCommand(query, db))
                            {
                                insertCmd.Parameters.AddWithValue("file", file);
                                insertCmd.ExecuteScalar();
                            }

                            // Queue up all the files that we parent-purged into the parent queue.
                            foreach (var c in oldCacheChildren)
                            {
                                if (c == null) continue;

                                query = "insert into dependencies_parents_queue (file) values ($file) on conflict do nothing";
                                using (var insertCmd = new SQLiteCommand(query, db))
                                {
                                    insertCmd.Parameters.AddWithValue("file", c);
                                    insertCmd.ExecuteScalar();
                                }
                            }

                        }
                        transaction.Commit();
                    }

                    // Couldn't hurt.
                    db.Close();
                }

                try
                {
                    // Now connect to the root cache and inject our roots.
                    using (var db = new SQLiteConnection(RootsCacheConnectionString))
                    {
                        db.BusyTimeout = 3000;
                        db.Open();


                        using (var transaction = db.BeginTransaction())
                        {
                            HashSet<XivDependencyRootInfo> roots = new HashSet<XivDependencyRootInfo>();
                            var query = "insert into roots (primary_type, primary_id, secondary_type, secondary_id, slot, root_path) values ($primary_type, $primary_id, $secondary_type, $secondary_id, $slot, $root_path) on conflict do nothing;";
                            using (var cmd = new SQLiteCommand(query, db))
                            {
                                foreach (var file in files)
                                {
                                    var root = XivDependencyGraph.ExtractRootInfo(file);
                                    if (root == null || root.PrimaryId < 0)
                                    {
                                        continue;
                                    }
                                    if (roots.Contains(root))
                                        continue;

                                    var fullRoot = XivDependencyGraph.CreateDependencyRoot(root);
                                    if (fullRoot == null)
                                        continue;

                                    roots.Add(root);
                                    XivCache.CacheRoot(root, cmd);
                                }
                            }
                            transaction.Commit();
                        }
                        db.Close();
                    }
                } catch(Exception ex)
                {
                    // This is a non-critical error.
                    Trace.Write(ex);
                }

            } catch(Exception ex)
            {
                throw new CacheException(ex);
            }
        }

        private static string PopChildQueue()
        {
            string file = null;
            int position = -1;
            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                db.BusyTimeout = 3000;
                db.Open();


                var query = "select position, file from dependencies_children_queue";
                using (var selectCmd = new SQLiteCommand(query, db))
                {
                    using (var reader = new CacheReader(selectCmd.ExecuteReader()))
                    {
                        if (!reader.NextRow())
                        {
                            // No entries left.
                            return null;
                        }
                        // Got the new item.
                        file = reader.GetString("file");
                        position = reader.GetInt32("position");
                    }
                }
                db.Close();
            }
            return file;
        }

        public static void RemoveFromChildQueue(string file)
        {
            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                db.BusyTimeout = 3000;
                db.Open();

                // Delete the row we took and all others that match the filename.
                var query = "delete from dependencies_children_queue where file = $file";
                using (var deleteCmd = new SQLiteCommand(query, db))
                {
                    deleteCmd.Parameters.AddWithValue("file", file);
                    deleteCmd.ExecuteScalar();
                }
                db.Close();
            }
        }

        private static string PopParentQueue()
        {
            string file = null;
            int position = -1;
            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                db.BusyTimeout = 3000;
                db.Open();


                var query = "select position, file from dependencies_parents_queue";
                using (var selectCmd = new SQLiteCommand(query, db))
                {
                    using (var reader = new CacheReader(selectCmd.ExecuteReader()))
                    {
                        if (!reader.NextRow())
                        {
                            // No entries left.
                            return null;
                        }
                        // Got the new item.
                        file = reader.GetString("file");
                        position = reader.GetInt32("position");
                    }
                }
                db.Close();
            }
            return file;
        }
        public static void RemoveFromParentQueue(string file)
        {
            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                db.BusyTimeout = 3000;
                db.Open();


                // Delete the row we took and all others that match the filename.
                var query = "delete from dependencies_parents_queue where file = $file";
                using (var deleteCmd = new SQLiteCommand(query, db))
                {
                    deleteCmd.Parameters.AddWithValue("file", file);
                    deleteCmd.ExecuteScalar();
                }
                db.Close();
            }
        }


        /// <summary>
        /// This function is a long-running thread function which operates alongside the main
        /// system.  It pops items off the dependency queue to pre-process their dependency information,
        /// so that accessing it later isn't an incredibly taxing operation.  If this thread dies, 
        /// or is disabled, all functionally will/must continue to operate - however, calls to
        /// GetParentFiles()/GetChildFiles() may often be significantly slower, as the data may not already be cached.
        /// </summary>
        private static async void ProcessDependencyQueue(object sender, DoWorkEventArgs e)
        {
            // Note: The Cache worker only runs when Transactions are not active.

            Trace.WriteLine("Starting Cache Worker on thread: " + Thread.CurrentThread.ManagedThreadId);

            // This will be executed on another thread.
            BackgroundWorker worker = (BackgroundWorker)sender;
            _CacheWorkerStartupComplete = true;
            while (!worker.CancellationPending)
            {
                var file = "";
                XivDependencyLevel level;
                try
                {
                    // Children queue always comes first.  This information is more important and efficient.
                    file = PopChildQueue();
                    if (file != null)
                    {
                        level = XivDependencyGraph.GetDependencyLevel(file);
                        if (level == XivDependencyLevel.Invalid)
                        {
                            RemoveFromChildQueue(file);
                            continue;
                        }

                        try
                        {
                            // The get call will automatically cache the data, if it needs updating.

                            // Use a temporary readonly TX for perf.
                            var tx = ModTransaction.BeginReadonlyTransaction();
                            await GetChildFiles(file, tx);
                        }
                        finally
                        {
                            RemoveFromChildQueue(file);
                        }
                        continue;

                    }
                    else
                    {

                        // Child queue is empty, we can efficiently process the parent's queue.
                        file = PopParentQueue();
                        if (file == null)
                        {
                            // Nothing in queue.  Time for nap.
                            //worker.CancelAsync();
                            Thread.Sleep(1000);
                            continue;
                        }
                        else
                        {
                            level = XivDependencyGraph.GetDependencyLevel(file);
                            if (level == XivDependencyLevel.Invalid)
                            {
                                RemoveFromParentQueue(file);
                                continue;
                            }

                            try
                            {
                                // Use a temporary readonly TX for perf.
                                var tx = ModTransaction.BeginReadonlyTransaction();

                                // The get call will automatically cache the data, if it needs updating.
                                await GetParentFiles(file, tx);
                            } finally
                            {
                                RemoveFromParentQueue(file);
                            }
                            continue;
                        }
                    }
                } catch( Exception ex)
                {
                    var a = "b";
                    //throw;
                    // No-op;
                }
            }

            Trace.WriteLine("Stopping Cache Worker on thread: " + Thread.CurrentThread.ManagedThreadId);

            // Ensure we're good and clean up after ourselves.
            WaitForSqlCleanup();
            // But the SQlite library sometimes hangs indefinitely if you call it.
            // So instead...

            bool accessFailed = true;
            while (accessFailed)
            {

                try
                {
                    WaitForSqlCleanup();

                    var fs = File.OpenWrite(_dbPath.FullName);
                    fs.Dispose();
                    accessFailed = false;
                }
                catch
                {
                    await Task.Delay(50);
                }
            }
            _cacheWorker = null;

        }


        /// <summary>
        /// Internal function used in generating the cached parent data.
        /// This checks the child cache to help see if any orphaned modded items
        /// may reference this file still.  Ex. An unused Material
        /// referencing an unused Texture File.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        internal static async Task<List<string>> GetCacheParents(string internalFilePath)
        {
            
            var wc = new WhereClause() { Column = "child", Comparer = WhereClause.ComparisonType.Equal, Value = internalFilePath };
            List<string> parents = BuildListFromTable("dependencies_children", wc, (reader) =>
            {
                return reader.GetString("parent");
            });

            // It's possible this file is a DX9 => 11 conversion texture, in which case we also have to look for references to our
            // DX9 version.
            var dx11Name = internalFilePath.Replace("--", "");
            if (dx11Name != internalFilePath)
            {
                wc = new WhereClause() { Column = "child", Comparer = WhereClause.ComparisonType.Equal, Value = dx11Name };
                List<string> dxParents = BuildListFromTable("dependencies_children", wc, (reader) =>
                {
                    return reader.GetString("parent");
                });

                parents.AddRange(dxParents);
            };
            return parents;

        }

        /// <summary>
        /// Gets the current length of the dependency processing queue.
        /// </summary>
        /// <returns></returns>
        public static async Task<int> GetDependencyQueueLength()
        {
            int length = 0;
            await Task.Run((Func<Task>)(async () =>
            {
                using (var db = new SQLiteConnection((string)XivCache.CacheConnectionString))
                {
                    db.BusyTimeout = 3000;
                    db.Open();


                    var query = "select count(file) as cnt from dependencies_parents_queue";
                    using (var selectCmd = new SQLiteCommand(query, db))
                    {
                        using (var reader = new CacheReader(selectCmd.ExecuteReader()))
                        {
                            reader.NextRow();
                            // Got the new item.
                            length = reader.GetInt32("cnt");
                        }
                    }

                    query = "select count(file) as cnt from dependencies_children_queue";
                    using (var selectCmd = new SQLiteCommand(query, db))
                    {
                        using (var reader = new CacheReader(selectCmd.ExecuteReader()))
                        {
                            reader.NextRow();
                            // Got the new item.
                            length += reader.GetInt32("cnt");
                        }
                    }
                }
            }));
            return length;
        }

        private static List<T> BuildListFromTable<T>(string table, WhereClause where, Func<CacheReader, T> func)
        {
            return BuildListFromTable<T>(CacheConnectionString, table, where, func);
        }

        /// <summary>
        /// Creates a list from the data entries in a cache table, using the given where clause and predicate.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="table"></param>
        /// <param name="where"></param>
        /// <param name="func"></param>
        /// <returns></returns>
        public static List<T> BuildListFromTable<T>(string connectionString, string table, WhereClause where, Func<CacheReader, T> func)
        {

            List<T> list;
            using (var db = new SQLiteConnection(connectionString))
            {
                db.BusyTimeout = 3000;
                db.Open();


                list = BuildListFromTable<T>(db, table, where, func);
                db.Close();
            }
            return list;
        }

        public static List<T> BuildListFromTable<T>(SQLiteConnection db, string table, WhereClause where, Func<CacheReader, T> func)
        {

            List<T> list = new List<T>();

            // Check how large the result set will be so we're not constantly
            // Reallocating the array.
            var query = "select count(*) from " + table + " ";
            if (where != null)
            {
                query += where.GetSql();
            }

            using (var cmd = new SQLiteCommand(query, db))
            {
                if (where != null)
                {
                    where.AddParameters(cmd);
                }

                int val = (int)((long) cmd.ExecuteScalar());
                list = new List<T>(val);
            }

            // Set up the actual full query.
            query = "select * from " + table;
            if (where != null)
            {
                query += where.GetSql();
            }

            using (var cmd = new SQLiteCommand(query, db))
            {
                if (where != null)
                {
                    where.AddParameters(cmd);
                }

                using (var reader = new CacheReader(cmd.ExecuteReader()))
                {
                    while (reader.NextRow())
                    {
                        try
                        {
                            list.Add(func(reader));
                        }
                        catch (Exception ex)
                        {
                            throw;
                        }
                    }
                }
            }
            return list;
        }


        /// <summary>
        /// Class for composing SQL Where clauses programatically.
        /// A [WhereClause] with any [Inner] entries is considered
        /// a parenthetical group and has its own Column/Value/Comparer ignored.
        /// </summary>
        public class WhereClause
        {
            public enum ComparisonType
            {
                Equal,
                NotEqual,
                Like
            }
            public enum JoinType
            {
                And,
                Or
            }

            public List<WhereClause> Inner;

            public string Column;
            public object Value;
            public ComparisonType Comparer = ComparisonType.Equal;
            public JoinType Join = JoinType.And;

            public WhereClause()
            {
                Inner = new List<WhereClause>();
            }


            /// <summary>
            /// Generates the body of the Where clause, without the starting ' where ';
            /// </summary>
            /// <param name="includeWhere">If literal word ' where ' should be included.</param>
            /// <param name="skipJoin">If the [and/or] should be skipped.</param>
            /// <returns></returns>
            public string GetSql(bool includeWhere = true, bool skipJoin = true)
            {
                // No clause if no valid value.
                if((Inner == null || Inner.Count == 0) && (Column == null || Column == ""))
                {
                    return "";
                }

                var result = "";
                if(includeWhere)
                {
                    result += " where ";
                }

                // If we're a parenthetical group
                if (Inner != null && Inner.Count > 0)
                {
                    var first = true;

                    if (skipJoin)
                    {
                        result += " ( ";
                    } else
                    {
                        result += " " + Join.ToString().ToLower() + " ( ";
                    }

                    foreach(var where in Inner)
                    {
                        if(first)
                        {
                            // First item in a parenthetical group or top level has its
                            // Join ignored - it is implicitly [AND] logically.
                            result += where.GetSql(false, true);
                            first = false;
                        } else
                        {
                            result += where.GetSql(false, false);
                        }
                        

                    }
                    result += " ) ";

                }
                else
                {
                    // We're a standard single term where clause
                    if (!skipJoin)
                    {
                        // [AND/OR]
                        result += " " + Join.ToString().ToLower() + " ";
                    }

                    // [AND/OR] [COLUMN]
                    result += " " + Column + " ";


                    // [AND/OR] [COLUMN] [=/LIKE]
                    if (Comparer == ComparisonType.Equal)
                    {
                        result += " = ";
                    }
                    else if(Comparer == ComparisonType.NotEqual)
                    {
                        result += " != ";
                    } else
                    {
                        result += " like ";
                    }
                    // [AND/OR] [COLUMN] [=/LIKE] [$COLUMN]
                    result += " $" + Column + " ";
                }

                return result;
            }

            public void AddParameters(SQLiteCommand cmd)
            {
                if (Inner != null && Inner.Count > 0)
                {
                    foreach (var where in Inner)
                    {
                        where.AddParameters(cmd);
                    }

                }
                else
                {
                    if (Column != null && Column != "")
                    {
                        cmd.Parameters.AddWithValue(Column, Value);
                    }
                }
            }
        }

        /// <summary>
        /// A thin wrapper for the SQLiteDataReader class that
        /// helps with string column accessors, NULL value coalescence, and 
        /// ensures the underlying reader is properly closed and destroyed to help
        /// avoid lingering file handles.
        /// </summary>
        public class CacheReader : IDisposable
        {
            private SQLiteDataReader _reader;
            private Dictionary<string, int> _headers;
            private static readonly Type NullType = typeof(DBNull);

            /// <summary>
            /// Returns ther raw SQLiteDataReader object.
            /// </summary>
            public SQLiteDataReader Raw
            {
                get
                {
                    return _reader;
                }
            }

            /// <summary>
            /// Column names/keys.
            /// </summary>
            public Dictionary <string, int> Columns
            {
                get
                {
                    return _headers;
                }
            }

            public CacheReader(SQLiteDataReader reader)
            {
                _reader = reader;
                _headers = new Dictionary<string, int>();

                // Immediately get and cache the headers.
                for(var idx = 0; idx < _reader.FieldCount; idx++)
                {
                    _headers.Add(_reader.GetName(idx), idx);
                }
            }

            public byte GetByte(string fieldName)
            {
                if (_reader[_headers[fieldName]].GetType() == NullType)
                {
                    return 0;
                }
                return _reader.GetByte(_headers[fieldName]);
            }

            public float GetFloat(string fieldName)
            {
                if (_reader[_headers[fieldName]].GetType() == NullType)
                {
                    return 0f;
                }
                return _reader.GetFloat(_headers[fieldName]);
            }

            public int GetInt32(string fieldName)
            {
                if(_reader[_headers[fieldName]].GetType() == NullType)
                {
                    return 0;
                }
                return _reader.GetInt32(_headers[fieldName]);
            }
            public long GetInt64(string fieldName)
            {
                if (_reader[_headers[fieldName]].GetType() == NullType)
                {
                    return 0;
                }
                return _reader.GetInt64(_headers[fieldName]);
            }
            public int? GetNullableInt32(string fieldName)
            {
                if (_reader[_headers[fieldName]].GetType() == NullType)
                {
                    return null;
                }
                return _reader.GetInt32(_headers[fieldName]);
            }
            public string GetString(string fieldName)
            {
                if (_reader[_headers[fieldName]].GetType() == NullType)
                {
                    return null;
                }
                return _reader.GetString(_headers[fieldName]);
            }
            public bool GetBoolean(string fieldName)
            {
                if (_reader[_headers[fieldName]].GetType() == NullType)
                {
                    return false;
                }
                return _reader.GetBoolean(_headers[fieldName]);
            }

            /// <summary>
            /// Moves forward to the next row.
            /// </summary>
            /// <returns></returns>
            public bool NextRow()
            {
                return _reader.Read();
            }

            public void Dispose()
            {
                if (_reader != null && !_reader.IsClosed)
                {
                    _reader.Close();
                }
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            private bool disposed = false;

            // Dispose(bool disposing) executes in two distinct scenarios.
            // If disposing equals true, the method has been called directly
            // or indirectly by a user's code. Managed and unmanaged resources
            // can be disposed.
            // If disposing equals false, the method has been called by the
            // runtime from inside the finalizer and you should not reference
            // other objects. Only unmanaged resources can be disposed.
            protected virtual void Dispose(bool disposing)
            {
                // Check to see if Dispose has already been called.
                if (!this.disposed)
                {
                    if(_reader != null && _reader.IsClosed)
                    {
                        _reader.Close();
                    }

                    // Note disposing has been done.
                    disposed = true;
                }
            }

            ~CacheReader()
            {
                Dispose(false);
            }
        }

    }
}
