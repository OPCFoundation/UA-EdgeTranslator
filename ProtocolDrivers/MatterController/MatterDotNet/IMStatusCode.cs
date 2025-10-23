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

namespace MatterDotNet.Protocol.Payloads.Status
{
    public enum IMStatusCode : byte
    {
        SUCCESS                  = 0x00,
        FAILURE                  = 0x01,
        INVALID_SUBSCRIPTION     = 0x7d,
        UNSUPPORTED_ACCESS       = 0x7e,
        UNSUPPORTED_ENDPOINT     = 0x7f,
        INVALID_ACTION           = 0x80,
        UNSUPPORTED_COMMAND      = 0x81,
        Deprecated82             = 0x82,
        Deprecated83             = 0x83,
        Deprecated84             = 0x84,
        INVALID_COMMAND          = 0x85,
        UNSUPPORTED_ATTRIBUTE    = 0x86,
        CONSTRAINT_ERROR         = 0x87,
        UNSUPPORTED_WRITE        = 0x88,
        RESOURCE_EXHAUSTED       = 0x89,
        Deprecated8a             = 0x8a,
        NOT_FOUND                = 0x8b,
        UNREPORTABLE_ATTRIBUTE   = 0x8c,
        INVALID_DATA_TYPE        = 0x8d,
        Deprecated8e             = 0x8e,
        UNSUPPORTED_READ         = 0x8f,
        Deprecated90             = 0x90,
        Deprecated91             = 0x91,
        DATA_VERSION_MISMATCH    = 0x92,
        Deprecated93             = 0x93,
        TIMEOUT                  = 0x94,
        BUSY                     = 0x9c,
        Deprecatedc0             = 0xc0,
        Deprecatedc1             = 0xc1,
        Deprecatedc2             = 0xc2,
        UNSUPPORTED_CLUSTER      = 0xc3,
        Deprecatedc4             = 0xc4,
        NO_UPSTREAM_SUBSCRIPTION = 0xc5,
        NEEDS_TIMED_INTERACTION  = 0xc6,
        UNSUPPORTED_EVENT        = 0xc7,
        PATHS_EXHAUSTED          = 0xc8,
        TIMED_REQUEST_MISMATCH   = 0xc9,
        FAILSAFE_REQUIRED        = 0xca,
        INVALID_IN_STATE         = 0xcb,
        NO_COMMAND_RESPONSE      = 0xcc,
    }
}
