namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Integration tests that drive <see cref="ModbusTCPAsset"/> against an
    /// in-process mock Modbus TCP server. They lock in the fix for the
    /// entity -> function-code dispatch defect: every legal <c>modv:entity</c>
    /// must issue its own Modbus function code instead of silently falling
    /// through to Read Coils (function code 1).
    /// </summary>
    public sealed class ModbusTCPAssetEntityIntegrationTests
    {
        // Modbus function codes exercised by the tests.
        private const byte ReadCoilStatus = 1;
        private const byte ReadInputStatus = 2;
        private const byte ReadHoldingRegisters = 3;
        private const byte ReadInputRegisters = 4;
        private const byte ForceSingleCoil = 5;
        private const byte PresetSingleRegister = 6;
        private const byte PresetMultipleRegisters = 16;

        [Theory]
        [InlineData("HoldingRegister", ReadHoldingRegisters)]
        [InlineData("InputRegister", ReadInputRegisters)]
        [InlineData("Coil", ReadCoilStatus)]
        [InlineData("DiscreteInput", ReadInputStatus)]
        public async Task Read_dispatches_expected_function_code_for_each_entity(string entity, byte expectedFunctionCode)
        {
            using MockModbusTcpServer server = new();
            ModbusTCPAsset asset = new();
            await asset.ConnectAsync(IPAddress.Loopback.ToString(), server.Port);

            try
            {
                bool isRegister = entity is "HoldingRegister" or "InputRegister";
                AssetTag tag = new()
                {
                    Name = entity,
                    UnitID = 1,
                    Entity = entity,
                    Type = isRegister ? "Float" : "Boolean",
                    Address = isRegister ? "0?quantity=2" : "0?quantity=1"
                };

                object value = await asset.ReadAsync(tag);

                Assert.Contains(expectedFunctionCode, server.ReceivedFunctionCodes);

                if (isRegister)
                {
                    Assert.Equal(123.5f, Assert.IsType<float>(value));
                }
                else
                {
                    Assert.True(Assert.IsType<bool>(value));
                }
            }
            finally
            {
                await asset.DisconnectAsync();
            }
        }

        [Fact]
        public async Task Read_throws_for_unknown_entity()
        {
            ModbusTCPAsset asset = new();
            AssetTag tag = new()
            {
                Name = "unknown",
                UnitID = 1,
                Entity = "NotAModbusEntity",
                Type = "Float",
                Address = "0?quantity=2"
            };

            await Assert.ThrowsAsync<ArgumentException>(() => asset.ReadAsync(tag));
        }

        [Theory]
        [InlineData("InputRegister")]
        [InlineData("DiscreteInput")]
        public async Task Write_rejects_read_only_entities(string entity)
        {
            ModbusTCPAsset asset = new();
            AssetTag tag = new()
            {
                Name = entity,
                UnitID = 1,
                Entity = entity,
                Type = "Float",
                Address = "0?quantity=2"
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => asset.WriteAsync(tag, 1.0f));
        }

        [Fact]
        public async Task Write_throws_for_unknown_entity()
        {
            ModbusTCPAsset asset = new();
            AssetTag tag = new()
            {
                Name = "unknown",
                UnitID = 1,
                Entity = "NotAModbusEntity",
                Type = "Float",
                Address = "0?quantity=2"
            };

            await Assert.ThrowsAsync<ArgumentException>(() => asset.WriteAsync(tag, 1.0f));
        }

        [Fact]
        public async Task Write_dispatches_force_single_coil_for_coil_entity()
        {
            using MockModbusTcpServer server = new();
            ModbusTCPAsset asset = new();
            await asset.ConnectAsync(IPAddress.Loopback.ToString(), server.Port);

            try
            {
                AssetTag tag = new()
                {
                    Name = "coil",
                    UnitID = 1,
                    Entity = "Coil",
                    Type = "Boolean",
                    Address = "0?quantity=1"
                };

                await asset.WriteAsync(tag, true);

                Assert.Contains(ForceSingleCoil, server.ReceivedFunctionCodes);
            }
            finally
            {
                await asset.DisconnectAsync();
            }
        }

        [Fact]
        public async Task Write_dispatches_preset_multiple_registers_for_holding_register_entity()
        {
            using MockModbusTcpServer server = new();
            ModbusTCPAsset asset = new();
            await asset.ConnectAsync(IPAddress.Loopback.ToString(), server.Port);

            try
            {
                AssetTag tag = new()
                {
                    Name = "register",
                    UnitID = 1,
                    Entity = "HoldingRegister",
                    Type = "Float",
                    Address = "0?quantity=2"
                };

                await asset.WriteAsync(tag, 42.0f);

                Assert.Contains(PresetMultipleRegisters, server.ReceivedFunctionCodes);
            }
            finally
            {
                await asset.DisconnectAsync();
            }
        }

        [Fact]
        public async Task Write_big_endian_two_register_integer_uses_read_compatible_register_order()
        {
            using MockModbusTcpServer server = new();
            ModbusTCPAsset asset = new();
            await asset.ConnectAsync(IPAddress.Loopback.ToString(), server.Port);

            try
            {
                AssetTag tag = new()
                {
                    Name = "boardCount",
                    UnitID = 1,
                    Entity = "HoldingRegister",
                    Type = "Integer",
                    Address = "0?quantity=2",
                    IsBigEndian = true,
                    SwapPerWord = true,
                    Multiplier = 0.0f
                };

                await asset.WriteAsync(tag, 1234567);

                Assert.Contains(PresetMultipleRegisters, server.ReceivedFunctionCodes);
                ushort[] writtenRegisters = Assert.Single(server.RegisterWrites);
                byte[] wireBytes = ModbusValueCodec.RegistersToWireBytes(writtenRegisters);
                Assert.Equal(1234567, Assert.IsType<int>(ModbusValueCodec.Decode(tag, wireBytes)));
            }
            finally
            {
                await asset.DisconnectAsync();
            }
        }

        [Fact]
        public async Task Read_short_at_quantity_1_reads_a_single_register()
        {
            // A native 16-bit register value; the driver must read exactly one register
            // (no zero-padded neighbour / quantity=2 workaround required).
            using MockModbusTcpServer server = new(registerReadData: BitConverter.GetBytes((short)1234));
            ModbusTCPAsset asset = new();
            await asset.ConnectAsync(IPAddress.Loopback.ToString(), server.Port);

            try
            {
                AssetTag tag = new()
                {
                    Name = "level",
                    UnitID = 1,
                    Entity = "HoldingRegister",
                    Type = "Short",
                    Address = "0?quantity=1"
                };

                object value = await asset.ReadAsync(tag);

                Assert.Contains(ReadHoldingRegisters, server.ReceivedFunctionCodes);
                // Default multiplier (1.0) projects the 16-bit reading onto the Float-typed node.
                Assert.Equal(1234.0f, Assert.IsType<float>(value));
            }
            finally
            {
                await asset.DisconnectAsync();
            }
        }

        [Fact]
        public async Task Write_short_dispatches_preset_single_register()
        {
            using MockModbusTcpServer server = new();
            ModbusTCPAsset asset = new();
            await asset.ConnectAsync(IPAddress.Loopback.ToString(), server.Port);

            try
            {
                AssetTag tag = new()
                {
                    Name = "setpoint",
                    UnitID = 1,
                    Entity = "HoldingRegister",
                    Type = "Short",
                    Address = "0?quantity=1"
                };

                await asset.WriteAsync(tag, 4321.0f);

                Assert.Contains(PresetSingleRegister, server.ReceivedFunctionCodes);
                Assert.DoesNotContain(PresetMultipleRegisters, server.ReceivedFunctionCodes);
            }
            finally
            {
                await asset.DisconnectAsync();
            }
        }

        [Fact]
        public async Task ExecuteAction_coil_pulse_issues_write_single_coil()
        {
            using MockModbusTcpServer server = new();
            ModbusTCPAsset asset = new();
            await asset.ConnectAsync(IPAddress.Loopback.ToString(), server.Port);
            asset.SetActionTags(new Dictionary<string, AssetTag>
            {
                ["motorStart"] = new AssetTag { Name = "motorStart", UnitID = 1, Entity = "Coil", Type = "Boolean", Address = "3?quantity=1" }
            });

            try
            {

                // A nullary coil action defaults to writing "true" (coil ON = 0xFF00).
                AssetActionResult actionResult = await asset.ExecuteActionAsync(ActionMethod("motorStart"), new List<object>());
                string result = actionResult.Status;

                Assert.Equal("ok", result);
                Assert.Contains(server.Writes, w => w.FunctionCode == ForceSingleCoil && w.Address == 3 && w.Value == 0xFF00);
            }
            finally
            {
                await asset.DisconnectAsync();
            }
        }

        [Fact]
        public async Task ExecuteAction_holding_register_setpoint_writes_value_via_fc6()
        {
            using MockModbusTcpServer server = new();
            ModbusTCPAsset asset = new();
            await asset.ConnectAsync(IPAddress.Loopback.ToString(), server.Port);
            asset.SetActionTags(new Dictionary<string, AssetTag>
            {
                ["setTankHighLevel"] = new AssetTag { Name = "setTankHighLevel", UnitID = 1, Entity = "HoldingRegister", Type = "Short", Address = "0?quantity=1" }
            });

            try
            {

                AssetActionResult actionResult = await asset.ExecuteActionAsync(ActionMethod("setTankHighLevel"), new List<object> { 1234 });
                string result = actionResult.Status;

                Assert.Equal("ok", result);
                Assert.Contains(server.Writes, w => w.FunctionCode == PresetSingleRegister && w.Address == 0 && w.Value == 1234);
            }
            finally
            {
                await asset.DisconnectAsync();
            }
        }

        [Fact]
        public async Task ExecuteAction_on_read_only_entity_returns_error_and_writes_nothing()
        {
            using MockModbusTcpServer server = new();
            ModbusTCPAsset asset = new();
            await asset.ConnectAsync(IPAddress.Loopback.ToString(), server.Port);
            asset.SetActionTags(new Dictionary<string, AssetTag>
            {
                ["badAction"] = new AssetTag { Name = "badAction", UnitID = 1, Entity = "InputRegister", Type = "Short", Address = "0?quantity=1" }
            });

            try
            {

                AssetActionResult actionResult = await asset.ExecuteActionAsync(ActionMethod("badAction"), new List<object> { 5 });
                string result = actionResult.Status;

                Assert.NotEqual("ok", result);
                Assert.Contains("read-only", result, StringComparison.OrdinalIgnoreCase);
                Assert.Empty(server.Writes);
            }
            finally
            {
                await asset.DisconnectAsync();
            }
        }

        [Fact]
        public async Task ExecuteAction_unknown_action_returns_error()
        {
            ModbusTCPAsset asset = new();

            AssetActionResult actionResult = await asset.ExecuteActionAsync(ActionMethod("noSuchAction"), new List<object>());
                string result = actionResult.Status;

            Assert.NotEqual("ok", result);
            Assert.Contains("No Modbus form", result, StringComparison.OrdinalIgnoreCase);
        }

        private static MethodState ActionMethod(string name)
        {
            return new MethodState(null) { BrowseName = new QualifiedName(name) };
        }
    }

    /// <summary>
    /// Minimal in-process Modbus TCP server used by the tests. It speaks just
    /// enough of the wire protocol to answer the driver's read and write
    /// requests and records the function code of every request it receives so
    /// tests can assert on entity -> function-code dispatch.
    /// </summary>
    internal sealed class MockModbusTcpServer : IDisposable
    {
        private const int HeaderLength = 8;

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _serveTask;
        private readonly byte[] _registerReadData;
        private readonly byte[] _bitReadData;

        public MockModbusTcpServer(byte[] registerReadData = null, byte[] bitReadData = null)
        {
            _registerReadData = registerReadData ?? BitConverter.GetBytes(123.5f);
            _bitReadData = bitReadData ?? new byte[] { 0x01 };

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _serveTask = Task.Run(() => ServeAsync(_cts.Token));
        }

        public int Port { get; }

        public ConcurrentQueue<byte> ReceivedFunctionCodes { get; } = new();

        // Captured write requests: the function code, target address and the written
        // value (first register / coil word) so tests can assert the exact Modbus write.
        public ConcurrentQueue<(byte FunctionCode, ushort Address, ushort Value)> Writes { get; } = new();

        public ConcurrentQueue<ushort[]> RegisterWrites { get; } = new();

        public void Dispose()
        {
            _cts.Cancel();

            try
            {
                _listener.Stop();
            }
            catch (SocketException)
            {
                // listener already stopped
            }

            try
            {
                _serveTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // best-effort shutdown
            }

            _cts.Dispose();
        }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            try
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                using NetworkStream stream = client.GetStream();
                byte[] header = new byte[HeaderLength];

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!await ReadExactAsync(stream, header, HeaderLength, cancellationToken).ConfigureAwait(false))
                    {
                        break;
                    }

                    int length = (header[4] << 8) | header[5];
                    int payloadLength = Math.Max(0, length - 2);
                    byte[] payload = new byte[payloadLength];

                    if (payloadLength > 0 && !await ReadExactAsync(stream, payload, payloadLength, cancellationToken).ConfigureAwait(false))
                    {
                        break;
                    }

                    byte[] response = BuildResponse(header, payload);
                    await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // server shutting down
            }
            catch (IOException)
            {
                // client disconnected
            }
            catch (SocketException)
            {
                // listener stopped
            }
            catch (ObjectDisposedException)
            {
                // listener stopped
            }
        }

        private byte[] BuildResponse(byte[] header, byte[] payload)
        {
            ushort transactionId = (ushort)((header[0] << 8) | header[1]);
            byte unitId = header[6];
            byte functionCode = header[7];

            ReceivedFunctionCodes.Enqueue(functionCode);

            switch (functionCode)
            {
                case 1: // ReadCoilStatus
                case 2: // ReadInputStatus
                    return BuildReadResponse(transactionId, unitId, functionCode, _bitReadData);

                case 3: // ReadHoldingRegisters
                case 4: // ReadInputRegisters
                {
                    // Honour the requested register count so width-aware decoding
                    // receives exactly count*2 wire bytes.
                    int registerCount = payload.Length >= 4 ? (payload[2] << 8) | payload[3] : _registerReadData.Length / 2;
                    byte[] data = new byte[registerCount * 2];
                    Array.Copy(_registerReadData, 0, data, 0, Math.Min(_registerReadData.Length, data.Length));
                    return BuildReadResponse(transactionId, unitId, functionCode, data);
                }

                case 5:  // ForceSingleCoil
                case 6:  // PresetSingleRegister
                case 16: // PresetMultipleRegisters
                {
                    ushort address = payload.Length >= 2 ? (ushort)((payload[0] << 8) | payload[1]) : (ushort)0;
                    ushort value = functionCode == 16
                        ? (payload.Length >= 7 ? (ushort)((payload[5] << 8) | payload[6]) : (ushort)0)
                        : (payload.Length >= 4 ? (ushort)((payload[2] << 8) | payload[3]) : (ushort)0);
                    Writes.Enqueue((functionCode, address, value));
                    if (functionCode == 16 && payload.Length >= 6)
                    {
                        int registerCount = (payload[2] << 8) | payload[3];
                        ushort[] registers = new ushort[registerCount];
                        for (int i = 0; i < registerCount; i++)
                        {
                            int offset = 5 + (i * 2);
                            if (offset + 1 < payload.Length)
                            {
                                registers[i] = (ushort)((payload[offset] << 8) | payload[offset + 1]);
                            }
                        }

                        RegisterWrites.Enqueue(registers);
                    }
                    return BuildWriteEcho(transactionId, unitId, functionCode, payload);
                }

                default:
                    return BuildReadResponse(transactionId, unitId, functionCode, Array.Empty<byte>());
            }
        }

        private static byte[] BuildReadResponse(ushort transactionId, byte unitId, byte functionCode, byte[] data)
        {
            ushort length = (ushort)(3 + data.Length); // unitId + functionCode + byteCount + data
            byte[] response = new byte[HeaderLength + 1 + data.Length];

            response[0] = (byte)(transactionId >> 8);
            response[1] = (byte)(transactionId & 0xFF);
            response[2] = 0;
            response[3] = 0;
            response[4] = (byte)(length >> 8);
            response[5] = (byte)(length & 0xFF);
            response[6] = unitId;
            response[7] = functionCode;
            response[8] = (byte)data.Length; // byte count
            Array.Copy(data, 0, response, 9, data.Length);

            return response;
        }

        private static byte[] BuildWriteEcho(ushort transactionId, byte unitId, byte functionCode, byte[] payload)
        {
            ushort length = 6; // unitId + functionCode + 4 echoed bytes
            byte[] response = new byte[HeaderLength + 4];

            response[0] = (byte)(transactionId >> 8);
            response[1] = (byte)(transactionId & 0xFF);
            response[2] = 0;
            response[3] = 0;
            response[4] = (byte)(length >> 8);
            response[5] = (byte)(length & 0xFF);
            response[6] = unitId;
            response[7] = functionCode;

            // echo the address + value/quantity (first four payload bytes) back to the driver
            for (int i = 0; i < 4; i++)
            {
                response[HeaderLength + i] = i < payload.Length ? payload[i] : (byte)0;
            }

            return response;
        }

        private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken cancellationToken)
        {
            int offset = 0;

            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return false;
                }

                offset += read;
            }

            return true;
        }
    }
}
