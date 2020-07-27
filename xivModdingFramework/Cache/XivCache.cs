using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using xivModdingFramework.Exd.FileTypes;
using xivModdingFramework.General;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Items;
using xivModdingFramework.Items.Categories;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Resources;

namespace xivModdingFramework.Cache
{

    /// <summary>
    /// Item Dependency Cache for keeping track of item dependency information.
    /// </summary>
    public static class XivCache
    {
        private static GameInfo _gameInfo;
        private static DirectoryInfo _dbPath;
        private static DirectoryInfo _rootCachePath;
        public static readonly Version CacheVersion = new Version("0.0.1.1");
        private const string dbFileName = "mod_cache.db";
        private const string rootCacheFileName = "roots_cache.db";
        private const string creationScript = "CreateCacheDB.sql";
        private const string rootCacheCreationScript = "CreateRootCacheDB.sql";
        public static string CacheConnectionString { get
            {
                return "Data Source=" + _dbPath + ";Pooling=True;Max Pool Size=100;";
            }
        }
        public static string RootsCacheConnectionString
        {
            get
            {
                return "Data Source=" + _rootCachePath + ";Pooling=True;Max Pool Size=100;";
            }
        }

        private static XivDependencyGraph Dependencies;

        // Safety check to make sure we don't redundantly attempt to rebuild the cache.
        private static bool _REBUILDING = false;
        public static GameInfo GameInfo
        {
            get
            {
                return _gameInfo;
            }
        }

        public static bool CacheWorkerEnabled
        {
            get
            {
                return _cacheWorker != null;
            }
            set
            {
                // State cannot be changed during a rebuild.
                // This shouldn't normally ever occur anyways unless some
                // parent application is multi-threading cache initializations
                // while a rebuild is happening.
                if (_REBUILDING) return;

                if (value  && _cacheWorker == null)
                {

                    BackgroundWorker _cacheWorker = new BackgroundWorker
                    {
                        WorkerReportsProgress = true,
                        WorkerSupportsCancellation = true
                    };
                    _cacheWorker.DoWork += ProcessDependencyQueue;
                    _cacheWorker.RunWorkerAsync();
                }
                else if(value == false && _cacheWorker != null) 
                {
                    // Sleep until the cache worker actually stops.
                    _cacheWorker.CancelAsync();
                    while(_cacheWorker != null)
                    {
                        Thread.Sleep(10);
                    }
                }
            }
        }


        private static BackgroundWorker _cacheWorker;

        /// <summary>
        /// Language is not actually required for Cache -reading-, only for cache generation, so it is 
        /// technically an optional parameter if you know you're just reading cache data.
        /// </summary>
        /// <param name="gameDirectory"></param>
        /// <param name="language"></param>
        /// <param name="validateCache"></param>
        public static void SetGameInfo(DirectoryInfo gameDirectory = null, XivLanguage language = XivLanguage.None, bool validateCache = true)
        {
            var gi = new GameInfo(gameDirectory, language);
            SetGameInfo(gi);
        }
        public static void SetGameInfo(GameInfo gameInfo = null)
        {
            // We need to either have a valid game directory reference in the static already or need one in the constructor.
            if (_gameInfo == null && (gameInfo == null || gameInfo.GameDirectory == null)) {
                throw new Exception("First call to cache must include a valid game directoy.");
            }

            // Sleep and lock this thread until rebuild is done.
            while (_REBUILDING)
            {
                Thread.Sleep(10);
            }

            if (_gameInfo == null)
            {
                _gameInfo = gameInfo;
            }


            bool restartCacheWorker = false;
            if(gameInfo.GameDirectory != _gameInfo.GameDirectory)
            {
                // Have to restart cache worker if we're changing directories.
                CacheWorkerEnabled = false;
                restartCacheWorker = true;
            }

            _dbPath = new DirectoryInfo(Path.Combine(_gameInfo.GameDirectory.Parent.Parent.FullName, dbFileName));
            _rootCachePath = new DirectoryInfo(Path.Combine(_gameInfo.GameDirectory.Parent.Parent.FullName, rootCacheFileName));

            Dependencies = new XivDependencyGraph();
            if (!_REBUILDING)
            {

                if (CacheNeedsRebuild() && !_REBUILDING)
                {
                    _REBUILDING = true;
                    RebuildCache();
                    _REBUILDING = false;
                }
            }

            CacheWorkerEnabled = true;

            // This just triggers the cache worker to start if it's not already.
            CacheWorkerEnabled = CacheWorkerEnabled || restartCacheWorker;

        }

