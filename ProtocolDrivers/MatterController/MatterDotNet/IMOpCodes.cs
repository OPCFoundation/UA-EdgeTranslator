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

namespace MatterDotNet.Protocol.Payloads.OpCodes
{
    internal enum IMOpCodes
    {
        StatusResponse = 0x01,
        ReadRequest = 0x02,
        SubscribeRequest = 0x03,
        SubscribeResponse = 0x04,
        ReportData = 0x05,
        WriteRequest = 0x06,
        WriteResponse = 0x07,
        InvokeRequest = 0x08,
        InvokeResponse = 0x09,
        TimedRequest = 0x0A,
    }
}
