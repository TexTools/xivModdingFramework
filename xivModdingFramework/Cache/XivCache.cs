using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Items;
using xivModdingFramework.Items.Categories;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Resources;

namespace xivModdingFramework.Cache
{

    /// <summary>
    /// Item Dependency Cache for keeping track of item dependency information.
    /// </summary>
    public class XivCache
    {
        private GameInfo _gameInfo;
        private DirectoryInfo _dbPath;
        private static readonly Version CacheVersion = new Version("0.0.0.24");
        private const string dbFileName = "mod_cache.db";
        private const string creationScript = "CreateDB.sql";
        private string _connectionString { get
            {
                return "Data Source=" + _dbPath + ";Pooling=True;Max Pool Size=100;";
            }
        }


        /// <summary>
        /// Language is not actually required for Cache -reading-, only for cache generation, so it is 
        /// technically an optional parameter if you know you're just reading cache data.
        /// </summary>
        /// <param name="gameDirectory"></param>
        /// <param name="language"></param>
        /// <param name="validateCache"></param>
        public XivCache(DirectoryInfo gameDirectory, XivLanguage language = XivLanguage.None, bool validateCache = true) : this(new GameInfo(gameDirectory, language), validateCache)
        {
        }
        public XivCache(GameInfo gameInfo, bool validateCache = true)
        {
            _gameInfo = gameInfo;
            _dbPath = new DirectoryInfo(Path.Combine(_gameInfo.GameDirectory.Parent.Parent.FullName, dbFileName));

            if (validateCache)
            {

                if (CacheNeedsRebuild())
                {
                    RebuildCache();
                }
            }
        }

