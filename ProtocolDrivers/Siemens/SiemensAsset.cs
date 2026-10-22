namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using Sharp7;
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Globalization;
    using System.Text;
    using System.Xml;
    using S7Helpers = Sharp7.S7;

    /// <summary>
    /// A SIMATIC S7 controller read and written as the W3C WoT S7Comm binding
    /// describes: the endpoint's rack and slot (or the Thing's TSAPs), memory-area
    /// addresses, S7 data types in big-endian order, single-bit BOOL access and
    /// linear value conversion.
    /// </summary>
    public class SiemensAsset : IAsset
    {
        // S7Comm area codes; Sharp7's enum lacks peripheral (0x80) and local data (0x86).
        private const int AreaPeripheral = 0x80;
        private const int AreaLocalData = 0x86;

        public S7Client S7 = null;

        private string _host = string.Empty;
        private int _port = S7Endpoint.DefaultPort;
        private int _rack = S7Endpoint.DefaultRack;
        private int _slot = S7Endpoint.DefaultSlot;
        private S7ConnectionTerms _terms = new();

        public bool IsConnected { get; private set; } = false;

        /// <summary>The Thing-level connection terms (controller family, TSAPs, PDU size).</summary>
        public S7ConnectionTerms Terms => _terms;

        /// <summary>Connects to the CPU an <c>s7</c> endpoint names, with the Thing's connection terms.</summary>
        public void Connect(S7Endpoint endpoint, S7ConnectionTerms terms)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            _host = endpoint.Host;
            _port = endpoint.Port;
            _rack = endpoint.Rack;
            _slot = endpoint.Slot;
            _terms = terms ?? new S7ConnectionTerms();
            ConnectCore();
        }

        public void Connect(string ipAddress, int rack, int slot)
        {
            Connect(new S7Endpoint(ipAddress, S7Endpoint.DefaultPort, rack, slot), _terms);
        }

        private void ConnectCore()
        {
            try
            {
                S7 = new S7Client { PLCPort = _port };
                if (_terms.PduSize.HasValue)
                {
                    S7.PduSizeRequested = _terms.PduSize.Value;
                }

                int result;
                if (_terms.UsesTsaps)
                {
                    // A non-zero TSAP overrides the rack and slot of the endpoint.
                    S7.SetConnectionParams(_host, _terms.LocalTsap ?? 0, _terms.RemoteTsap ?? 0);
                    result = S7.Connect();
                }
                else
                {
                    result = S7.ConnectTo(_host, _rack, _slot);
                }

                if (result == 0)
                {
                    Log.Logger.Information("Connected to Siemens S7 at {ip}:{port} ({route})", _host, _port, Route());
                    IsConnected = true;
                }
                else
                {
                    Log.Logger.Error("Failed to connect to Siemens S7 at {ip}:{port} ({route}): {err}", _host, _port, Route(), S7.ErrorText(result));
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Siemens S7 connect failed for {ip}:{port} ({route})", _host, _port, Route());
            }
        }

        private string Route() => _terms.UsesTsaps
            ? $"local TSAP 0x{_terms.LocalTsap ?? 0:X4}, remote TSAP 0x{_terms.RemoteTsap ?? 0:X4}"
            : $"rack {_rack}, slot {_slot}";

        private void DisconnectCore()
        {
            if (S7 != null)
            {
                S7.Disconnect();
                S7 = null;
            }

            IsConnected = false;
        }

        /// <summary>
        /// The controller as <c>host:port</c>; a reconnect through
        /// <see cref="ConnectAsync(string, int, CancellationToken)"/> keeps the rack,
        /// slot and connection terms of the endpoint.
        /// </summary>
        public string GetRemoteEndpoint()
        {
            if (string.IsNullOrEmpty(_host))
            {
                return _host;
            }

            return string.Concat(_host, ":", _port.ToString(CultureInfo.InvariantCulture));
        }

        private object ReadCore(AssetTag tag)
        {
            (S7Form form, S7Address address) = ResolveTag(tag);
            if (address == null)
            {
                return null;
            }

            S7DataType type = address.Type.Value;
            ApplyTimeout(form);

            byte[] buffer = new byte[WireSize(address)];
            int err = S7.ReadArea(AreaCode(address), DbNumber(address), Start(address), Amount(address), (int)WordLength(address), buffer);
            if (err != 0)
            {
                Log.Logger.Error("S7 read failed for {tag}: {err}", tag.Name, S7.ErrorText(err));
                return null;
            }

            return form.ToApplicationValue(Decode(buffer, type, address));
        }

        private void WriteCore(AssetTag tag, object value)
        {
            (S7Form form, S7Address address) = ResolveTag(tag);
            if (address == null || value == null)
            {
                return;
            }

            S7DataType type = address.Type.Value;
            ApplyTimeout(form);

            // A BOOL is written as a single bit, so the neighboring bits are not affected.
            byte[] buffer = type == S7DataType.BOOL
                ? new[] { (byte)(ConvertToBool(value) ? 1 : 0) }
                : Encode(form.ToControllerValue(value, type), type, address);

            int err = S7.WriteArea(AreaCode(address), DbNumber(address), Start(address), Amount(address), (int)WordLength(address), buffer);
            if (err != 0)
            {
                Log.Logger.Error("S7 write failed for {tag}: {err}", tag.Name, S7.ErrorText(err));
            }
        }

        private string ExecuteActionCore(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs)
        {
            return null;
        }

        // ---- addressing ---------------------------------------------------

        /// <summary>
        /// The form stored with the tag and its resolved address; the address is null
        /// when the form violates the binding, in which case nothing is read or written.
        /// </summary>
        private (S7Form Form, S7Address Address) ResolveTag(AssetTag tag)
        {
            S7Form form = ParseForm(tag);
            if (form == null)
            {
                return (null, null);
            }

            string problem = form.TryResolveAddress(out S7Address resolved) ?? form.Validate(_terms.Controller);
            if (problem != null)
            {
                Log.Logger.Error("S7 form for {tag} rejected: {problem}", tag.Name, problem);
                return (form, null);
            }

            return (form, resolved);
        }

        private static S7Form ParseForm(AssetTag tag)
        {
            if (string.IsNullOrEmpty(tag?.Address))
            {
                return null;
            }

            // CreateTag stores the full form as JSON in AssetTag.Address so every
            // address term survives the round trip.
            try
            {
                return JsonConvert.DeserializeObject<S7Form>(tag.Address);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Could not parse S7 form for tag {tag}", tag.Name);
                return null;
            }
        }

        private void ApplyTimeout(S7Form form)
        {
            if (form?.Timeout is long timeout && timeout > 0)
            {
                S7.RecvTimeout = (int)Math.Min(int.MaxValue, timeout);
                S7.SendTimeout = S7.RecvTimeout;
            }
        }

        /// <summary>The S7Comm area code of an address; every area of the binding is supported.</summary>
        public static int AreaCode(S7Address address) => address.Area switch
        {
            S7MemoryArea.I => (int)S7Area.PE,
            S7MemoryArea.Q => (int)S7Area.PA,
            S7MemoryArea.M => (int)S7Area.MK,
            S7MemoryArea.DB => (int)S7Area.DB,

            // Instance data blocks are data blocks in absolute addressing.
            S7MemoryArea.DBI => (int)S7Area.DB,
            S7MemoryArea.C => (int)S7Area.CT,
            S7MemoryArea.T => (int)S7Area.TM,
            S7MemoryArea.D => AreaPeripheral,
            S7MemoryArea.LD => AreaLocalData,
            _ => throw new NotSupportedException($"S7 memory area {address.Area} is not supported.")
        };

        private static int DbNumber(S7Address address) => address.IsDataBlock ? address.DbNumber ?? 0 : 0;

        private static bool IsCounterOrTimer(S7Address address) => address.Area == S7MemoryArea.C || address.Area == S7MemoryArea.T;

        private static S7WordLength WordLength(S7Address address)
        {
            if (address.Type == S7DataType.BOOL)
            {
                return S7WordLength.Bit;
            }

            return address.Area switch
            {
                S7MemoryArea.C => S7WordLength.Counter,
                S7MemoryArea.T => S7WordLength.Timer,
                _ => S7WordLength.Byte
            };
        }

        /// <summary>The start of the transfer: the bit address of a BOOL, the number of a counter or timer, else the byte offset.</summary>
        public static int Start(S7Address address) =>
            address.Type == S7DataType.BOOL ? (address.ByteOffset * 8) + address.EffectiveBitOffset : address.ByteOffset;

        /// <summary>The element count of the transfer: one bit, one counter or timer, or the bytes of the value.</summary>
        private static int Amount(S7Address address) =>
            address.Type == S7DataType.BOOL || IsCounterOrTimer(address) ? 1 : WireSize(address);

        /// <summary>The bytes a value occupies on the wire; strings use their declared maximum length.</summary>
        public static int WireSize(S7Address address) =>
            S7DataTypes.ByteSize(address.Type.Value, address.EffectiveLength);

        // ---- Decode -------------------------------------------------------

        public static object Decode(byte[] buffer, S7DataType type, S7Address address = null)
        {
            switch (type)
            {
                // A single-bit read returns the bit in the least significant bit.
                case S7DataType.BOOL:   return (buffer[0] & 1) != 0;
                case S7DataType.BYTE:   return buffer[0];
                case S7DataType.USINT:  return buffer[0];
                case S7DataType.SINT:   return (sbyte)buffer[0];
                case S7DataType.CHAR:   return ((char)buffer[0]).ToString();

                case S7DataType.WORD:   return BinaryPrimitives.ReadUInt16BigEndian(buffer);
                case S7DataType.UINT:   return BinaryPrimitives.ReadUInt16BigEndian(buffer);
                case S7DataType.INT:    return BinaryPrimitives.ReadInt16BigEndian(buffer);
                case S7DataType.WCHAR:  return ((char)BinaryPrimitives.ReadUInt16BigEndian(buffer)).ToString();

                case S7DataType.DWORD:  return BinaryPrimitives.ReadUInt32BigEndian(buffer);
                case S7DataType.UDINT:  return BinaryPrimitives.ReadUInt32BigEndian(buffer);
                case S7DataType.DINT:   return BinaryPrimitives.ReadInt32BigEndian(buffer);
                case S7DataType.REAL:   return S7Helpers.GetRealAt(buffer, 0);

                case S7DataType.LWORD:  return BinaryPrimitives.ReadUInt64BigEndian(buffer);
                case S7DataType.ULINT:  return BinaryPrimitives.ReadUInt64BigEndian(buffer);
                case S7DataType.LINT:   return BinaryPrimitives.ReadInt64BigEndian(buffer);
                case S7DataType.LREAL:  return S7Helpers.GetLRealAt(buffer, 0);

                case S7DataType.STRING:  return DecodeS7String(buffer);
                case S7DataType.WSTRING: return DecodeS7WString(buffer);

                // Durations, times and dates as the ISO 8601 strings the binding recommends.
                case S7DataType.DATE:   return DecodeDate(buffer);
                case S7DataType.TIME:   return XmlConvert.ToString(TimeSpan.FromMilliseconds(BinaryPrimitives.ReadInt32BigEndian(buffer)));
                case S7DataType.LTIME:  return XmlConvert.ToString(DecodeLTime(buffer));
                case S7DataType.S5TIME: return XmlConvert.ToString(DecodeS5Time(buffer));
                case S7DataType.TOD:    return DecodeTod(buffer);
                case S7DataType.LTOD:   return DecodeLTod(buffer);
                case S7DataType.DT:     return DecodeDt(buffer);
                case S7DataType.LDT:    return DecodeLdt(buffer);
                case S7DataType.DTL:    return DecodeDtl(buffer);

                default: throw new NotSupportedException($"S7 type {type} is not supported.");
            }
        }

        /// <summary>
        /// Reads a STRING: the declared maximum length, the current length, then the
        /// characters; the current length decides how many characters are valid.
        /// </summary>
        public static string DecodeS7String(byte[] buffer)
        {
            if (buffer.Length < 2) return string.Empty;
            int len = Math.Min(buffer[1], buffer[0]);
            int avail = Math.Min(len, buffer.Length - 2);
            return Encoding.ASCII.GetString(buffer, 2, avail);
        }

        public static string DecodeS7WString(byte[] buffer)
        {
            if (buffer.Length < 4) return string.Empty;
            // Header: 2 words. word[0] = max length, word[1] = current length (in chars).
            int maxChars = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(0));
            int currentChars = Math.Min(BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2)), maxChars);
            int availChars = Math.Min(currentChars, (buffer.Length - 4) / 2);
            return Encoding.BigEndianUnicode.GetString(buffer, 4, availChars * 2);
        }

        private static string DecodeDate(byte[] buffer)
        {
            int days = BinaryPrimitives.ReadUInt16BigEndian(buffer);
            return new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
                   .AddDays(days)
                   .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static string DecodeTod(byte[] buffer)
        {
            uint ms = BinaryPrimitives.ReadUInt32BigEndian(buffer);
            return TimeSpan.FromMilliseconds(ms)
                           .ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
        }

        private static string DecodeLTod(byte[] buffer)
        {
            ulong ns = BinaryPrimitives.ReadUInt64BigEndian(buffer);
            // 100-ns ticks; clamp to 24h so the formatter stays in TOD range.
            ulong ticks = (ns / 100) % (24UL * 3600 * 10_000_000);
            return new TimeSpan((long)ticks)
                   .ToString(@"hh\:mm\:ss\.fffffff", CultureInfo.InvariantCulture);
        }

        private static TimeSpan DecodeLTime(byte[] buffer)
        {
            // LTIME is signed nanoseconds; convert via 100-ns ticks.
            long ns = BinaryPrimitives.ReadInt64BigEndian(buffer);
            return new TimeSpan(ns / 100);
        }

        private static TimeSpan DecodeS5Time(byte[] buffer)
        {
            // S5TIME: word laid out as [TB(4)] [HUNDREDS(4)] [TENS(4)] [UNITS(4)].
            // Time base (TB): 0=10ms, 1=100ms, 2=1s, 3=10s.
            ushort raw = BinaryPrimitives.ReadUInt16BigEndian(buffer);
            int tb = (raw >> 12) & 0x03;
            int h = (raw >> 8) & 0x0F;
            int t = (raw >> 4) & 0x0F;
            int u = raw & 0x0F;
            int value = (h * 100) + (t * 10) + u;
            int unitMs = tb switch { 0 => 10, 1 => 100, 2 => 1000, _ => 10000 };
            return TimeSpan.FromMilliseconds((long)value * unitMs);
        }

        private static string DecodeDt(byte[] buffer)
        {
            // 8 bytes BCD: YY MM DD HH mm ss MSh MSl|DOW
            int year = BcdToInt(buffer[0]);
            year += (year >= 90) ? 1900 : 2000;
            int month = BcdToInt(buffer[1]);
            int day = BcdToInt(buffer[2]);
            int hour = BcdToInt(buffer[3]);
            int min = BcdToInt(buffer[4]);
            int sec = BcdToInt(buffer[5]);
            int msHi = BcdToInt(buffer[6]);
            int msLoNibble = (buffer[7] >> 4) & 0x0F;
            int millis = (msHi * 10) + msLoNibble;
            try
            {
                return new DateTime(year, month, day, hour, min, sec, millis, DateTimeKind.Unspecified)
                       .ToString("o", CultureInfo.InvariantCulture);
            }
            catch
            {
                return $"{year:D4}-{month:D2}-{day:D2}T{hour:D2}:{min:D2}:{sec:D2}.{millis:D3}";
            }
        }

        private static string DecodeLdt(byte[] buffer)
        {
            // LDT: signed nanoseconds since 1970-01-01 00:00:00 UTC.
            long ns = BinaryPrimitives.ReadInt64BigEndian(buffer);
            long ticks = ns / 100;
            try
            {
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                       .AddTicks(ticks)
                       .ToString("o", CultureInfo.InvariantCulture);
            }
            catch
            {
                return ns.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string DecodeDtl(byte[] buffer)
        {
            // DTL: year(WORD) month(BYTE) day(BYTE) dow(BYTE)
            //      hour(BYTE) min(BYTE) sec(BYTE) nanoseconds(DWORD).
            if (buffer.Length < 12) return string.Empty;
            int year = BinaryPrimitives.ReadUInt16BigEndian(buffer);
            int month = buffer[2];
            int day = buffer[3];
            // buffer[4] = day of week (1..7), ignored for ISO formatting.
            int hour = buffer[5];
            int min = buffer[6];
            int sec = buffer[7];
            uint ns = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(8));
            try
            {
                DateTime dt = new(year, month, day, hour, min, sec, DateTimeKind.Unspecified);
                dt = dt.AddTicks(ns / 100);
                return dt.ToString("o", CultureInfo.InvariantCulture);
            }
            catch
            {
                return $"{year:D4}-{month:D2}-{day:D2}T{hour:D2}:{min:D2}:{sec:D2}.{ns:D9}";
            }
        }

        private static int BcdToInt(byte b) => (((b >> 4) & 0x0F) * 10) + (b & 0x0F);

        // ---- Encode -------------------------------------------------------

        public static byte[] Encode(object value, S7DataType type, S7Address address)
        {
            byte[] buffer = new byte[S7DataTypes.ByteSize(type, address?.EffectiveLength ?? S7Form.DefaultStringLength)];
            switch (type)
            {
                case S7DataType.BOOL:
                    buffer[0] = (byte)(ConvertToBool(value) ? 1 : 0);
                    return buffer;
                case S7DataType.BYTE:
                case S7DataType.USINT:
                    buffer[0] = Convert.ToByte(value, CultureInfo.InvariantCulture);
                    return buffer;
                case S7DataType.SINT:
                    buffer[0] = (byte)Convert.ToSByte(value, CultureInfo.InvariantCulture);
                    return buffer;
                case S7DataType.CHAR:
                    {
                        string sc = value?.ToString() ?? string.Empty;
                        buffer[0] = (byte)(sc.Length > 0 ? sc[0] : (char)0);
                        return buffer;
                    }

                case S7DataType.WORD:
                case S7DataType.UINT:
                    BinaryPrimitives.WriteUInt16BigEndian(buffer, Convert.ToUInt16(value, CultureInfo.InvariantCulture));
                    return buffer;
                case S7DataType.INT:
                    BinaryPrimitives.WriteInt16BigEndian(buffer, Convert.ToInt16(value, CultureInfo.InvariantCulture));
                    return buffer;
                case S7DataType.WCHAR:
                    {
                        string sc = value?.ToString() ?? string.Empty;
                        ushort cp = (ushort)(sc.Length > 0 ? sc[0] : (char)0);
                        BinaryPrimitives.WriteUInt16BigEndian(buffer, cp);
                        return buffer;
                    }

                case S7DataType.DWORD:
                case S7DataType.UDINT:
                    BinaryPrimitives.WriteUInt32BigEndian(buffer, Convert.ToUInt32(value, CultureInfo.InvariantCulture));
                    return buffer;
                case S7DataType.DINT:
                    BinaryPrimitives.WriteInt32BigEndian(buffer, Convert.ToInt32(value, CultureInfo.InvariantCulture));
                    return buffer;
                case S7DataType.REAL:
                    S7Helpers.SetRealAt(buffer, 0, Convert.ToSingle(value, CultureInfo.InvariantCulture));
                    return buffer;

                case S7DataType.LWORD:
                case S7DataType.ULINT:
                    BinaryPrimitives.WriteUInt64BigEndian(buffer, Convert.ToUInt64(value, CultureInfo.InvariantCulture));
                    return buffer;
                case S7DataType.LINT:
                    BinaryPrimitives.WriteInt64BigEndian(buffer, Convert.ToInt64(value, CultureInfo.InvariantCulture));
                    return buffer;
                case S7DataType.LREAL:
                    S7Helpers.SetLRealAt(buffer, 0, Convert.ToDouble(value, CultureInfo.InvariantCulture));
                    return buffer;

                case S7DataType.STRING:
                    EncodeS7String(buffer, address?.EffectiveLength ?? S7Form.DefaultStringLength, value?.ToString() ?? string.Empty);
                    return buffer;
                case S7DataType.WSTRING:
                    EncodeS7WString(buffer, address?.EffectiveLength ?? S7Form.DefaultStringLength, value?.ToString() ?? string.Empty);
                    return buffer;

                case S7DataType.DATE:
                    {
                        DateTime d = ConvertToDateTime(value);
                        ushort days = (ushort)(d.Date - new DateTime(1990, 1, 1)).TotalDays;
                        BinaryPrimitives.WriteUInt16BigEndian(buffer, days);
                        return buffer;
                    }
                case S7DataType.TIME:
                    BinaryPrimitives.WriteInt32BigEndian(buffer, (int)ConvertToTimeSpan(value).TotalMilliseconds);
                    return buffer;
                case S7DataType.LTIME:
                    BinaryPrimitives.WriteInt64BigEndian(buffer, ConvertToTimeSpan(value).Ticks * 100);
                    return buffer;
                case S7DataType.S5TIME:
                    EncodeS5Time(buffer, ConvertToTimeSpan(value));
                    return buffer;
                case S7DataType.TOD:
                    BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)ConvertToTimeSpan(value).TotalMilliseconds);
                    return buffer;
                case S7DataType.LTOD:
                    BinaryPrimitives.WriteUInt64BigEndian(buffer, (ulong)(ConvertToTimeSpan(value).Ticks * 100));
                    return buffer;
                case S7DataType.DT:
                    EncodeDt(buffer, ConvertToDateTime(value));
                    return buffer;
                case S7DataType.LDT:
                    {
                        DateTime dt = ConvertToDateTime(value).ToUniversalTime();
                        long ns = (dt.Ticks - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks) * 100;
                        BinaryPrimitives.WriteInt64BigEndian(buffer, ns);
                        return buffer;
                    }
                case S7DataType.DTL:
                    EncodeDtl(buffer, ConvertToDateTime(value));
                    return buffer;

                default:
                    throw new NotSupportedException($"S7 type {type} is not supported.");
            }
        }

        /// <summary>
        /// Writes a STRING: the declared maximum length, the current length, then the
        /// characters. More characters than the declared maximum are rejected.
        /// </summary>
        public static void EncodeS7String(byte[] buffer, int max, string text)
        {
            if (text.Length > max)
            {
                throw new ArgumentException($"{text.Length} characters exceed the declared STRING length of {max}.");
            }

            buffer[0] = (byte)max;
            buffer[1] = (byte)text.Length;
            Encoding.ASCII.GetBytes(text, 0, text.Length, buffer, 2);
        }

        public static void EncodeS7WString(byte[] buffer, int max, string text)
        {
            if (text.Length > max)
            {
                throw new ArgumentException($"{text.Length} characters exceed the declared WSTRING length of {max}.");
            }

            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(0), (ushort)max);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2), (ushort)text.Length);
            byte[] bytes = Encoding.BigEndianUnicode.GetBytes(text);
            Buffer.BlockCopy(bytes, 0, buffer, 4, Math.Min(bytes.Length, buffer.Length - 4));
        }

        private static void EncodeDtl(byte[] buffer, DateTime dt)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(0), (ushort)dt.Year);
            buffer[2] = (byte)dt.Month;
            buffer[3] = (byte)dt.Day;
            buffer[4] = (byte)((int)dt.DayOfWeek + 1); // S7: Sun=1..Sat=7
            buffer[5] = (byte)dt.Hour;
            buffer[6] = (byte)dt.Minute;
            buffer[7] = (byte)dt.Second;
            uint ns = (uint)((dt.Ticks % TimeSpan.TicksPerSecond) * 100);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(8), ns);
        }

        private static void EncodeDt(byte[] buffer, DateTime dt)
        {
            int yy = dt.Year % 100;
            buffer[0] = IntToBcd(yy);
            buffer[1] = IntToBcd(dt.Month);
            buffer[2] = IntToBcd(dt.Day);
            buffer[3] = IntToBcd(dt.Hour);
            buffer[4] = IntToBcd(dt.Minute);
            buffer[5] = IntToBcd(dt.Second);
            int ms = dt.Millisecond;
            int msHi = ms / 10;
            int msLo = ms % 10;
            buffer[6] = IntToBcd(msHi);
            int dow = (int)dt.DayOfWeek + 1; // S7: Sun=1..Sat=7
            buffer[7] = (byte)(((msLo & 0x0F) << 4) | (dow & 0x0F));
        }

        private static void EncodeS5Time(byte[] buffer, TimeSpan ts)
        {
            long ms = (long)ts.TotalMilliseconds;
            int tb;
            int unitMs;
            if (ms <= 9990L) { tb = 0; unitMs = 10; }
            else if (ms <= 99900L) { tb = 1; unitMs = 100; }
            else if (ms <= 999000L) { tb = 2; unitMs = 1000; }
            else { tb = 3; unitMs = 10000; }

            int value = (int)Math.Min(999, ms / unitMs);
            int h = (value / 100) % 10;
            int t = (value / 10) % 10;
            int u = value % 10;
            ushort raw = (ushort)(((tb & 0x03) << 12) | ((h & 0x0F) << 8) | ((t & 0x0F) << 4) | (u & 0x0F));
            BinaryPrimitives.WriteUInt16BigEndian(buffer, raw);
        }

        private static byte IntToBcd(int v)
        {
            v = Math.Max(0, Math.Min(99, v));
            return (byte)(((v / 10) << 4) | (v % 10));
        }

        // ---- value coercion ----------------------------------------------

        private static bool ConvertToBool(object value)
        {
            if (value is bool b)
            {
                return b;
            }

            string s = value?.ToString();
            if (bool.TryParse(s, out bool parsed))
            {
                return parsed;
            }

            return Convert.ToInt32(s, CultureInfo.InvariantCulture) != 0;
        }

        private static DateTime ConvertToDateTime(object value)
        {
            if (value is DateTime dt) return dt;
            if (value is DateTimeOffset dto) return dto.UtcDateTime;
            string s = value?.ToString();
            if (string.IsNullOrEmpty(s))
            {
                return DateTime.UnixEpoch;
            }
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
            {
                return parsed;
            }
            return DateTime.UnixEpoch;
        }

        /// <summary>A duration given as a TimeSpan, milliseconds, an ISO 8601 duration or hh:mm:ss.</summary>
        public static TimeSpan ConvertToTimeSpan(object value)
        {
            if (value is TimeSpan ts) return ts;
            if (value is long l) return TimeSpan.FromMilliseconds(l);
            if (value is int i) return TimeSpan.FromMilliseconds(i);
            if (value is double d) return TimeSpan.FromMilliseconds(d);
            string s = value?.ToString()?.Trim();
            if (string.IsNullOrEmpty(s)) return TimeSpan.Zero;
            if (s.StartsWith("P", StringComparison.Ordinal) || s.StartsWith("-P", StringComparison.Ordinal))
            {
                try
                {
                    return XmlConvert.ToTimeSpan(s);
                }
                catch (FormatException)
                {
                    return TimeSpan.Zero;
                }
            }
            if (TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out TimeSpan parsed))
            {
                return parsed;
            }
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms))
            {
                return TimeSpan.FromMilliseconds(ms);
            }
            return TimeSpan.Zero;
        }

        /// <summary>
        /// Reconnects to the controller. The rack, slot and connection terms of the
        /// endpoint are kept; only the host and port come from the caller.
        /// </summary>
        public Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken = default)
        {
            _host = ipAddress;
            _port = port > 0 ? port : S7Endpoint.DefaultPort;
            ConnectCore();
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            DisconnectCore();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisconnectCore();
            return ValueTask.CompletedTask;
        }

        public Task<object> ReadAsync(AssetTag tag, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ReadCore(tag));
        }

        public Task WriteAsync(AssetTag tag, object value, CancellationToken cancellationToken = default)
        {
            WriteCore(tag, value);
            return Task.CompletedTask;
        }

        public Task<AssetActionResult> ExecuteActionAsync(MethodState method, IList<object> inputArgs, CancellationToken cancellationToken = default)
        {
            IList<object> outputArgs = null;
            string status = ExecuteActionCore(method, inputArgs, ref outputArgs);
            return Task.FromResult(AssetActionResult.FromOutputs(status, outputArgs));
        }
    }
}
