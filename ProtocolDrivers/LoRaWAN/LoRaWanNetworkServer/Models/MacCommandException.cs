// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System;

    [Serializable]
    public class MacCommandException : Exception
    {
        public MacCommandException(string message)
            : base(message)
        {
        }

        public MacCommandException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public MacCommandException()
        {
        }
    }
}
