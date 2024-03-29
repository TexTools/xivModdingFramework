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
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.DataContainers;
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
        internal static async Task ApplyRgspFile(string filePath, IndexFile index = null, ModList modlist = null)
        {
            var _dat = new Dat(XivCache.GameInfo.GameDirectory);
            var rgspData = await _dat.GetType2Data(filePath, false, index, modlist);

            await ApplyRgspFile(rgspData, index, modlist);
        }

        /// <summary>
        /// Applies a custom .rgsp file to the main Human.CMP file.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="index"></param>
        /// <param name="modlist"></param>
        /// <returns></returns>
        internal static async Task ApplyRgspFile(byte[] data, IndexFile index = null, ModList modlist = null)
        {
            var rgsp = new RacialGenderScalingParameter(data);

            await SetScalingParameter(rgsp, index, modlist);
        }

        /// <summary>
        /// Disables the .rgsp file for this race, if it exists, and restores the .cmp file for the race back to default.
        /// </summary>
        /// <param name="race"></param>
        /// <param name="gender"></param>
        /// <returns></returns>
        public static async Task DisableRgspMod(XivSubRace race, XivGender gender)
        {
            var path = GetRgspPath(race, gender);
            var _modding = new Modding(XivCache.GameInfo.GameDirectory);


            var modlist = await _modding.GetModListAsync();
            var mod = modlist.Mods.FirstOrDefault(x => x.fullPath == path);
            if (mod != null && mod.enabled)
            {
                await _modding.ToggleModStatus(path, false);
            } else
            {
                var def = await GetScalingParameter(race, gender, true);
                await SetScalingParameter(def);
            }

        }

        internal static async Task RestoreDefaultScaling(string rgspPath, IndexFile index = null, ModList modlist = null)
        {
            var match = RgspPathExtractFormat.Match(rgspPath);
            if (!match.Success) throw new InvalidDataException("Invalid .RGSP file path.");

            var race = (XivSubRace) Int32.Parse(match.Groups[1].Value);
            var gender = (XivGender) Int32.Parse(match.Groups[2].Value);

            await RestoreDefaultScaling(race, gender, index, modlist);
        }

        internal static string GetModFileNameFromRgspPath(string path)
        {
            var match = RgspPathExtractFormat.Match(path);
            if (!match.Success) return null;

            var race = (XivSubRace)Int32.Parse(match.Groups[1].Value);
            var gender = (XivGender)Int32.Parse(match.Groups[2].Value);

            var name = race.GetDisplayName() + " - " + gender.ToString();
            return name;
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
        internal static async Task RestoreDefaultScaling(XivSubRace race, XivGender gender, IndexFile index = null, ModList modlist = null)
        {
            var defaults = await GetScalingParameter(race, gender, true, index, modlist);
            await SetScalingParameter(defaults, index, modlist);
        }

        /// <summary>
        /// Saves a racial scaling entry to file.
        /// </summary>
        /// <param name="rgsp"></param>
        /// <param name=""></param>
        /// <returns></returns>
        public static async Task SaveScalingParameter(RacialGenderScalingParameter rgsp, string sourceApplication, IndexFile index = null, ModList modlist = null)
        {

            // Write the .rgsp file and let the DAT functions handle applying it.
            var rgspFilePath = GetRgspPath(rgsp.Race, rgsp.Gender);

            var bytes = rgsp.GetBytes();


            var dummyItem = new XivGenericItemModel();
            dummyItem.Name = rgsp.Race.GetDisplayName() + " - " + rgsp.Gender.ToString();
            dummyItem.SecondaryCategory = "Racial Scaling";

            var _dat = new Dat(XivCache.GameInfo.GameDirectory);
            await _dat.ImportType2Data(bytes, rgspFilePath, sourceApplication, dummyItem , index, modlist);
        }



        public  static async Task<RacialGenderScalingParameter> GetScalingParameter(XivSubRace race, XivGender gender, bool forceOriginal = false, IndexFile index = null, ModList modlist = null)
        {
            var cmp = await GetCharaMakeParameterSet(forceOriginal, index, modlist);

            return cmp.GetScalingParameter(race, gender);
        }

        /// <summary>
        /// Performs the base level alteration to the CMP file, without going through .rgsp files.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="index"></param>
        /// <param name="modlist"></param>
        /// <returns></returns>
        private static async Task SetScalingParameter(RacialGenderScalingParameter data, IndexFile index = null, ModList modlist = null)
        {
            var cmp = await GetCharaMakeParameterSet(false, index, modlist);
            cmp.SetScalingParameter(data);
            await SaveCharaMakeParameterSet(cmp, index, modlist);
        }

        private static async Task<CharaMakeParameterSet> GetCharaMakeParameterSet(bool forceOriginal = false, IndexFile index = null, ModList modlist = null)
        {
            var _dat = new Dat(XivCache.GameInfo.GameDirectory);

            var data = await _dat.GetType2Data(HumanCmpPath, forceOriginal, index, modlist);
            var cmp = new CharaMakeParameterSet(data);


            return cmp;
        }

        private static async Task SaveCharaMakeParameterSet(CharaMakeParameterSet cmp, IndexFile index = null, ModList modlist = null)
        {
            var _dat = new Dat(XivCache.GameInfo.GameDirectory);
            var dummyItem = new XivGenericItemModel();
            dummyItem.Name = "human.cmp";
            dummyItem.SecondaryCategory = Constants.InternalModSourceName;

            await _dat.ImportType2Data(cmp.GetBytes(), HumanCmpPath, Constants.InternalModSourceName, dummyItem, index, modlist);
        }

    }
}
