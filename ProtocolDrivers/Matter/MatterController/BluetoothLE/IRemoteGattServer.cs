//-----------------------------------------------------------------------
// <copyright file="RemoteGattServer.cs" company="In The Hand Ltd">
//   Copyright (c) 2018-25 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

namespace InTheHand.Bluetooth
{
    using System.Threading.Tasks;

    public interface IRemoteGattServer
    {
        public IBluetoothDevice Device { get; set; }

        public bool IsConnected { get; set; }

        public int Mtu { get; set; }

        public Task<IGattService> GetPrimaryServiceAsync(BluetoothUuid service);

        public Task ConnectAsync();
    }
}
