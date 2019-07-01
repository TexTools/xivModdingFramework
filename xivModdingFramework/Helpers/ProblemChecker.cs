// xivModdingFramework
// Copyright © 2018 Rafael Gonzalez - All Rights Reserved
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using xivModdingFramework.General.Enums;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.Helpers
{
    public class ProblemChecker
    {
        private readonly DirectoryInfo _gameDirectory;
        private readonly Index _index;
        private readonly Dat _dat;

        public ProblemChecker(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
            _index = new Index(_gameDirectory);
            _dat = new Dat(_gameDirectory);
        }

        /// <summary>
        /// Checks the index for the number of dats the game will attempt to read
        /// </summary>
        /// <returns>True if there is a problem, False otherwise</returns>
        public Task<bool> CheckIndexDatCounts(XivDataFile dataFile)
        {
            return Task.Run(() =>
            {
                var indexDatCounts = _index.GetIndexDatCount(dataFile);
                var largestDatNum = _dat.GetLargestDatNumber(dataFile) + 1;

                if (indexDatCounts.Index1 != largestDatNum)
                {
                    return true;
                }

                if (indexDatCounts.Index2 != largestDatNum)
                {
                    return true;
                }

                return false;
            });
        }

        /// <summary>
        /// Checks the index for any empty dat files
        /// </summary>
        /// <returns>A list of dats which are empty if any</returns>
        public Task<List<int>> CheckForEmptyDatFiles(XivDataFile dataFile)
        {
            return Task.Run(() =>
            {
                var largestDatNum = _dat.GetLargestDatNumber(dataFile) + 1;
                var emptyList = new List<int>();

                for (var i = 0; i < largestDatNum; i++)
                {
                    var fileInfo = new FileInfo(Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}.win32.dat{i}"));

                    if (fileInfo.Length == 0)
                    {
                        emptyList.Add(i);
                    }
                }

                return emptyList;
            });
        }

        /// <summary>
        /// Checks the index for the number of dats the game will attempt to read
        /// </summary>
        /// <returns>True if there is a problem, False otherwise</returns>
        public Task<bool> CheckForLargeDats(XivDataFile dataFile)
        {
            return Task.Run(() =>
            {
                var largestDatNum = _dat.GetLargestDatNumber(dataFile) + 1;

                var fileSizeList = new List<long>();

                for (var i = 0; i < largestDatNum; i++)
                {
                    var fileInfo = new FileInfo(Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}.win32.dat{i}"));

                    try
                    {
                        fileSizeList.Add(fileInfo.Length);
                    }
                    catch
                    {
                        return true;
                    }
                }

                if (largestDatNum > 8 || fileSizeList.FindAll(x => x.Equals(2048)).Count > 1)
                {
                    return true;
                }

                return false;
            });
        }

        /// <summary>
        /// Repairs the dat count in the index files
        /// </summary>
        public Task RepairIndexDatCounts(XivDataFile dataFile)
        {
            return Task.Run(() =>
            {
                var largestDatNum = _dat.GetLargestDatNumber(dataFile);

                _index.UpdateIndexDatCount(dataFile, largestDatNum);
            });
        }

        public Task<bool> CheckForOutdatedBackups(XivDataFile dataFile, DirectoryInfo backupsDirectory)
        {
            return Task.Run(() =>
            {
                var backupDataFile =
                    new DirectoryInfo(Path.Combine(backupsDirectory.FullName, $"{dataFile.GetDataFileName()}.win32.index"));
                var currentDataFile =
                    new DirectoryInfo(Path.Combine(_gameDirectory.FullName, $"{dataFile.GetDataFileName()}.win32.index"));

                var backupHash = _index.GetIndexSection1Hash(backupDataFile);
                var currentHash = _index.GetIndexSection1Hash(currentDataFile);

                return backupHash.SequenceEqual(currentHash);
            });
        }
    }
}