        /// <summary>
        /// Tests if the cache needs to be rebuilt (and starts the process if it does.)
        /// </summary>
        private static bool CacheNeedsRebuild()
        {
            Func<bool> checkValidation = () =>
            {
                try
                {
                    // Cache structure updated?
                    var val = GetMetaValue("cache_version");
                    var version = new Version(val);
                    if (version != CacheVersion)
                    {
                        return true;
                    }

                    // FFXIV Updated?
                    val = GetMetaValue("ffxiv_version");
                    version = new Version(val);
                    if (version != _gameInfo.GameVersion)
                    {
                        return true;
                    }

                    if (_gameInfo.GameLanguage != XivLanguage.None)
                    {
                        // If user changed languages, we need to rebuild, too.
                        val = GetMetaValue("language");
                        if (val != _gameInfo.GameLanguage.ToString())
                        {
                            return true;
                        }
                    }

                    // Forced rebuild from a failed rebuild before restart.
                    val = GetMetaValue("needs_rebuild");
                    if (val != null)
                    {
                        return true;
                    }

                    return false;
                }
                catch (Exception Ex)
                {
                    return true;
                }
            };

            var result = checkValidation();
            if (result)
            {
                // Ensure we cleaned up after ourselves
                // in preprartion for calling rebuild.
                // Needs to be done in -this- thread before
                // Rebuild is Asynchronously called.
                SQLiteConnection.ClearAllPools();
                GC.WaitForPendingFinalizers();
            }
            return result;
        }

        /// <summary>
        /// Destroys and rebuilds the cache.
        /// Function is intentionally synchronous to
        /// help ensure it's never accidentally called
        /// without an await.
        /// </summary>
        public static void RebuildCache()
        {

            Task.Run(async () =>
            {
                if (_gameInfo.GameLanguage == XivLanguage.None)
                {
                    throw new NotSupportedException("A valid language must be specified when rebuilding the Cache.");
                }

                try
                {
                    CreateCache();

                    await RebuildItemsCache();
                    await RebuildMonstersCache();
                    await RebuildUiCache();
                    await RebuildFurnitureCache();
                    var pre = (Int32)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                    await BuildModdedItemDependencies();
                    var post = (Int32)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;

                    var result = post - pre;

                    SetMetaValue("cache_version", CacheVersion.ToString());
                    SetMetaValue("ffxiv_version", _gameInfo.GameVersion.ToString());
                    SetMetaValue("language", _gameInfo.GameLanguage.ToString());

                } catch (Exception Ex)
                {
                    SetMetaValue("needs_rebuild", "1");
                    throw;
                }
            }).Wait();
        }


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
            // That data is considere inviolate, and should never be changed
            // unless the user specifically requests to rebuild it, or
            // manually replaces the roots DB.  (It takes an hour or more to build)
            SQLiteConnection.ClearAllPools();
            GC.WaitForPendingFinalizers();
            File.Delete(_dbPath.FullName);

            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                db.Open();
                var lines = File.ReadAllLines("Resources\\SQL\\" + creationScript);
                var sqlCmd = String.Join("\n", lines);

                using (var cmd = new SQLiteCommand(sqlCmd, db))
                {
                    cmd.ExecuteScalar();
                }
            }

