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

namespace xivModdingFramework.Items.Enums
{
    /// <summary>
    /// Enum containing the types of items
    /// These are the FFXIV file system divisions of item types.
    /// </summary>
    public enum XivItemType
    {
        unknown,
        none,
        weapon,
        equipment,
        accessory,
        monster,
        demihuman,
        body,
        hair,
        tail,
        ear,
        face,
        human,
        decal,
        ui,
        furniture // This one's a little vague and encompasses really all of /bgcommon/
    }
}