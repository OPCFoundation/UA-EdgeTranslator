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

using MatterDotNet.Protocol.Parsers;
using System;
using System.Collections.Generic;
using System.IO;

namespace MatterDotNet.Protocol.Payloads
{
    /// <summary>
    /// An application payload
    /// </summary>
    public abstract record TLVPayload : IPayload
    {
        /// <summary>
        /// An empty application payload
        /// </summary>
        public TLVPayload() { }
        /// <summary>
        /// Parse the TLVs from a frame into this message
        /// </summary>
        /// <param name="data"></param>
        internal TLVPayload(Memory<byte> data) : this(new TLVReader(data)) {}

        /// <summary>
        /// Parse the TLVs from a frame into this message
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="structureNumber"></param>
        internal TLVPayload(TLVReader reader, long structureNumber = -1) { }

        public TLVPayload(object[] fields) { }

        /// <summary>
        /// Write the TLVs to an application payload
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="structureNumber"></param>
        internal abstract void Serialize(TLVWriter writer, long structureNumber = -1);

        /// <summary>
        /// Write the TLVs to an application payload
        /// </summary>
        /// <param name="writer"></param>
        /// <returns></returns>
        public void Serialize(PayloadWriter writer)
        {
            Serialize(new TLVWriter(writer));
        }

        /// <summary>
        /// Throw an error if the list does not fall within the valid size range
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <exception cref="InvalidDataException"></exception>
        protected void Constrain<T>(List<T> list, int min, int max = int.MaxValue)
        {
            if (list.Count < min)
                throw new InvalidDataException($"List must contain at least {min} element{(min == 1 ? "" : "s")} but contained {list.Count}");
            if (list.Count > max)
                throw new InvalidDataException($"List may not contain more than {max} element{(min == 1 ? "" : "s")} but contained {list.Count}");
        }

        /// <summary>
        /// Throw an error if the array does not fall within the valid size range
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="array"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <exception cref="InvalidDataException"></exception>
        protected void Constrain<T>(T[] array, int min, int max = int.MaxValue)
        {
            if (array.Length < min)
                throw new InvalidDataException($"Array must contain at least {min} element{(min == 1 ? "" : "s")} but contained {array.Length}");
            if (array.Length > max)
                throw new InvalidDataException($"Array may not contain more than {max} element{(min == 1 ? "" : "s")} but contained {array.Length}");
        }
    }
}
