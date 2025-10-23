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
    internal enum ElementType
    {
        SByte = 0,
        Short = 1,
        Int = 2,
        Long = 3,
        Byte = 4,
        UShort = 5,
        UInt = 6,
        ULong = 7,
        False = 8,
        True = 9,
        Float = 10,
        Double = 11,
        /// <summary>
        /// 1 Byte Length UTF-8 String
        /// </summary>
        String8 = 12,
        /// <summary>
        /// 2 Byte Length UTF-8 String
        /// </summary>
        String16 = 13,
        /// <summary>
        /// 4 Byte Length UTF-8 String
        /// </summary>
        String32 = 14,
        /// <summary>
        /// 8 Byte Length UTF-8 String
        /// </summary>
        String64 = 15,
        /// <summary>
        /// 1 Byte Length Octet String
        /// </summary>
        Bytes8 = 16,
        /// <summary>
        /// 2 Byte Length Octet String
        /// </summary>
        Bytes16 = 17,
        /// <summary>
        /// 4 Byte Length Octet String
        /// </summary>
        Bytes32 = 18,
        /// <summary>
        /// 8 Byte Length Octet String
        /// </summary>
        Bytes64 = 19,
        Null = 20,
        Structure = 21,
        Array = 22,
        List = 23,
        EndOfContainer = 24,
        None = 0xFF
    }
}
