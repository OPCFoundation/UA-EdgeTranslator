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

using System;

namespace MatterDotNet.Attributes
{
    /// <summary>
    /// Create a read-only attribute
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ReadAttribute<T>
    {
        /// <summary>
        /// The last returned value
        /// </summary>
        public T Value { get; internal set; } = default!;
        /// <summary>
        /// Cluster ID
        /// </summary>
        public uint ClusterId { get; init; }
        /// <summary>
        /// End Point Number
        /// </summary>
        public ushort EndPoint { get; init; }
        /// <summary>
        /// Attribute ID
        /// </summary>
        public ushort AttributeId { get; init; }
        /// <summary>
        /// Is null allowed
        /// </summary>
        protected bool nullable;

        /// <summary>
        /// Create a read-only attribute
        /// </summary>
        /// <param name="clusterId"></param>
        /// <param name="endPoint"></param>
        /// <param name="attributeId"></param>
        /// <param name="nullable"></param>
        internal ReadAttribute(uint clusterId, ushort endPoint, ushort attributeId, bool nullable = false)
        {
            ClusterId = clusterId;
            EndPoint = endPoint;
            AttributeId = attributeId;
            this.nullable = nullable;
        }

        /// <summary>
        /// Required deserialization function
        /// </summary>
        public required Func<object, T> Deserialize;
    }
}
