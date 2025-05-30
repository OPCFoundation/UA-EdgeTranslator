// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// RXTimingSetupAns Upstream & RXTimingSetupReq Downstream.
    /// </summary>
    public class RXTimingSetupAnswer : MacCommand
    {
        public override int Length => 1;

        public RXTimingSetupAnswer()
        {
            Cid = Cid.RXTimingCmd;
        }

        public override IEnumerable<byte> ToBytes()
        {
            yield return (byte)Cid;
        }

        public override string ToString()
        {
            return $"Type: {Cid} Answer";
        }
    }
}