            if (!File.Exists(_rootCachePath.FullName))
            {
                using (var db = new SQLiteConnection(RootsCacheConnectionString))
                {
                    db.Open();
                    var lines = File.ReadAllLines("Resources\\SQL\\" + rootCacheCreationScript);
                    var sqlCmd = String.Join("\n", lines);

                    using (var cmd = new SQLiteCommand(sqlCmd, db))
                    {
                        cmd.ExecuteScalar();
                    }
                }
            }
        }


        /// <summary>
        /// Populate the monsters table.
        /// </summary>
        /// <returns></returns>
        private static async Task RebuildMonstersCache()
        {
            // Mounts, Minions, etc. are really just monsters.
            await RebuildMinionsCache();
            await RebuildMountsCache();
            await RebuildPetsCache();
        }


        /// <summary>
        /// Populate the ui table.
        /// </summary>
        /// <returns></returns>
        private static async Task RebuildUiCache()
        {
            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                var _ui = new UI(_gameInfo.GameDirectory, _gameInfo.GameLanguage);
                var list = await _ui.GetActionList();
                list.AddRange(await _ui.GetLoadingImageList());
                list.AddRange(await _ui.GetMapList());
                list.AddRange(await _ui.GetMapSymbolList());
                list.AddRange(await _ui.GetOnlineStatusList());
                list.AddRange(await _ui.GetStatusList());
                list.AddRange(await _ui.GetWeatherList());
                list.AddRange(await _ui.GetUldList());

                db.Open();
                using (var transaction = db.BeginTransaction())
                {
                    foreach (var item in list)
                    {
                        var query = @"
                            insert into ui (name, category, subcategory, path, icon_id) values ($name, $category, $subcategory, $path, $icon_id)
                                on conflict do nothing";
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            cmd.Parameters.AddWithValue("name", item.Name);
                            cmd.Parameters.AddWithValue("category", item.SecondaryCategory);
                            cmd.Parameters.AddWithValue("subcategory", item.TertiaryCategory);
                            cmd.Parameters.AddWithValue("path", item.UiPath);
                            cmd.Parameters.AddWithValue("icon_id", item.IconNumber);
                            cmd.ExecuteScalar();
                        }
                    }
                    transaction.Commit();
                }
            }
        }


        /// <summary>
        /// Populate the housing table.
        /// </summary>
        /// <returns></returns>
        private static async Task RebuildFurnitureCache()
        {
            using (var db = new SQLiteConnection(CacheConnectionString))
            {

                var _housing = new Housing(_gameInfo.GameDirectory, _gameInfo.GameLanguage);
                var list = await _housing.GetUncachedFurnitureList();

                db.Open();
                using (var transaction = db.BeginTransaction())
                {
                    foreach (var item in list)
                    {

                        var query = @"
                            insert into housing ( name,  category,  subcategory,  primary_id,  icon_id) 
                                          values($name, $category, $subcategory, $primary_id, $icon_id)";
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            cmd.Parameters.AddWithValue("name", item.Name);
                            cmd.Parameters.AddWithValue("category", item.SecondaryCategory);
                            cmd.Parameters.AddWithValue("subcategory", item.TertiaryCategory);
                            cmd.Parameters.AddWithValue("icon_id", item.IconNumber);
                            cmd.Parameters.AddWithValue("primary_id", item.ModelInfo.PrimaryID);
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
        private static async Task RebuildMountsCache()
        {
            using (var db = new SQLiteConnection(CacheConnectionString))
            {

                var _companions = new Companions(_gameInfo.GameDirectory, _gameInfo.GameLanguage);
                var list = await _companions.GetUncachedMountList();

                // Don't get the ornament list for the Chinese or Korean clients as they don't have them yet
                if (_gameInfo.GameLanguage != XivLanguage.Chinese && _gameInfo.GameLanguage != XivLanguage.Korean)
                {
                    list.AddRange(await _companions.GetUncachedOrnamentList());
                }

                db.Open();
                using (var transaction = db.BeginTransaction())
                {
                    foreach (var item in list)
                    {

                        var query = @"
                            insert into monsters ( name, category,  primary_id,  secondary_id,  imc_variant,  model_type) 
                                           values($name, $category, $primary_id, $secondary_id, $imc_variant, $model_type)
                            on conflict do nothing";
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            try
                            {
                                cmd.Parameters.AddWithValue("name", item.Name);
                                cmd.Parameters.AddWithValue("category", item.SecondaryCategory);
                                cmd.Parameters.AddWithValue("primary_id", item.ModelInfo.PrimaryID);
                                cmd.Parameters.AddWithValue("secondary_id", item.ModelInfo.SecondaryID);
                                cmd.Parameters.AddWithValue("imc_variant", item.ModelInfo.ImcSubsetID);
                                cmd.Parameters.AddWithValue("model_type", ((XivMonsterModelInfo)item.ModelInfo).ModelType.ToString());
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
        private static async Task RebuildPetsCache()
        {
            using (var db = new SQLiteConnection(CacheConnectionString))
            {

                var _companions = new Companions(_gameInfo.GameDirectory, _gameInfo.GameLanguage);
                var list = await _companions.GetUncachedPetList();

                db.Open();
                using (var transaction = db.BeginTransaction())
                {
                    foreach (var item in list)
                    {

                        var query = @"
                            insert into monsters ( name, category,  primary_id,  secondary_id,  imc_variant,  model_type) 
                                           values($name, $category, $primary_id, $secondary_id, $imc_variant, $model_type)
                            on conflict do nothing";
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            try
                            {
                                cmd.Parameters.AddWithValue("name", item.Name);
                                cmd.Parameters.AddWithValue("category", item.SecondaryCategory);
                                cmd.Parameters.AddWithValue("primary_id", item.ModelInfo.PrimaryID);
                                cmd.Parameters.AddWithValue("secondary_id", item.ModelInfo.SecondaryID);
                                cmd.Parameters.AddWithValue("imc_variant", item.ModelInfo.ImcSubsetID);
                                cmd.Parameters.AddWithValue("model_type", ((XivMonsterModelInfo)item.ModelInfo).ModelType.ToString());
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
        private static async Task RebuildMinionsCache()
        {
            using (var db = new SQLiteConnection(CacheConnectionString))
            {

                var _companions = new Companions(_gameInfo.GameDirectory, _gameInfo.GameLanguage);
                var list = await _companions.GetUncachedMinionList();

                db.Open();
                using (var transaction = db.BeginTransaction())
                {
                    foreach (var item in list)
                    {

                        var query = @"
                            insert into monsters ( name, category,  primary_id,  secondary_id,  imc_variant,  model_type) 
                                           values($name, $category, $primary_id, $secondary_id, $imc_variant, $model_type)
                            on conflict do nothing";
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            try {
                                cmd.Parameters.AddWithValue("name", item.Name);
                                cmd.Parameters.AddWithValue("primary_id", item.ModelInfo.PrimaryID);
                                cmd.Parameters.AddWithValue("secondary_id", item.ModelInfo.SecondaryID);
                                cmd.Parameters.AddWithValue("imc_variant", item.ModelInfo.ImcSubsetID);
                                cmd.Parameters.AddWithValue("category", item.SecondaryCategory);
                                cmd.Parameters.AddWithValue("model_type", ((XivMonsterModelInfo)item.ModelInfo).ModelType.ToString());
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


        /// <summary>
        /// Populate the items table.
        /// </summary>
        /// <returns></returns>
        private static async Task RebuildItemsCache()
        {
            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                Gear gear = null;
                gear = new Gear(_gameInfo.GameDirectory, _gameInfo.GameLanguage);
                var items = await gear.GetUnCachedGearList();

                db.Open();
                using (var transaction = db.BeginTransaction())
                {
                    foreach (var item in items)
                    {
                        var query = @"insert into items ( exd_id,  primary_id,  secondary_id,  imc_variant,  slot,  slot_full,  name,  icon_id, is_weapon) 
                                                  values($exd_id, $primary_id, $secondary_id, $imc_variant, $slot, $slot_full, $name, $icon_id, $is_weapon)";
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            cmd.Parameters.AddWithValue("exd_id", item.ExdID);
                            cmd.Parameters.AddWithValue("primary_id", item.ModelInfo.PrimaryID);
                            cmd.Parameters.AddWithValue("secondary_id", item.ModelInfo.SecondaryID);
                            cmd.Parameters.AddWithValue("is_weapon", ((XivGearModelInfo)item.ModelInfo).IsWeapon);
                            cmd.Parameters.AddWithValue("slot", item.GetItemSlotAbbreviation());
                            cmd.Parameters.AddWithValue("slot_full", item.SecondaryCategory);
                            cmd.Parameters.AddWithValue("imc_variant", item.ModelInfo.ImcSubsetID);
                            cmd.Parameters.AddWithValue("name", item.Name);
                            cmd.Parameters.AddWithValue("icon_id", item.IconNumber);
                            cmd.ExecuteScalar();
                        }
                    }
                    transaction.Commit();
                }
            }
        }


        /// <summary>
        /// Get the ui entries list, optionally with a substring filter.
        /// </summary>
        /// <param name="substring"></param>
        /// <returns></returns>
        public static async Task<List<XivFurniture>> GetCachedFurnitureList(string substring = null)
        {
            WhereClause where = null;
            if (substring != null)
            {
                where = new WhereClause();
                where.Comparer = WhereClause.ComparisonType.Like;
                where.Column = "name";
                where.Value = "%" + substring + "%";
            }

            return await BuildListFromTable("housing", where, async (reader) =>
            {
                var item = new XivFurniture
                {
                    PrimaryCategory = XivStrings.Housing,
                    SecondaryCategory = reader.GetString("category"),
                    TertiaryCategory = reader.GetString("subcategory"),
                    Name = reader.GetString("name"),
                    IconNumber = (uint)reader.GetInt32("icon_id"),
                    ModelInfo = new XivModelInfo()
                    {
                        PrimaryID = reader.GetInt32("primary_id")
                    }
                };
                return item;
            });
        }

        /// <summary>
        /// Get the ui entries list, optionally with a substring filter.
        /// </summary>
        /// <param name="substring"></param>
        /// <returns></returns>
        public static async Task<List<XivUi>> GetCachedUiList(string substring = null)
        {
            WhereClause where = null;
            if (substring != null)
            {
                where = new WhereClause();
                where.Comparer = WhereClause.ComparisonType.Like;
                where.Column = "name";
                where.Value = "%" + substring + "%";
            }

            return await BuildListFromTable("ui", where, async (reader) =>
            {
                var item = new XivUi
                {
                    PrimaryCategory = XivStrings.UI,
                    SecondaryCategory = reader.GetString("category"),
                    TertiaryCategory = reader.GetString("subcategory"),
                    Name = reader.GetString("name"),
                    IconNumber = reader.GetInt32("icon_id"),
                    UiPath = reader.GetString("path"),
                };
                return item;
            });
        }

        /// <summary>
        /// Get the minions entries list, optionally with a substring filter.
        /// </summary>
        /// <param name="substring"></param>
        /// <returns></returns>
        public static async Task<List<XivMinion>> GetCachedMinionsList(string substring = null)
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
                return await BuildListFromTable("monsters", where, async (reader) =>
                {
                    var item = new XivMinion
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
                    return item;
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
        public static async Task<List<XivPet>> GetCachedPetList(string substring = null)
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
                return await BuildListFromTable("monsters", where, async (reader) =>
                {
                    var item = new XivPet
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
                    return item;
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
        public static async Task<List<XivMount>> GetCachedMountList(string substring = null, string category = null)
        {
            var where = new WhereClause();

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
                return await BuildListFromTable("monsters", where, async (reader) =>
                {
                    var item = new XivMount
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
                    return item;
                });
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        /// <summary>
        /// Get the gear entries list, optionally with a substring filter.
        /// </summary>
        /// <param name="substring"></param>
        /// <returns></returns>
        public static async Task<List<XivGear>> GetCachedGearList(string substring = null)
        {
            WhereClause where = null;
            if (substring != null)
            {
                where = new WhereClause();
                where.Comparer = WhereClause.ComparisonType.Like;
                where.Column = "name";
                where.Value = "%" + substring + "%";
            }

            List<XivGear> mainHands = new List<XivGear>();
            List<XivGear> offHands = new List<XivGear>();
            var list = await BuildListFromTable("items", where, async (reader) =>
            {
                var primaryMi = new XivGearModelInfo();

                var item = new XivGear
                {
                    ExdID = reader.GetInt32("exd_id"),
                    PrimaryCategory = XivStrings.Gear,
                    SecondaryCategory = reader.GetString("slot_full"),
                    ModelInfo = primaryMi,
                };

                item.Name = reader.GetString("name");
                item.IconNumber = (uint)reader.GetInt32("icon_id");
                primaryMi.IsWeapon = reader.GetBoolean("is_weapon");
                primaryMi.PrimaryID = reader.GetInt32("primary_id");
                primaryMi.SecondaryID = reader.GetInt32("secondary_id");
                primaryMi.ImcSubsetID = reader.GetInt32("imc_variant");

                if(item.Name.Contains(XivStrings.Main_Hand))
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
        /// Sets a meta value to the cache.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        private static void SetMetaValue(string key, string value = null)
        {
            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                db.Open();
                var query = "insert into meta(key, value) values($key,$value) on conflict(key) do update set value = excluded.value";
                using (var cmd = new SQLiteCommand(query, db))
                {
                    cmd.Parameters.AddWithValue("key", key);
                    cmd.Parameters.AddWithValue("value", value);
                    cmd.ExecuteScalar();
                }
            }
        }

        /// <summary>
        /// Retrieves a meta value from the cache.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private static string GetMetaValue(string key)
        {
            string val = null;
            using (var db = new SQLiteConnection(CacheConnectionString))
            {
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
            }

            return val?.ToString();
        }

        /// <summary>
        /// Builds the initial base/required dependency list, aka
        /// all the file children of the modded files in the modlist.
        /// </summary>
        /// <returns></returns>
        private static async Task BuildModdedItemDependencies()
        {
            var _modding = new Modding(GameInfo.GameDirectory);
            var modList = _modding.GetModList();

            var paths = new List<string>(modList.Mods.Count);
            foreach (var m in modList.Mods)
            {
                try
                {
                    if (m.fullPath != null && m.fullPath != "")
                    {
                        paths.Add(m.fullPath);
                        await UpdateChildFiles(m.fullPath);
                    }
                }
                catch (Exception ex)
                {
                    throw;
                }
            }
            QueueParentFilesUpdate(paths);
        }


        /// <summary>
        /// Retreives the child files in the dependency graph for this file.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public static async Task<List<string>> GetChildFiles(string internalFilePath, bool cachedOnly = false)
        {
            var wc = new WhereClause() { Column = "parent", Comparer = WhereClause.ComparisonType.Equal, Value = internalFilePath };
            var list = await BuildListFromTable("dependencies_children", wc, async (reader) =>
            {
                return reader.GetString("child");
            });

            // Cache said this file has no children.
            if(list.Count == 1 && list[0] == null)
            {
                return new List<string>();
            }

            if (cachedOnly)
            {
                return list;
            }


            if (list.Count == 0)
            {
                // Need to pull the raw data to verify a 0 count entry.
                list = await Dependencies.GetChildFiles(internalFilePath);
                if (list != null && list.Count > 0)
                {
                    await UpdateChildFiles(internalFilePath, list);
                }
            }
            return list;
        }

        /// <summary>
        /// Retreives the parent files in the dependency graph for this file.
        /// </summary>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public static async Task<List<string>> GetParentFiles(string internalFilePath, bool cachedOnly = false)
        {
            var wc = new WhereClause() { Column = "child", Comparer = WhereClause.ComparisonType.Equal, Value = internalFilePath };
            var list = await BuildListFromTable("dependencies_parents", wc, async (reader) =>
            {
                return reader.GetString("parent");
            });

            // Cache said this file has no parents.
            if (list.Count == 1 && list[0] == null)
            {
                return new List<string>();
            }

            if (cachedOnly)
            {
                return list;
            }

            if (list.Count == 0)
            {
                // Need to pull the raw data to verify a 0 count entry.
                list = await Dependencies.GetParentFiles(internalFilePath);
                if (list != null && list.Count > 0)
                {
                    await UpdateParentFiles(internalFilePath, list);
                }
            }
            return list;
        }


        /// <summary>
        /// Retrieves the entire universe of available, valid roots from the database; or lacking a cache,
        /// this simply uses the main ItemList to generate it.
        /// </summary>
        /// <returns></returns>
        public static Dictionary<XivItemType, Dictionary<int, Dictionary<XivItemType, Dictionary<int, Dictionary<string, XivDependencyRootInfo>>>>> GetAllRoots()
        {
            // Primary Type => Dictionary of Primary IDs
            // Primary IDs => Dictionary of Primary Secondary Types
            // Secondary Types => Dictionary of Secondary IDs 
            // Secondary ID => List of Slots
            // => Slot => [Root]

            // This monolith is keyed by
            // [PrimaryType] [PrimaryId] [SecondaryType] [SecondaryId] [Slot]
            var dict = new Dictionary<XivItemType, Dictionary<int, Dictionary<XivItemType, Dictionary<int, Dictionary<string, XivDependencyRootInfo>>>>>();


            using (var db = new SQLiteConnection(RootsCacheConnectionString))
            {
                // Set up the actual full query.
                var query = "select * from roots order by primary_type, primary_id, secondary_type, secondary_id";
                db.Open();

                using (var cmd = new SQLiteCommand(query, db))
                {
                    using (var reader = new CacheReader(cmd.ExecuteReader()))
                    {
                        while (reader.NextRow())
                        {
                            try
                            {
                                var root = new XivDependencyRootInfo();
                                var pTypeString = reader.GetString("primary_type");
                                root.PrimaryType = (XivItemType)Enum.Parse(typeof(XivItemType), pTypeString); ;
                                var sTypeString = reader.GetString("secondary_type");
                                if(!String.IsNullOrEmpty(sTypeString))
                                {
                                    root.SecondaryType = (XivItemType)Enum.Parse(typeof(XivItemType), sTypeString);
                                }
                                root.PrimaryId = reader.GetInt32("primary_id");
                                root.SecondaryId = reader.GetNullableInt32("secondary_id");
                                root.Slot = reader.GetString("slot");

                                if (!dict.ContainsKey(root.PrimaryType))
                                {
                                    dict.Add(root.PrimaryType, new Dictionary<int, Dictionary<XivItemType, Dictionary<int, Dictionary<string, XivDependencyRootInfo>>>>());
                                }
                                var pTypeDict = dict[root.PrimaryType];

                                if(!pTypeDict.ContainsKey(root.PrimaryId))
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
                            catch (Exception ex)
                            {
                                throw;
                            }
                        }
                    }
                }
            }
            return dict;


        }

        public static XivDependencyRoot CreateDependencyRoot(string internalPath)
        {
            return Dependencies.CreateDependencyRoot(CreateDependencyRootInfo(internalPath));
        }
        public static XivDependencyRootInfo CreateDependencyRootInfo(string internalPath)
        {
            return Dependencies.ExtractRootInfo(internalPath);
        }

        public static XivDependencyRootInfo GetItemRootInfo(this IItem item)
        {
            var rootFolder = item.GetItemRootFolder();
            var info = Dependencies.ExtractRootInfo(rootFolder);
            info.Slot = item.GetItemSlotAbbreviation();

            if(String.IsNullOrWhiteSpace(info.Slot))
            {
                info.Slot = null;
            }
            return info;
        }
        public static XivDependencyRoot GetItemRoot(this IItem item)
        {
            return Dependencies.CreateDependencyRoot(GetItemRootInfo(item));
        }


        public static void ResetRootCache()
        {

            SQLiteConnection.ClearAllPools();
            GC.WaitForPendingFinalizers();
            File.Delete(_rootCachePath.FullName);

            using (var db = new SQLiteConnection(RootsCacheConnectionString))
            {
                db.Open();
                var lines = File.ReadAllLines("Resources\\SQL\\" + rootCacheCreationScript);
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
        public static void CacheRoot(XivDependencyRootInfo root)
        {
            using (var db = new SQLiteConnection(RootsCacheConnectionString))
            {
                db.Open();
                var query = "insert into roots (primary_type, primary_id, secondary_type, secondary_id, slot, root_path) values ($primary_type, $primary_id, $secondary_type, $secondary_id, $slot, $root_path) on conflict do nothing;";
                using (var cmd = new SQLiteCommand(query, db))
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
                    cmd.ExecuteScalar();
                }
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
        public static Dictionary<string, List<string>> GetModListParents()
        {
            var modding = new Modding(GameInfo.GameDirectory);
            var modList = modding.GetModList();
            var dict = new Dictionary<string, List<string>>(modList.Mods.Count);

            var query = "select child, parent from dependencies_parents where child in(";
            foreach(var mod in modList.Mods)
            {
                if(mod.fullPath == null || mod.fullPath == "" || mod.enabled == false)
                {
                    continue;
                }
                query += "'" + mod.fullPath + "',";
            }

            query = query.Substring(0, query.Length - 1);
            query += ") order by child";

            using (var db = new SQLiteConnection(CacheConnectionString))
            {
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
        public static async Task<List<string>> GetSiblingFiles(string internalFilePath)
        {
            return await Dependencies.GetSiblingFiles(internalFilePath);
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
            var roots = await Dependencies.GetDependencyRoots(internalFilePath);
            return roots;
        }


        /// <summary>
        /// Updates the file children in the dependencies cache.
        /// </summary>
        /// <param name="internalFilePath"></param>
        public static async Task UpdateChildFiles(string internalFilePath, List<string> children = null)
        {
            var level = Dependencies.GetDependencyLevel(internalFilePath);
            if (level == XivDependencyLevel.Invalid || level == XivDependencyLevel.Texture)
            {
                return;
            }

            // Just updating a single file.
            if (children == null)
            {
                children = await Dependencies.GetChildFiles(internalFilePath);
            }

            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                db.Open();
                using (var transaction = db.BeginTransaction())
                {
                    var query = "delete from dependencies_children where parent = $parent";
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
                    transaction.Commit();
                }
            }

        }

        /// <summary>
        /// Updates the file parents in the dependencies cache.
        /// </summary>
        /// <param name="internalFilePath"></param>
        private static async Task UpdateParentFiles(string internalFilePath, List<string> parents = null)
        {
            var level = Dependencies.GetDependencyLevel(internalFilePath);
            if (level == XivDependencyLevel.Invalid || level == XivDependencyLevel.Root)
            {
                return;
            }

            // Just updating a single file.
            if (parents == null)
            {
                parents = await Dependencies.GetParentFiles(internalFilePath);
            }

            using (var db = new SQLiteConnection(CacheConnectionString))
            {
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


        public static void QueueParentFilesUpdate(string file)
        {
            QueueParentFilesUpdate(new List<string>() { file });
        }

        public static void QueueParentFilesUpdate(List<string> files)
        {
            try
            {
                using (var db = new SQLiteConnection(CacheConnectionString))
                {
                    db.Open();
                    using (var transaction = db.BeginTransaction())
                    {
                        // First, clear out all the old data.  This is the most important point.
                        var query = "delete from dependencies_parents where child = $child";
                        using (var delCmd = new SQLiteCommand(query, db))
                        {
                            // Then insert us into the queue.
                            query = "insert into  dependencies_update_queue (file) values ($file)";
                            using (var insertCmd = new SQLiteCommand(query, db))
                            {

                                foreach (var file in files)
                                {
                                    var level = Dependencies.GetDependencyLevel(file);
                                    if (level == XivDependencyLevel.Invalid || level == XivDependencyLevel.Root)
                                    {
                                        continue;
                                    }

                                    delCmd.Parameters.AddWithValue("child", file);
                                    delCmd.ExecuteScalar();

                                    insertCmd.Parameters.AddWithValue("file", file);
                                    insertCmd.ExecuteScalar();
                                }
                            }
                        }
                        transaction.Commit();
                    }
                }
            } catch(Exception ex)
            {
                // No-Op.  This is a non-critical error.
                // Lacking appropriate parent cache data will just be a slowdown later if we ever need the data.
                //throw;
            }
        }


        private static string PopParentQueue()
        {
            string file = null;
            int position = -1;
            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                db.Open();

                // First, clear out all the old data.  This is the most important point.
                var query = "select position, file from dependencies_update_queue";
                using (var selectCmd = new SQLiteCommand(query, db))
                {
                    using (var reader = new CacheReader(selectCmd.ExecuteReader()))
                    {
                        if (!reader.NextRow())
                        {
                            // No entries left.  Signal the cache worker to shut down gracefully.
                            return null;
                        }
                        // Got the new item.
                        file = reader.GetString("file");
                        position = reader.GetInt32("position");
                    }
                }

                // Delete the row we took and all others that match the filename.
                query = "delete from dependencies_update_queue where file = $file";
                using (var deleteCmd = new SQLiteCommand(query, db))
                {
                    deleteCmd.Parameters.AddWithValue("file", file);
                    deleteCmd.ExecuteScalar();
                }
            }
            return file;
        }


        /// <summary>
        /// This function is a long-running thread function which operates alongside the main
        /// system.  It pops items off the dependency queue to process and identify what file parents
        /// they have, as the operation is rather expensive, and cache-able.  If this thread dies, 
        /// or is disabled, all functionally will/must continue to operate - however, calls to
        /// GetParentFiles() may often be significantly slower, as the data may not already be cached.
        /// </summary>
        private static void ProcessDependencyQueue(object sender, DoWorkEventArgs e)
        {
            // This will be executed on another thread.
            BackgroundWorker worker = (BackgroundWorker)sender;
            while (!worker.CancellationPending)
            {
                var file = "";
                XivDependencyLevel level;
                try
                {
                    file = PopParentQueue();
                    if (file == null)
                    {
                        // Nothing in queue.  Time for nap.
                        //worker.CancelAsync();
                        Thread.Sleep(1000);
                        continue;
                    } else
                    {
                        level = Dependencies.GetDependencyLevel(file);
                        if (level == XivDependencyLevel.Invalid || level == XivDependencyLevel.Root ) continue;

                        // See if we actually need an update.
                        var task = GetParentFiles(file, true);
                        task.Wait();
                        var parentFiles = task.Result;
                        if (parentFiles == null || parentFiles.Count == 0)
                        {
                            // Update us if we do.
                            UpdateParentFiles(file).Wait();
                        }
                    }
                } catch( Exception ex)
                {
                    var a = "b";
                    //throw;
                    // No-op;
                }
            }


            // Ensure we're good and clean up after ourselves.
            SQLiteConnection.ClearAllPools();
            _cacheWorker = null;
        }


        /// <summary>
        /// Gets the current length of the dependency processing queue.
        /// </summary>
        /// <returns></returns>
        public static int GetDependencyQueueLength()
        {
            int length = 0;
            using (var db = new SQLiteConnection(CacheConnectionString))
            {
                db.Open();

                // First, clear out all the old data.  This is the most important point.
                var query = "select count(file) as cnt from dependencies_update_queue";
                using (var selectCmd = new SQLiteCommand(query, db))
                {
                    using (var reader = new CacheReader(selectCmd.ExecuteReader()))
                    {
                        reader.NextRow();
                        // Got the new item.
                        length = reader.GetInt32("cnt");
                    }
                }
            }
            return length;
        }

        private static async Task<List<T>> BuildListFromTable<T>(string table, WhereClause where, Func<CacheReader, Task<T>> func)
        {
            return await BuildListFromTable<T>(CacheConnectionString, table, where, func);
        }

        /// <summary>
        /// Creates a list from the data entries in a cache table, using the given where clause and predicate.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="table"></param>
        /// <param name="where"></param>
        /// <param name="func"></param>
        /// <returns></returns>
        public static async Task<List<T>> BuildListFromTable<T>(string connectionString, string table, WhereClause where, Func<CacheReader, Task<T>> func)
        {

            List<T> list = new List<T>();
            using (var db = new SQLiteConnection(connectionString))
            {
                db.Open();
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

                    int val = (int)((long)await cmd.ExecuteScalarAsync());
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
                                list.Add(await func(reader));
                            }
                            catch (Exception ex)
                            {
                                throw;
                            }
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
                    else
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
                    // If disposing equals true, dispose all managed
                    // and unmanaged resources.
                    if (disposing)
                    {
                        // Dispose managed resources.

                        // Ensure the raw reader's file handle was closed.
                        if (!_reader.IsClosed)
                        {
                            _reader.Close();
                        }
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
