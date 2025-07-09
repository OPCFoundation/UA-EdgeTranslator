// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using Opc.Ua.Edge.Translator.ProtocolDrivers.LoRaWanNetworkServer.Models;
    using System;
    using System.Collections.Generic;

    public class SearchDevicesResult
    {
        public static readonly Dictionary<string, string> DeviceList = new();

        public IReadOnlyList<DeviceInfo> Devices { get; } = Array.Empty<DeviceInfo>();

        public SearchDevicesResult(IReadOnlyList<DeviceInfo> devices)
        {
            Devices = devices ?? Array.Empty<DeviceInfo>();
        }

        public static SearchDevicesResult SearchForDevice(DevEui devEUI, DevNonce? nounce = null)
        {
            string index = devEUI.ToString();

            if (DeviceList.ContainsKey(index))
            {
                return new SearchDevicesResult([new DeviceInfo(devEUI, AppKey.Parse(DeviceList[index]))]);
            }

            return null;
        }

        public static void AddDevice(DevEui devEUI, AppKey appKey)
        {
            string index = devEUI.ToString();
            if (!DeviceList.ContainsKey(index))
            {
                DeviceList.Add(index, appKey.ToString());
            }
            else
            {
                DeviceList[index] = appKey.ToString();
            }
        }
    }
}
