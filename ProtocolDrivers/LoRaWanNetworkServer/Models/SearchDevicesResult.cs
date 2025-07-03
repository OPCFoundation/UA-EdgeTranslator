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
        public static readonly List<string> DeviceList = new();

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

        public static SearchDevicesResult SearchForDevice(DevEui devEUI, DevNonce? nounce = null)
        {
            string index = devEUI.ToString();

            // check if we allow all devices to be added
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ALLOW_ALL_LORAWAN_DEVICES")))
            {
                AddDevice(devEUI);
            }

            if (DeviceList.Contains(index))
            {
                return new SearchDevicesResult([new DeviceInfo(devEUI)]);
            }

            return null;
        }

        public static void AddDevice(DevEui devEUI)
        {
            string index = devEUI.ToString();
            if (!DeviceList.Contains(index))
            {
                DeviceList.Add(index);
            }
        }
    }
}
