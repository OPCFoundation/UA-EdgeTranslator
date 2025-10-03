//-----------------------------------------------------------------------
// <copyright file="BluetoothRemoteGATTServer.windows.cs" company="In The Hand Ltd">
//   Copyright (c) 2018-25 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

#if WINDOWS

using System;
using System.Security;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;

namespace InTheHand.Bluetooth
{
    partial class RemoteGattServerWindows : IRemoteGattServer
    {
        public IBluetoothDevice Device { get; set; } = new BluetoothDeviceWindows();

        public bool IsConnected { get; set; }

        public int Mtu { get; set; }

        private void NativeDevice_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                ((BluetoothDeviceWindows)Device).OnGattServerDisconnected();
            }
        }

        public async Task ConnectAsync()
        {
            // Ensure that our native objects have not been disposed.
            // If they have, re-create the native device object.
            if (await ((BluetoothDeviceWindows)Device).CreateNativeInstance())
            {
                ((BluetoothDeviceWindows)Device).NativeDevice.ConnectionStatusChanged += NativeDevice_ConnectionStatusChanged;
            }

            var status = await ((BluetoothDeviceWindows)Device).NativeDevice.RequestAccessAsync();
            if (status == Windows.Devices.Enumeration.DeviceAccessStatus.Allowed)
            {
                ((BluetoothDeviceWindows)Device).LastKnownAddress = ((BluetoothDeviceWindows)Device).NativeDevice.BluetoothAddress;
                var session = await Windows.Devices.Bluetooth.GenericAttributeProfile.GattSession.FromDeviceIdAsync(((BluetoothDeviceWindows)Device).NativeDevice.BluetoothDeviceId);
                if (session != null)
                {
                    Mtu = session.MaxPduSize;
                    session.MaxPduSizeChanged += Session_MaxPduSizeChanged;
                    // Even though this is a local variable, we still want to add it to our dispose list so
                    // we don't have to rely on the GC to clean it up.
                    ((BluetoothDeviceWindows)Device).AddDisposableObject(this, session);

                    if (session.CanMaintainConnection)
                        session.MaintainConnection = true;
                }

                // need to request something to force a connection
                for (int i = 0; i < 3; i++)
                {
                    var services = await ((BluetoothDeviceWindows)Device).NativeDevice.GetGattServicesForUuidAsync(GattServiceUuids.GenericAccess, Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);
                    if (services.Status == Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success)
                    {
                        foreach (var service in services.Services)
                        {
                            service.Dispose();
                        }
                        break;
                    }
                }
            }
            else
            {
                throw new SecurityException();
            }
        }

        public void Session_MaxPduSizeChanged(Windows.Devices.Bluetooth.GenericAttributeProfile.GattSession sender, object args)
        {
            System.Diagnostics.Debug.WriteLine($"MaxPduSizeChanged Size:{sender.MaxPduSize}");
            Mtu = sender.MaxPduSize;
        }

        public async Task<IGattService> GetPrimaryServiceAsync(BluetoothUuid service)
        {
            try
            {
                if (await ((BluetoothDeviceWindows)Device).CreateNativeInstance())
                {
                    ((BluetoothDeviceWindows)Device).NativeDevice.ConnectionStatusChanged += NativeDevice_ConnectionStatusChanged;
                }

                var result = ((BluetoothDeviceWindows)Device).NativeDevice.GetGattService(service.Value);
                if (result == null)
                {
                    return null;
                }

                return new GattServiceWindows(Device, result, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetGattService Exception: {ex.Message}, {ex.HResult}");
                return null;
            }
        }
    }
}

#endif
