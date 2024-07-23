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

namespace xivModdingFramework.Models.Enums
{
    /// <summary>
    /// Enum containing the what the data entries in the Vertex Data Block will be used for
    /// </summary>
    public enum VertexUsageType
    {
        Position          = 0x0,
        BoneWeight        = 0x1,
        BoneIndex         = 0x2,
        Normal            = 0x3,
        TextureCoordinate = 0x4,
        Flow           = 0x5,
        Binormal          = 0x6,
        Color             = 0x7
    }
}