        /// <summary>
        /// Tests if the cache needs to be rebuilt (and starts the process if it does.)
        /// </summary>
        private bool CacheNeedsRebuild()
        {
            Func<bool> checkValidation = () =>
            {
                try
                {
                    // Cache structure updated?
                    var val = GetMetaValue("cache_version");
                    var version = new Version(val);
                    if (version < CacheVersion)
                    {
                        return true;
                    }

                    // FFXIV Updated?
                    val = GetMetaValue("ffxiv_version");
                    version = new Version(val);
                    if (version < _gameInfo.GameVersion)
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
            if(result)
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
        public void RebuildCache()
        {

            Task.Run(async () =>
            {
                if (_gameInfo.GameLanguage == XivLanguage.None)
                {
                    throw new NotSupportedException("A valid language must be specified when rebuilding the Cache.");
                }

                CreateCache();

                try
                {
                    await RebuildItemsCache();
                    await RebuildMonstersCache();
                    await RebuildUiCache();
                    await RebuildFurnitureCache();

                    SetMetaValue("cache_version", CacheVersion.ToString());
                    SetMetaValue("ffxiv_version", _gameInfo.GameVersion.ToString());
                    SetMetaValue("language", _gameInfo.GameLanguage.ToString());

                } catch(Exception Ex)
                {
                    SetMetaValue("needs_rebuild", "1");
                    throw Ex;
                }
            }).Wait();
        }


        /// <summary>
        /// Destroys and recreates the base SQL Database.
        /// </summary>
        private void CreateCache()
        {
            SQLiteConnection.ClearAllPools();
            GC.WaitForPendingFinalizers();
            File.Delete(_dbPath.FullName);

            using (var db = new SQLiteConnection(_connectionString))
            {
                db.Open();
                var lines = File.ReadAllLines("Resources\\SQL\\" + creationScript);
                var sqlCmd = String.Join("\n", lines);

                using (var cmd = new SQLiteCommand(sqlCmd, db))
                {
                    cmd.ExecuteScalar();
                }
            }
        }


        /// <summary>
        /// Populate the monsters table.
        /// </summary>
        /// <returns></returns>
        private async Task RebuildMonstersCache()
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
        private async Task RebuildUiCache()
        {
            using (var db = new SQLiteConnection(_connectionString))
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
        private async Task RebuildFurnitureCache()
        {
            using (var db = new SQLiteConnection(_connectionString))
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
        private async Task RebuildMountsCache()
        {
            using (var db = new SQLiteConnection(_connectionString))
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
                                throw ex;
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
        private async Task RebuildPetsCache()
        {
            using (var db = new SQLiteConnection(_connectionString))
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
                                throw ex;
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
        private async Task RebuildMinionsCache()
        {
            using (var db = new SQLiteConnection(_connectionString))
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
                                throw ex;
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
        private async Task RebuildItemsCache()
        {
            using (var db = new SQLiteConnection(_connectionString))
            {
                Gear gear = null;
                gear = new Gear(_gameInfo.GameDirectory, _gameInfo.GameLanguage);
                var items = await gear.GetUnCachedGearList();

                db.Open();
                using (var transaction = db.BeginTransaction())
                {
                    foreach (var item in items)
                    {
                        var query = @"insert into items (exd_id, primary_id, secondary_id, imc_variant, slot, slot_full, name, icon_id, primary_id_2, secondary_id_2, imc_variant_2) 
                            values($exd_id, $primary_id, $secondary_id, $imc_variant, $slot, $slot_full, $name, $icon_id, $primary_id_2, $secondary_id_2, $imc_variant_2)";
                        using (var cmd = new SQLiteCommand(query, db))
                        {
                            cmd.Parameters.AddWithValue("exd_id", item.ExdID);
                            cmd.Parameters.AddWithValue("primary_id", item.ModelInfo.PrimaryID);
                            cmd.Parameters.AddWithValue("secondary_id", item.ModelInfo.SecondaryID);
                            cmd.Parameters.AddWithValue("slot", item.GetItemSlotAbbreviation());
                            cmd.Parameters.AddWithValue("slot_full", item.SecondaryCategory);
                            cmd.Parameters.AddWithValue("imc_variant", item.ModelInfo.ImcSubsetID);
                            cmd.Parameters.AddWithValue("name", item.Name);
                            cmd.Parameters.AddWithValue("icon_id", item.IconNumber);
                            cmd.Parameters.AddWithValue("primary_id_2", item.SecondaryModelInfo == null ? 0 : (int?)item.SecondaryModelInfo.PrimaryID);
                            cmd.Parameters.AddWithValue("secondary_id_2", item.SecondaryModelInfo == null ? 0 : (int?)item.SecondaryModelInfo.SecondaryID);
                            cmd.Parameters.AddWithValue("imc_variant_2", item.SecondaryModelInfo == null ? 0 : (int?)item.SecondaryModelInfo.ImcSubsetID);
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
        public async Task<List<XivFurniture>> GetCachedFurnitureList(string substring = null)
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
        public async Task<List<XivUi>> GetCachedUiList(string substring = null)
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
        public async Task<List<XivMinion>> GetCachedMinionsList(string substring = null)
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
                throw ex;
            }
        }

        /// <summary>
        /// Get the pets entries list, optionally with a substring filter.
        /// </summary>
        /// <param name="substring"></param>
        /// <returns></returns>
        public async Task<List<XivPet>> GetCachedPetList(string substring = null)
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
                throw ex;
            }
        }

        /// <summary>
        /// Get the mounts entries list, optionally with a substring filter.
        /// </summary>
        /// <param name="substring"></param>
        /// <returns></returns>
        public async Task<List<XivMount>> GetCachedMountList(string substring = null, string category = null)
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
                throw ex;
            }
        }

        /// <summary>
        /// Get the gear entries list, optionally with a substring filter.
        /// </summary>
        /// <param name="substring"></param>
        /// <returns></returns>
        public async Task<List<XivGear>> GetCachedGearList(string substring = null)
        {
            WhereClause where = null;
            if (substring != null)
            {
                where = new WhereClause();
                where.Comparer = WhereClause.ComparisonType.Like;
                where.Column = "name";
                where.Value = "%" + substring + "%";
            }

            return await BuildListFromTable("items", where, async (reader) =>
            {
                var item = new XivGear
                {
                    ExdID = reader.GetInt32("exd_id"),
                    PrimaryCategory = XivStrings.Gear,
                    SecondaryCategory = reader.GetString("slot_full"),
                    ModelInfo = new XivModelInfo(),
                    SecondaryModelInfo = new XivModelInfo()
                };

                item.Name = reader.GetString("name");
                item.IconNumber = (uint)reader.GetInt32("icon_id");
                item.ModelInfo.PrimaryID = reader.GetInt32("primary_id");
                item.ModelInfo.SecondaryID = reader.GetInt32("secondary_id");
                item.ModelInfo.ImcSubsetID = reader.GetInt32("imc_variant");

                if (reader.GetInt32("primary_id_2") > 0)
                {
                    item.SecondaryModelInfo.PrimaryID = reader.GetInt32("primary_id_2");
                    item.SecondaryModelInfo.SecondaryID = reader.GetInt32("secondary_id_2");
                    item.SecondaryModelInfo.ImcSubsetID = reader.GetInt32("imc_variant_2");
                }
                else
                {
                    item.SecondaryModelInfo = null;
                }

                return item;
            });

        }

        /// <summary>
        /// Retrieve the path list of the children for this file.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public List<string> GetFileChildren(string path)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Retreive the path list of the parents for this file.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public List<string> GetFileParents(string path)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Sets a meta value to the cache.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        private void SetMetaValue(string key, string value = null)
        {
            using (var db = new SQLiteConnection(_connectionString))
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
        private string GetMetaValue(string key)
        {
            string val = null;
            using (var db = new SQLiteConnection(_connectionString))
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
                        throw Ex;
                        // Meta Table doesn't exist.
                    }
                }
            }

            return val?.ToString();
        }

        /// <summary>
        /// Creates a list from the data entries in a cache table, using the given where clause and predicate.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="table"></param>
        /// <param name="where"></param>
        /// <param name="func"></param>
        /// <returns></returns>
        private async Task<List<T>> BuildListFromTable<T>(string table, WhereClause where, Func<CacheReader, Task<T>> func)
        {
            List<T> list = new List<T>();
            using (var db = new SQLiteConnection(_connectionString))
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
                                throw ex;
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
        private class WhereClause
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
                    if (Comparer == ComparisonType.Like)
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
                    cmd.Parameters.AddWithValue(Column, Value);
                }
            }
        }

        /// <summary>
        /// A thin wrapper for the SQLiteDataReader class that
        /// helps with string column accessors, NULL value coalescence, and 
        /// ensures the underlying reader is properly closed and destroyed to help
        /// avoid lingering file handles.
        /// </summary>
        private class CacheReader : IDisposable
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

            public int GetInt32(string fieldName)
            {
                if(_reader[_headers[fieldName]].GetType() == NullType)
                {
                    return 0;
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
