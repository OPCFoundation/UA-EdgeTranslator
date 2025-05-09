// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;

    /// <summary>
    /// Results of a <see cref="LoRaDeviceAPIServiceBase.SearchDevicesAsync"/> call.
    /// </summary>
    public class SearchDevicesResult : IReadOnlyList<DeviceInfo>
    {
        // Nounce:DevEUI:GatewayID
        public static readonly Dictionary<string, Tuple<DevNonce, DevEui, string>> DeviceList = [];

        /// <summary>
        /// Gets list of devices that match the criteria.
        /// </summary>
        public IReadOnlyList<DeviceInfo> Devices { get; } = Array.Empty<DeviceInfo>();

        /// <summary>
        /// Gets or sets a value indicating whether the dev nonce was already used.
        /// </summary>
        public bool IsDevNonceAlreadyUsed { get; set; }

        public string RefusedMessage { get; set; }

        public int Count => Devices.Count;

        public DeviceInfo this[int index] => Devices[index];

        public SearchDevicesResult() { }

        public SearchDevicesResult(IReadOnlyList<DeviceInfo> devices)
        {
            Devices = devices ?? Array.Empty<DeviceInfo>();
        }

        public IEnumerator<DeviceInfo> GetEnumerator() =>
            Devices.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() =>
            GetEnumerator();

        public static SearchDevicesResult SearchForDevice(string gatewayID, DevEui devEUI, DevNonce? nounce = null)
        {
            string index = gatewayID + ":" + devEUI.ToString();
            if (DeviceList.ContainsKey(index))
            {
                return new SearchDevicesResult([new DeviceInfo(devEUI)]) { IsDevNonceAlreadyUsed = (DeviceList[index].Item1.AsUInt16 != 0) };
            }

            return null;
        }

        public static void AddOrUpdateDevice(string gatewayID, DevEui devEUI, DevNonce nounce)
        {
            string index = gatewayID + ":" + devEUI.ToString();
            if (!DeviceList.ContainsKey(index))
            {
                DeviceList.Add(index, new Tuple<DevNonce, DevEui, string>(nounce, devEUI, gatewayID));
            }
            else
            {
                DeviceList[index] = new Tuple<DevNonce, DevEui, string>(nounce, devEUI, gatewayID);
            }
        }
    }
}
