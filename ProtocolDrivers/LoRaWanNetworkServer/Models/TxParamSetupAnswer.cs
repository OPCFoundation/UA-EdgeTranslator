// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using LoRaTools;

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System.Collections.Generic;

    public sealed class TxParamSetupAnswer : MacCommand
    {
        public TxParamSetupAnswer()
        {
            Cid = Cid.TxParamSetupCmd;
        }

        public override int Length => 1;

        public override IEnumerable<byte> ToBytes() =>
            new byte[] { (byte)Cid };

        public override string ToString() =>
            $"Type: {Cid} Answer";
    }
}
