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
//
// WARNING: This file was auto-generated. Do not edit.

namespace MatterDotNet.PKI
{
    /// <summary>
    /// The level of certificate verification requested
    /// </summary>
    public enum VerificationLevel
    {
        /// <summary>
        /// Only official Matter certified devices
        /// </summary>
        CertifiedDevicesOnly = 0x0,
        /// <summary>
        /// Matter certified devices and devices using the CHIP Tool Test Certificate
        /// </summary>
        CertifiedDevicesOrCHIPTest = 0x1,
        /// <summary>
        /// Allow any device
        /// </summary>
        AnyDevice = 0x2
    }
}
