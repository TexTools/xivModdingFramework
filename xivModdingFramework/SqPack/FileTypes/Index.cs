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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using xivModdingFramework.General.Enums;

namespace xivModdingFramework.SqPack.FileTypes
{
    /// <summary>
    /// This class contains the methods that deal with the .index file type 
    /// </summary>
    public class Index
    {
        private const string IndexExtension = ".win32.index";
        private const string Index2Extension = ".win32.index2";
        private readonly DirectoryInfo _gameDirectory;

        public Index(DirectoryInfo gameDirectory)
        {
            _gameDirectory = gameDirectory;
        }

        /// <summary>
        /// Update the dat count within the index files.
        /// </summary>
        /// <param name="dataFile">The data file to update the index for.</param>
        /// <param name="datNum">The dat number to update to.</param>
        internal void UpdateIndexDatCount(XivDataFile dataFile, int datNum)
        {
            var datCount = (byte)(datNum + 1);

            var indexPaths = new[]
            {
                _gameDirectory + "\\" + dataFile.GetDataFileName() + IndexExtension,
                _gameDirectory + "\\" + dataFile.GetDataFileName() + Index2Extension
            };

            foreach (var indexPath in indexPaths)
            {
                using (var bw = new BinaryWriter(File.OpenWrite(indexPath)))
                {
                    bw.BaseStream.Seek(1104, SeekOrigin.Begin);
                    bw.Write(datCount);
                }
            }
        }

        /// <summary>
        /// Gets the offset for the data in the .dat file
        /// </summary>
        /// <param name="hashedFolder">The hashed value of the folder path</param>
        /// <param name="hashedFile">The hashed value of the file name</param>
        /// <returns>The offset to the data</returns>
        internal int GetDataOffset(int hashedFolder, int hashedFile, XivDataFile dataFile)
        {
            var indexPath = _gameDirectory + "\\" + dataFile.GetDataFileName() + IndexExtension;
            var offset = 0;

            // These are the offsets to relevant data
            const int fileCountOffset = 1036;
            const int dataStartOffset = 2048;

            using (var br = new BinaryReader(File.OpenRead(indexPath)))
            {
                br.BaseStream.Seek(fileCountOffset, SeekOrigin.Begin);
                var fileCount = br.ReadInt32();

                br.BaseStream.Seek(dataStartOffset, SeekOrigin.Begin);

                // loop through each file entry
                for (var i = 0; i < fileCount; br.ReadBytes(4), i += 16)
                {
                    var fileNameHash = br.ReadInt32();

                    // check if the provided file name hash matches the current file name hash
                    if (fileNameHash == hashedFile)
                    {
                        var folderPathHash = br.ReadInt32();

                        // check if the provided folder path hash matches the current folder path hash
                        if (folderPathHash == hashedFolder)
                        {
                            // this is the entry we are looking for, get the offset and break out of the loop
                            offset = br.ReadInt32() * 8;
                            break;
                        }

                        br.ReadBytes(4);
                    }
                    else
                    {
                        br.ReadBytes(8);
                    }
                }
            }

            return offset;
        }


        /// <summary>
        /// Gets all the file offsets in a given folder path
        /// </summary>
        /// <param name="hashedFolder">The hashed value of the folder path</param>
        /// <param name="dataFile">The data file to look in</param>
        /// <returns>A list of all of the offsets in the given folder</returns>
        public List<int> GetAllFileOffsetsInFolder(int hashedFolder, XivDataFile dataFile)
        {
            var fileOffsetList = new List<int>();

            // These are the offsets to relevant data
            const int fileCountOffset = 1036;
            const int dataStartOffset = 2048;

            var indexPath = _gameDirectory + "\\" + dataFile.GetDataFileName() + IndexExtension;

            using (var br = new BinaryReader(File.OpenRead(indexPath)))
            {
                br.BaseStream.Seek(fileCountOffset, SeekOrigin.Begin);
                var totalFiles = br.ReadInt32();

                br.BaseStream.Seek(dataStartOffset, SeekOrigin.Begin);
                for (var i = 0; i < totalFiles; br.ReadBytes(4), i += 16)
                {
                    br.ReadBytes(4);

                    var folderPathHash = br.ReadInt32();

                    if (folderPathHash == hashedFolder)
                    {
                        fileOffsetList.Add(br.ReadInt32() * 8);
                    }
                    else
                    {
                        br.ReadBytes(4);
                    }
                }
            }

            return fileOffsetList;
        }

        /// <summary>
        /// Get all the hashed values of the files in a given folder 
        /// </summary>
        /// <param name="hashedFolder">The hashed value of the folder path</param>
        /// <param name="dataFile">The data file to look in</param>
        /// <returns>A list containing the hashed values of the files in the given folder</returns>
        public List<int> GetAllHashedFilesInFolder(int hashedFolder, XivDataFile dataFile)
        {
            var fileHashesList = new List<int>();

            // These are the offsets to relevant data
            const int fileCountOffset = 1036;
            const int dataStartOffset = 2048;

            var indexPath = _gameDirectory + "\\" + dataFile.GetDataFileName() + IndexExtension;

            using (var br = new BinaryReader(File.OpenRead(indexPath)))
            {
                br.BaseStream.Seek(fileCountOffset, SeekOrigin.Begin);
                var totalFiles = br.ReadInt32();

                br.BaseStream.Seek(dataStartOffset, SeekOrigin.Begin);
                for (var i = 0; i < totalFiles; br.ReadBytes(4), i += 16)
                {
                    var hashedFile = br.ReadInt32();

                    var folderPathHash = br.ReadInt32();

                    if (folderPathHash == hashedFolder)
                    {
                        fileHashesList.Add(hashedFile);
                    }

                    br.ReadBytes(4);
                }
            }

            return fileHashesList;
        }

        /// <summary>
        /// Creates a backup of the index file.
        /// </summary>
        /// <param name="backupsDirectory">The directory in which to place the backup files.
        /// The directory will be created if it does not exist.</param>
        /// <param name="dataFile">The file to backup.</param>
        public void CreateIndexBackups(DirectoryInfo backupsDirectory, XivDataFile dataFile)
        {
            var fileName = dataFile.GetDataFileName();

            var indexPath = _gameDirectory + "\\" + fileName + IndexExtension;
            var index2Path = _gameDirectory + "\\" + fileName + Index2Extension;

            var indexBackupPath = backupsDirectory.FullName + "\\" + fileName + IndexExtension;
            var index2BackupPath = backupsDirectory.FullName + "\\" + fileName + Index2Extension;

            Directory.CreateDirectory(backupsDirectory.FullName);

            File.Copy(indexPath, indexBackupPath, true);
            File.Copy(index2Path, index2BackupPath, true);
        }


        /// <summary>
        /// Creates a backup of all the index files.
        /// </summary>
        /// <param name="backupsDirectory">The directory in which to place the backup files.</param>
        public void BackupAllIndexFiles(DirectoryInfo backupsDirectory)
        {
            var dataFileList = Enum.GetValues(typeof(XivDataFile)).Cast<XivDataFile>();

            foreach (var dataFile in dataFileList)
            {
                CreateIndexBackups(backupsDirectory, dataFile);
            }
        }
    }
}