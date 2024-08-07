﻿// xivModdingFramework
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

namespace xivModdingFramework.Mods.Enums
{
    public enum EModState
    {
        // The file is modded, but points to an offset that is neither the modded or original file.
        Invalid,

        // The file has not been modded at all, and exists only in its default game state.
        UnModded,

        // The file is modded, and enabled.
        Enabled,

        // The file is modded, but the mod is disabled.
        Disabled
    }
}