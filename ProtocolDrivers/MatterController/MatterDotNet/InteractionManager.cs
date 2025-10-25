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

using Matter.Core;
using Matter.Core.Sessions;
using MatterDotNet.Messages.InteractionModel;
using MatterDotNet.Protocol.Payloads;
using MatterDotNet.Protocol.Payloads.OpCodes;
using MatterDotNet.Protocol.Payloads.Status;
using System;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AttributeReportIB = MatterDotNet.Messages.InteractionModel.AttributeReportIB;
using AttributeStatusIB = MatterDotNet.Messages.InteractionModel.AttributeStatusIB;

namespace MatterDotNet.Protocol.Subprotocols
{
    internal class InteractionManager
    {
        public static Task<InvokeResponseIB> ExecCommand(SecureSession secSession, ushort endpoint, uint cluster, uint command, TLVPayload payload = null, CancellationToken token = default)
        {
            MessageExchange exchange = secSession.CreateExchange();
            {
                //ushort refNum = (ushort)Random.Shared.Next();
                //await SendCommand(exchange, endpoint, cluster, command, false, refNum, payload, token);
                //while (!token.IsCancellationRequested)
                //{
                //    MessageFrame response = await exchange.Read(token);
                //    if (response.Message.Payload is InvokeResponseMessage msg)
                //    {
                //        if (msg.InvokeResponses[0].Status == null || !msg.InvokeResponses[0].Status!.CommandRef.HasValue || msg.InvokeResponses[0].Status!.CommandRef!.Value == refNum)
                //            return msg.InvokeResponses[0];
                //    }
                //    else if (response.Message.Payload is StatusResponseMessage status)
                //        throw new IOException("Error: " + (IMStatusCode)status.Status);
                //}
                throw new OperationCanceledException();
            }
        }

        /// <summary>
        /// Validates a response and throws an exception if it's an error status
        /// </summary>
        /// <param name="resp"></param>
        /// <param name="endPoint"></param>
        /// <returns></returns>
        /// <exception cref="InvalidDataException"></exception>
        internal static bool ValidateResponse(InvokeResponseIB resp, ushort endPoint)
        {
            if (resp.Status == null)
            {
                if (resp.Command?.CommandFields != null)
                    return true;
                throw new InvalidDataException("Response received without status");
            }
            return ValidateStatus((IMStatusCode)resp.Status.Status.Status, endPoint);
        }

        /// <summary>
        /// Validates a response and throws an exception if it's an error status
        /// </summary>
        /// <param name="resp"></param>
        /// <param name="endPoint"></param>
        /// <returns></returns>
        /// <exception cref="InvalidDataException"></exception>
        private static bool ValidateResponse(AttributeStatusIB resp, ushort endPoint)
        {
            if (resp.Status == null)
                throw new InvalidDataException("Response received without status");

            return ValidateStatus((IMStatusCode)resp.Status.Status, endPoint);
        }

        /// <summary>
        /// Validates a response and throws an exception if it's an error status
        /// </summary>
        /// <param name="resp"></param>
        /// <param name="endPoint"></param>
        /// <returns></returns>
        /// <exception cref="InvalidDataException"></exception>
        private static bool ValidateResponse(AttributeReportIB resp, ushort endPoint)
        {
            if (resp.AttributeStatus == null)
            {
                if (resp.AttributeData != null)
                    return true;
                throw new InvalidDataException("Response received without status");
            }
            return ValidateStatus((IMStatusCode)resp.AttributeStatus.Status.Status, endPoint);
        }

        private static bool ValidateStatus(IMStatusCode status, ushort endPoint)
        {
            switch (status)
            {
                case IMStatusCode.SUCCESS:
                    return true;
                case IMStatusCode.FAILURE:
                    return false;
                case IMStatusCode.UNSUPPORTED_ACCESS:
                    throw new UnauthorizedAccessException("Unsupported / Unauthorized Access");
                case IMStatusCode.UNSUPPORTED_ENDPOINT:
                    throw new InvalidOperationException("Endpoint " + endPoint + " is not supported");
                case IMStatusCode.INVALID_ACTION:
                    throw new DataException("Invalid Action");
                case IMStatusCode.UNSUPPORTED_COMMAND:
                    throw new DataException("Command ID not supported on this cluster");
                case IMStatusCode.INVALID_COMMAND:
                    throw new DataException("Invalid Command Payload");
                case IMStatusCode.CONSTRAINT_ERROR:
                    throw new DataException("Data constraint violated");
                case IMStatusCode.RESOURCE_EXHAUSTED:
                    throw new InsufficientMemoryException("Resource exhausted");
                case IMStatusCode.DATA_VERSION_MISMATCH:
                    throw new DataException("Data version mismatch");
                case IMStatusCode.TIMEOUT:
                    throw new TimeoutException();
                case IMStatusCode.BUSY:
                    throw new IOException("Resource Busy");
                case IMStatusCode.UNSUPPORTED_CLUSTER:
                    throw new DataException("Unsupported Cluster");
                case IMStatusCode.FAILSAFE_REQUIRED:
                    throw new InvalidOperationException("Failsafe required");
                case IMStatusCode.INVALID_IN_STATE:
                    throw new InvalidOperationException("The received request cannot be handled due to the current operational state of the device");
                default:
                    return false;
            }
        }
    }
}
