// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using System.Collections.Generic;

    public class SearchDevicesResult
    {
        public static readonly Dictionary<string, string> DeviceList = new();

        public static void AddDevice(DevEui devEUI, string payload)
        {
            string index = devEUI.ToString();
            if (!DeviceList.ContainsKey(index))
            {
                DeviceList.Add(index, payload);
            }
            else
            {
                DeviceList[index] = payload;
            }
        }
    }
}
