//-----------------------------------------------------------------------
// <copyright file="BluetoothDevice.windows.cs" company="In The Hand Ltd">
//   Copyright (c) 2018-23 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

#if WINDOWS

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;

namespace InTheHand.Bluetooth
{
    partial class BluetoothDeviceWindows : IBluetoothDevice, IDisposable
    {
        public BluetoothLEDevice NativeDevice { get; set; }

        public ConcurrentDictionary<int, IDisposable> NativeDisposeList { get; set; } = new ConcurrentDictionary<int, IDisposable>();

        public ulong LastKnownAddress { get; set; }

        public string Id { get; set; }

        public IRemoteGattServer GattServer { get; set; }

        private string _cachedId;
        private bool _disposed;

        public event EventHandler GattServerDisconnected;

        public void OnGattServerDisconnected()
        {
            GattServerDisconnected?.Invoke(this, EventArgs.Empty);
        }

        ~BluetoothDeviceWindows()
        {
            Dispose(disposing: false);
        }

        public async Task<bool> CreateNativeInstance()
        {
            if (NativeDisposeList.TryGetValue(GetHashCode(), out IDisposable existingItem))
            {
                if (existingItem == null)
                {
                    // The native object was disposed as the result of a call to RemoteGattServer.Disconnect.
                    // we need to create another one.
                    BluetoothLEDevice nativeDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(LastKnownAddress);
                    if (nativeDevice != null)
                    {
                        AddDisposableObject(this, nativeDevice);
                        NativeDevice = nativeDevice;
                        LastKnownAddress = nativeDevice.BluetoothAddress;
                        Id = GetId();

                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Adds a native (IDisposable) object to the dispose list</summary>
        /// <param name="container">This is generally the managed object that contains the native object but it can be anything that can serve as a unique key.</param>
        /// <param name="disposableObject">The native object that we will disposed when the user requests a disconnect</param>
        internal void AddDisposableObject(object container, IDisposable disposableObject)
        {
            if (NativeDisposeList.TryGetValue(container.GetHashCode(), out IDisposable existingValue))
            {
                NativeDisposeList.TryUpdate(container.GetHashCode(), disposableObject, existingValue);
            }
            else
            {
                NativeDisposeList.TryAdd(container.GetHashCode(), disposableObject);
            }
        }

        /// <summary>Called in RemoteServer.PlatformCleanup to dispose all of the native object that have been collected.</summary>
        internal void DisposeAllNativeObjects()
        {
            Dictionary<int, IDisposable> itemsDisposed = new Dictionary<int, IDisposable>();
            foreach (var kv in NativeDisposeList)
            {
                try
                {
                    kv.Value?.Dispose();
                }
                catch (TargetInvocationException e) when (e.InnerException is ObjectDisposedException) { }
                catch (ObjectDisposedException) { }
                catch (InvalidComObjectException) { }
                itemsDisposed.Add(kv.Key, kv.Value);
            }

            foreach (var kv in itemsDisposed)
            {
                IDisposable val;
                NativeDisposeList.TryRemove(kv.Key, out val);
            }
        }


        /// <summary>Checks if the native object for this container has been disposed.</summary>
        /// <param name="container">This is generally the managed object that contains the native object but it can be anything that can serve as a unique key.</param>
        /// <returns>True if the container exists and it's native object has been disposed.</returns>
        internal bool IsDisposedItem(object container)
        {
            if (NativeDisposeList.TryGetValue(container.GetHashCode(), out IDisposable existingItem))
            {
                return existingItem == null;
            }

            return false;
        }

        internal string GetId()
        {
            if (IsDisposedItem(this))
                return _cachedId;

            _cachedId = NativeDevice.BluetoothAddress.ToString("X6");

            return _cachedId;
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                DisposeAllNativeObjects();
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}

#endif
