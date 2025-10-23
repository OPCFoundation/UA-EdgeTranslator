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

using MatterDotNet.Clusters.General;
using MatterDotNet.Messages.InteractionModel;
using MatterDotNet.Protocol.Subprotocols;
using System.Data;

namespace MatterDotNet.Clusters
{
    /// <summary>
    /// The base class for all clusters
    /// </summary>
    public abstract class ClusterBase
    {
        /// <summary>
        /// Creates a new instance of the cluster
        /// </summary>
        /// <param name="cluster"></param>
        /// <param name="endPoint"></param>
        public ClusterBase(uint cluster, ushort endPoint)
        {
            this.cluster = cluster;
            this.endPoint = endPoint;
        }

        /// <summary>
        /// End point number
        /// </summary>
        protected readonly ushort endPoint;

        /// <summary>
        /// Cluster ID
        /// </summary>
        protected readonly uint cluster;

        /// <summary>
        /// Gets an optional field from an Invoke Response
        /// </summary>
        /// <param name="resp"></param>
        /// <param name="fieldNumber"></param>
        /// <returns></returns>
        protected static object GetOptionalField(InvokeResponseIB resp, int fieldNumber)
        {
            object[] fields = (object[])resp.Command!.CommandFields!;
            if (fieldNumber >= fields.Length)
                return null;
            return fields[fieldNumber];
        }

        /// <summary>
        /// Gets a required field from an Invoke Response
        /// </summary>
        /// <param name="resp"></param>
        /// <param name="fieldNumber"></param>
        /// <returns></returns>
        /// <exception cref="DataException"></exception>
        protected static object GetField(InvokeResponseIB resp, int fieldNumber)
        {
            object[] fields = (object[])resp.Command!.CommandFields!;
            if (fieldNumber >= fields.Length)
                throw new DataException("Field " + fieldNumber + " is missing");
            return fields[fieldNumber]!;
        }

        /// <summary>
        /// Validates a response and throws an exception if it's an error status
        /// </summary>
        /// <param name="resp"></param>
        /// <returns></returns>
        /// <exception cref="InvalidDataException"></exception>
        protected bool ValidateResponse(InvokeResponseIB resp)
        {
            return InteractionManager.ValidateResponse(resp, endPoint);
        }

        /// <summary>
        /// Returns the human readable name for the cluster
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return this.GetType().Name;
        }

        /// <summary>
        /// Create a cluster for the given cluster ID and end point
        /// </summary>
        /// <param name="clusterId"></param>
        /// <param name="endPoint"></param>
        /// <returns></returns>
        public static ClusterBase Create(uint clusterId, ushort endPoint)
        {
            switch (clusterId)
            {
                case OperationalCredentials.CLUSTER_ID:
                    return new OperationalCredentials(endPoint);
            }

            return null;
        }
    }
}
