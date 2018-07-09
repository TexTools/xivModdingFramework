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
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Resources;

namespace xivModdingFramework.Items.Categories
{
    public class Character
    {
        /// <summary>
        /// Gets the List to be displayed under the Character category
        /// </summary>
        /// <returns>A list containing XivCharacter data</returns>
        public List<XivCharacter> GetCharacterList()
        {
            var characterList = new List<XivCharacter>
            {
                new XivCharacter {Name = XivStrings.Body, Category = XivStrings.Character},
                new XivCharacter {Name = XivStrings.Face, Category = XivStrings.Character},
                new XivCharacter {Name = XivStrings.Hair, Category = XivStrings.Character},
                new XivCharacter {Name = XivStrings.Tail, Category = XivStrings.Character},
                new XivCharacter {Name = XivStrings.Face_Paint, Category = XivStrings.Character},
                new XivCharacter {Name = XivStrings.Equipment_Decals, Category = XivStrings.Character}
            };

            return characterList;
        }
    }
}