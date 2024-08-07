﻿using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.General.DataContainers;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.Mods.Enums;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.General
{
    /// <summary>
    /// Class for manipulating FFXIV CharaMakeParameter files.
    /// </summary>
    public static class CMP
    {
        const string HumanCmpPath = "chara/xls/charamake/human.cmp";
        const string RgspPathFormat = "chara/xls/charamake/rgsp/{0}-{1}.rgsp";
        static readonly Regex RgspPathExtractFormat = new Regex("^chara\\/xls\\/charamake\\/rgsp\\/([0-9]+)-([0-9]+).rgsp$");


        /// <summary>
        /// Applies a custom .rgsp file to the main Human.CMP file.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="index"></param>
        /// <param name="modlist"></param>
        /// <returns></returns>
        internal static async Task ApplyRgspFile(string filePath, bool forceDefault = false, ModTransaction tx = null)
        {
            var rgspData = await Dat.ReadFile(filePath, forceDefault, tx);

            await ApplyRgspFile(rgspData, tx);
        }

        /// <summary>
        /// Applies a custom .rgsp file to the main Human.CMP file.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="index"></param>
        /// <param name="modlist"></param>
        /// <returns></returns>
        internal static async Task ApplyRgspFile(byte[] data, ModTransaction tx = null)
        {
            var rgsp = new RacialGenderScalingParameter(data);

            await SetScalingParameter(rgsp, tx);
        }

        /// <summary>
        /// Disables the .rgsp file for this race, if it exists, and restores the .cmp file for the race back to default.
        /// </summary>
        /// <param name="race"></param>
        /// <param name="gender"></param>
        /// <returns></returns>
        public static async Task DisableRgspMod(XivSubRace race, XivGender gender, ModTransaction tx = null)
        {
            var boiler = await TxBoiler.BeginWrite(tx);
            tx = boiler.Transaction;
            try
            {
                var path = GetRgspPath(race, gender);


                var modlist = await tx.GetModList();
                var mod = modlist.GetMod(path);
                if (mod != null)
                {
                    var state = await mod.Value.GetState(tx);
                    if (state == Mods.Enums.EModState.Enabled)
                    {
                        await Modding.SetModState(EModState.Disabled, path, tx);
                    }
                }
                else
                {
                    var def = await GetScalingParameter(race, gender, true);
                    await SetScalingParameter(def);
                }

                await boiler.Commit();
            } catch(Exception ex)
            {
                await boiler.Cancel();
                throw;
            }

        }

        internal static async Task RestoreDefaultScaling(string rgspPath, ModTransaction tx = null)
        {
            var match = RgspPathExtractFormat.Match(rgspPath);
            if (!match.Success) throw new InvalidDataException("Invalid .RGSP file path.");

            var rg = GetRaceGenderFromRgspPath(rgspPath);

            await RestoreDefaultScaling(rg.Race, rg.Gender, tx);
        }

        internal static string GetModFileNameFromRgspPath(string path)
        {
            
            var rg = GetRaceGenderFromRgspPath(path);
            if (!rg.Valid) return null;

            var name = rg.Race.GetDisplayName() + " - " + rg.Gender.ToString();
            return name;
        }

        public static (XivSubRace Race, XivGender Gender, bool Valid) GetRaceGenderFromRgspPath(string path)
        {
            var match = RgspPathExtractFormat.Match(path);
            if (!match.Success) return (XivSubRace.Hyur_Midlander, XivGender.Male, false);

            var race = (XivSubRace)Int32.Parse(match.Groups[1].Value);
            var gender = (XivGender)Int32.Parse(match.Groups[2].Value);
            return (race, gender, true);
        }

        public static string GetRgspPath(RacialGenderScalingParameter rgsp)
        {
            return GetRgspPath(rgsp.Race, rgsp.Gender);
        }
        public static string GetRgspPath(XivSubRace race, XivGender gender)
        {
            var subraceId = (int)race;
            var genderId = (int)gender;
            var rgspFilePath = String.Format(RgspPathFormat, subraceId, genderId);
            return rgspFilePath;
        }

        /// <summary>
        /// Restores the default settings back into the CMP file.
        /// Does NOT delete .rgsp entry.
        /// </summary>
        /// <param name="race"></param>
        /// <param name="gender"></param>
        /// <param name="index"></param>
        /// <param name="modlist"></param>
        /// <returns></returns>
        internal static async Task RestoreDefaultScaling(XivSubRace race, XivGender gender, ModTransaction tx = null)
        {
            var defaults = await GetScalingParameter(race, gender, true, tx);
            await SetScalingParameter(defaults, tx);
        }

        /// <summary>
        /// Saves a racial scaling entry to file.
        /// </summary>
        /// <param name="rgsp"></param>
        /// <param name=""></param>
        /// <returns></returns>
        public static async Task SaveScalingParameter(RacialGenderScalingParameter rgsp, string sourceApplication, ModTransaction tx = null)
        {

            // Write the .rgsp file and let the DAT functions handle applying it.
            var rgspFilePath = GetRgspPath(rgsp.Race, rgsp.Gender);

            var bytes = rgsp.GetBytes();


            var dummyItem = GetDummyItem(rgsp);

            await Dat.ImportType2Data(bytes, rgspFilePath, sourceApplication, dummyItem , tx);
        }

        public static IItem GetDummyItem(RacialGenderScalingParameter rgsp)
        {
            var dummyItem = new XivGenericItemModel();
            dummyItem.Name = rgsp.Race.GetDisplayName() + " - " + rgsp.Gender.ToString();
            dummyItem.SecondaryCategory = "Racial Scaling";
            return dummyItem;
        }



        public static async Task<RacialGenderScalingParameter> GetScalingParameter(XivSubRace race, XivGender gender, bool forceOriginal = false, ModTransaction tx = null)
        {
            var cmp = await GetCharaMakeParameterSet(forceOriginal, tx);

            return cmp.GetScalingParameter(race, gender);
        }

        /// <summary>
        /// Performs the base level alteration to the CMP file, without going through .rgsp files.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="index"></param>
        /// <param name="modlist"></param>
        /// <returns></returns>
        private static async Task SetScalingParameter(RacialGenderScalingParameter data, ModTransaction tx = null)
        {
            var cmp = await GetCharaMakeParameterSet(false, tx);
            cmp.SetScalingParameter(data);
            await SaveCharaMakeParameterSet(cmp, tx);
        }

        private static async Task<CharaMakeParameterSet> GetCharaMakeParameterSet(bool forceOriginal = false, ModTransaction tx = null)
        {

            var data = await Dat.ReadFile(HumanCmpPath, forceOriginal, tx);
            var cmp = new CharaMakeParameterSet(data);


            return cmp;
        }

        private static async Task SaveCharaMakeParameterSet(CharaMakeParameterSet cmp, ModTransaction tx = null)
        {
            var dummyItem = new XivGenericItemModel();
            dummyItem.Name = "human.cmp";
            dummyItem.SecondaryCategory = Constants.InternalModSourceName;

            await Dat.ImportType2Data(cmp.GetBytes(), HumanCmpPath, Constants.InternalModSourceName, dummyItem, tx);
        }

    }
}
