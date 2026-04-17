namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TitaniumAS.Opc.Client.Da;

    public class OPCDAAsset : IAsset
    {
        private OpcDaServer _server = null;
        private OpcDaGroup _group = null;
        private string _endpoint = string.Empty;
        private string _progId = string.Empty;
        private readonly Dictionary<string, OpcDaItem> _items = new();
        private readonly object _lock = new();

        public bool IsConnected => _server != null && _server.IsConnected;

        /// <summary>
        /// Sets the ProgId for the OPC DA server connection.
        /// Must be called before Connect(string ipAddress, int port).
        /// </summary>
        public void SetProgId(string progId)
        {
            _progId = progId;
        }

        public void Connect(string ipAddress, int port)
        {
            // For OPC DA, the ipAddress contains the hostname
            // The ProgId should be set via SetProgId() before calling Connect()
            if (string.IsNullOrEmpty(_progId))
            {
                throw new InvalidOperationException("ProgId must be set via SetProgId() before connecting.");
            }

            try
            {
                _endpoint = $"opc.da://{ipAddress}/{_progId}";

                _server = new OpcDaServer(_progId, ipAddress);
                _server.Connect();

                // Create a group for reading/writing items
                _group = _server.AddGroup("UAEdgeTranslatorGroup");
                _group.IsActive = true;
                _group.UpdateRate = TimeSpan.FromMilliseconds(100);

                Log.Logger.Information($"Connected to OPC DA server: {_progId} on {ipAddress}");
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Failed to connect to OPC DA server: {ex.Message}");
                throw;
            }
        }

        public void Disconnect()
        {
            try
            {
                if (_group != null)
                {
                    _server?.RemoveGroup(_group);
                    _group = null;
                }

                if (_server != null)
                {
                    _server.Disconnect();
                    _server = null;
                }

                _items.Clear();
                Log.Logger.Information("Disconnected from OPC DA server");
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error disconnecting from OPC DA server: {ex.Message}");
            }
        }

        public string GetRemoteEndpoint()
        {
            return _endpoint;
        }

        public object Read(AssetTag tag)
        {
            if (_server == null || !_server.IsConnected)
            {
                throw new InvalidOperationException("OPC DA server is not connected.");
            }

            lock (_lock)
            {
                try
                {
                    // Ensure the item is added to the group
                    OpcDaItem item = GetOrAddItem(tag.Address);

                    // Read the value synchronously
                    var values = _group.Read(_group.Items, OpcDaDataSource.Device);
                    var itemValue = values.FirstOrDefault(v => v.Item.ItemId == tag.Address);

                    if (itemValue == null || itemValue.Error.Failed)
                    {
                        Log.Logger.Warning($"Failed to read OPC DA item: {tag.Address}");
                        return null;
                    }

                    object value = itemValue.Value;

                    // Convert based on tag type
                    if (tag.Type == "Float" && value != null)
                    {
                        return Convert.ToSingle(value);
                    }
                    else if (tag.Type == "Boolean" && value != null)
                    {
                        return Convert.ToBoolean(value);
                    }
                    else if (tag.Type == "Integer" && value != null)
                    {
                        return Convert.ToInt32(value);
                    }
                    else if (tag.Type == "String" && value != null)
                    {
                        return value.ToString();
                    }

                    return value;
                }
                catch (Exception ex)
                {
                    Log.Logger.Error($"Error reading OPC DA item {tag.Address}: {ex.Message}");
                    return null;
                }
            }
        }

        public void Write(AssetTag tag, string value)
        {
            if (_server == null || !_server.IsConnected)
            {
                throw new InvalidOperationException("OPC DA server is not connected.");
            }

            lock (_lock)
            {
                try
                {
                    // Ensure the item is added to the group
                    OpcDaItem item = GetOrAddItem(tag.Address);

                    // Convert value based on tag type
                    object convertedValue;
                    if (tag.Type == "Float")
                    {
                        convertedValue = float.Parse(value);
                    }
                    else if (tag.Type == "Boolean")
                    {
                        convertedValue = bool.Parse(value);
                    }
                    else if (tag.Type == "Integer")
                    {
                        convertedValue = int.Parse(value);
                    }
                    else
                    {
                        convertedValue = value;
                    }

                    // Write the value
                    var results = _group.Write(new[] { item }, new[] { convertedValue });

                    if (results[0].Failed)
                    {
                        throw new Exception($"Failed to write to OPC DA item {tag.Address}: Error code {results[0]}");
                    }

                    Log.Logger.Debug($"Successfully wrote value {value} to OPC DA item {tag.Address}");
                }
                catch (Exception ex)
                {
                    Log.Logger.Error($"Error writing to OPC DA item {tag.Address}: {ex.Message}");
                    throw;
                }
            }
        }

        public string ExecuteAction(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs)
        {
            // OPC DA does not support method calls directly
            // Actions would need to be mapped to writes or other mechanisms
            Log.Logger.Warning("ExecuteAction is not supported for OPC DA");
            return null;
        }

        private OpcDaItem GetOrAddItem(string itemId)
        {
            if (_items.TryGetValue(itemId, out OpcDaItem existingItem))
            {
                return existingItem;
            }

            // Add the item to the group
            var itemDefinitions = new OpcDaItemDefinition[]
            {
                new OpcDaItemDefinition
                {
                    ItemId = itemId,
                    IsActive = true
                }
            };

            var addedResults = _group.AddItems(itemDefinitions);

            if (addedResults[0].Error.Failed)
            {
                throw new Exception($"Failed to add OPC DA item: {itemId}");
            }

            // Get the actual item from the group after adding
            var addedItem = _group.Items.First(i => i.ItemId == itemId);
            _items[itemId] = addedItem;
            return addedItem;
        }
    }
}
