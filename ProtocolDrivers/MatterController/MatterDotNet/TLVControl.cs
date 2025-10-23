// MatterDotNet Copyright (C) 2025
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or any later version.
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY, without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU Affero General Public License for more details.
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

namespace MatterDotNet.Protocol.Parsers
{
    internal enum TLVControl
    {
        /// <summary>
        /// 0 Byte Length
        /// </summary>
        Anonymous = 0,
        /// <summary>
        /// 1 Byte Length
        /// </summary>
        ContextSpecific = 1,
        CommonProfileShort = 2,
        CommonProfileInt = 3,
        ImplicitProfileShort = 4,
        ImplicitProfileInt = 5,
        FullyQualifiedShort = 6,
        FullyQualifiedInt = 7
    }
}
