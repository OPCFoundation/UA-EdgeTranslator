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
    using System.Globalization;
    using System.Text;
    using S7Helpers = Sharp7.S7;

    public class SiemensAsset : IAsset
    {
        public S7Client S7 = null;

        private string _endpoint = string.Empty;

        public bool IsConnected { get; private set; } = false;

        /// <summary>
        /// Connects to the PLC. Sharp7 always uses ISO‑on‑TCP port 102, so the
        /// "port" parameter from the URL is interpreted as the CPU slot.
        /// </summary>
        public void Connect(string ipAddress, int slot)
        {
            Connect(ipAddress, rack: 0, slot: slot);
        }

        public void Connect(string ipAddress, int rack, int slot)
        {
            try
            {
                _endpoint = ipAddress;

                S7 = new S7Client();

                var result = S7.ConnectTo(ipAddress, rack, slot);

                if (result == 0)
                {
                    Log.Logger.Information("Connected to Siemens S7 at {ip} (rack {rack}, slot {slot})", ipAddress, rack, slot);
                    IsConnected = true;
                }
                else
                {
                    Log.Logger.Error("Failed to connect to Siemens S7 at {ip} (rack {rack}, slot {slot}): {err}",
                        ipAddress, rack, slot, S7.ErrorText(result));
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        public void Disconnect()
        {
            if (S7 != null)
            {
                S7.Disconnect();
                S7 = null;
            }

            IsConnected = false;
        }

        public string GetRemoteEndpoint()
        {
            return _endpoint;
        }

        public object Read(AssetTag tag)
        {
            S7Form form = ParseForm(tag);
            if (form == null)
            {
                return null;
            }

            int sizeBytes = ResolveSize(form);
            if (sizeBytes <= 0)
            {
                return null;
            }

            byte[] buffer = new byte[sizeBytes];
            S7Area areaCode = AreaCodeFor(form.S7Target);

            // For BOOL we read a single bit; everything else is byte-addressed.
            bool isBoolWire = IsBoolWire(form);
            S7WordLength wordLength = isBoolWire ? S7WordLength.Bit : S7WordLength.Byte;

            int amount = isBoolWire ? 1 : sizeBytes;

            // S7AreaDB requires the DB number; the other areas ignore it.
            int dbNumber = (form.S7Target == S7Target.DataBlock) ? form.S7DBNumber : 0;

            // For BOOL the "start" passed to ReadArea is the absolute bit
            // address (byteOffset * 8 + bitOffset). For everything else it's
            // the byte offset.
            int start = isBoolWire
                ? (form.S7Start * 8) + form.S7Pos
                : form.S7Start;

            int err = S7.ReadArea(areaCode, dbNumber, start, amount, wordLength, buffer);
            if (err != 0)
            {
                Log.Logger.Error("S7 read failed for {tag}: {err}", tag.Name, S7.ErrorText(err));
                return null;
            }

            return Decode(buffer, form);
        }

        public void Write(AssetTag tag, object value)
        {
            S7Form form = ParseForm(tag);
            if (form == null || value == null)
            {
                return;
            }

            S7Area areaCode = AreaCodeFor(form.S7Target);
            int dbNumber = (form.S7Target == S7Target.DataBlock) ? form.S7DBNumber : 0;

            if (IsBoolWire(form))
            {
                byte[] one = new byte[1];
                S7Helpers.SetBitAt(one, 0, 0, ConvertToBool(value));
                int bitStart = (form.S7Start * 8) + form.S7Pos;
                int err = S7.WriteArea(areaCode, dbNumber, bitStart, 1, S7WordLength.Bit, one);
                if (err != 0)
                {
                    Log.Logger.Error("S7 write failed for {tag}: {err}", tag.Name, S7.ErrorText(err));
                }

                return;
            }

            byte[] buffer = Encode(value, form);
            if (buffer == null)
            {
                return;
            }

            int errOther = S7.WriteArea(areaCode, dbNumber, form.S7Start, buffer.Length, S7WordLength.Byte, buffer);
            if (errOther != 0)
            {
                Log.Logger.Error("S7 write failed for {tag}: {err}", tag.Name, S7.ErrorText(errOther));
            }
        }

        public string ExecuteAction(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs)
        {
            return null;
        }

        // ---- helpers ------------------------------------------------------

        private static S7Form ParseForm(AssetTag tag)
        {
            if (string.IsNullOrEmpty(tag?.Address))
            {
                return null;
            }

            // CreateTag stores the full S7Form as JSON in AssetTag.Address so
            // every detail (DB number, byte+bit offset, target area, max len)
            // survives the round trip.
            string a = tag.Address.TrimStart();
            if (a.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    return JsonConvert.DeserializeObject<S7Form>(a);
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex, "Could not parse S7 form for tag {tag}", tag.Name);
                    return null;
                }
            }

            // Legacy "DBn?count" form — best effort.
            return ParseLegacyAddress(a, tag.Type);
        }

        private static S7Form ParseLegacyAddress(string address, string typeName)
        {
            string[] parts = address.Split(['?', '&', '=']);
            if (parts.Length < 2)
            {
                return null;
            }

            int dbNumber = 0;
            string head = parts[0];
            if (head.StartsWith("DB", StringComparison.OrdinalIgnoreCase))
            {
                _ = int.TryParse(head.AsSpan(2), out dbNumber);
            }

            _ = int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sizeOrCount);

            S7Form form = new()
            {
                S7Target = S7Target.DataBlock,
                S7DBNumber = dbNumber,
                S7Start = 0,
                S7Size = sizeOrCount,
                Type = ParseLegacyType(typeName),
                Href = address
            };

            return form;
        }

        private static TypeString ParseLegacyType(string typeName)
        {
            return typeName switch
            {
                "Float" => TypeString.Float,
                "Double" => TypeString.Double,
                "Boolean" => TypeString.Boolean,
                "Integer" => TypeString.Integer,
                "Short" => TypeString.Short,
                "Byte" => TypeString.Byte,
                "Long" => TypeString.Long,
                "UnsignedLong" => TypeString.UnsignedLong,
                "DateTime" => TypeString.DateTime,
                "Duration" => TypeString.Duration,
                _ => TypeString.String,
            };
        }

        private static S7Area AreaCodeFor(S7Target target) => target switch
        {
            S7Target.DataBlock => S7Area.DB,
            S7Target.Merker => S7Area.MK,
            S7Target.IPIProcessInput => S7Area.PE,
            S7Target.IPUProcessInput => S7Area.PA,
            S7Target.Timer => S7Area.TM,
            S7Target.Counter => S7Area.CT,
            _ => S7Area.DB,
        };

        /// <summary>
        /// True only for true single-bit BOOLs. Everything else (including
        /// CHAR, WCHAR, dates, durations, 64-bit integers, arrays) is
        /// byte-addressed.
        /// </summary>
        private static bool IsBoolWire(S7Form form)
        {
            string s7 = form.S7S7Type;
            if (!string.IsNullOrEmpty(s7))
            {
                return string.Equals(s7, "BOOL", StringComparison.OrdinalIgnoreCase);
            }

            return form.Type == TypeString.Boolean;
        }

        private static int ResolveSize(S7Form form)
        {
            if (form.S7Size > 0)
            {
                return form.S7Size;
            }

            // Fall back to the canonical S7 type name first (more precise
            // than TypeString, which is a coarse XSD bucket).
            int s7Size = SizeForS7Type(form.S7S7Type, form.S7MaxLen);
            if (s7Size > 0)
            {
                return s7Size;
            }

            return form.Type switch
            {
                TypeString.Boolean => 1,
                TypeString.Byte => 1,
                TypeString.Short => 2,
                TypeString.Integer => 4,
                TypeString.Long => 8,
                TypeString.UnsignedLong => 8,
                TypeString.Float => 4,
                TypeString.Double => 8,
                TypeString.Duration => 4,    // TIME
                TypeString.DateTime => 12,   // DTL
                TypeString.String => form.S7MaxLen > 0 ? form.S7MaxLen + 2 : 0,
                _ => 0,
            };
        }

        private static int SizeForS7Type(string s7Type, int maxLen)
        {
            if (string.IsNullOrEmpty(s7Type))
            {
                return 0;
            }

            switch (s7Type.ToUpperInvariant())
            {
                case "BOOL": return 1;
                case "BYTE": case "USINT": case "SINT": case "CHAR": return 1;
                case "WORD": case "UINT": case "INT": case "WCHAR":
                case "DATE": case "S5TIME": return 2;
                case "DWORD": case "UDINT": case "DINT": case "REAL":
                case "TIME": case "TOD": case "TIME_OF_DAY": return 4;
                case "LWORD": case "ULINT": case "LINT": case "LREAL":
                case "LTIME": case "LTOD": case "LTIME_OF_DAY":
                case "DT": case "DATE_AND_TIME": case "LDT": return 8;
                case "DTL": return 12;
                case "STRING": return (maxLen > 0 ? maxLen : 254) + 2;
                case "WSTRING": return 4 + ((maxLen > 0 ? maxLen : 254) * 2);
                default: return 0;
            }
        }

        // ---- Decode -------------------------------------------------------

        private static object Decode(byte[] buffer, S7Form form)
        {
            // Prefer the canonical Siemens type when present (importer fills
            // it in as of the new schema). Falls back to the broad XSD bucket
            // for backward compatibility with pre-existing Thing Models.
            if (!string.IsNullOrEmpty(form.S7S7Type))
            {
                object decoded = DecodeBySiemensType(buffer, form);
                if (decoded != null)
                {
                    return decoded;
                }
            }

            switch (form.Type)
            {
                case TypeString.Boolean:
                    return S7Helpers.GetBitAt(buffer, 0, form.S7Pos);
                case TypeString.Byte:
                    return S7Helpers.GetByteAt(buffer, 0);
                case TypeString.Short:
                    return S7Helpers.GetIntAt(buffer, 0);
                case TypeString.Integer:
                    return S7Helpers.GetDIntAt(buffer, 0);
                case TypeString.Long:
                    return BinaryPrimitives.ReadInt64BigEndian(buffer);
                case TypeString.UnsignedLong:
                    return BinaryPrimitives.ReadUInt64BigEndian(buffer);
                case TypeString.Float:
                    return S7Helpers.GetRealAt(buffer, 0);
                case TypeString.Double:
                    return S7Helpers.GetLRealAt(buffer, 0);
                case TypeString.DateTime:
                    return DecodeDtl(buffer); // legacy default = DTL
                case TypeString.Duration:
                    return TimeSpan.FromMilliseconds(BinaryPrimitives.ReadInt32BigEndian(buffer));
                case TypeString.String:
                    if (buffer.Length >= 2 && form.S7MaxLen > 0)
                    {
                        return S7Helpers.GetStringAt(buffer, 0);
                    }
                    return Encoding.ASCII.GetString(buffer);
                default:
                    return null;
            }
        }

        private static object DecodeBySiemensType(byte[] buffer, S7Form form)
        {
            switch (form.S7S7Type.ToUpperInvariant())
            {
                // ---- bit / 8-bit primitives ----------------------------------
                case "BOOL":   return S7Helpers.GetBitAt(buffer, 0, form.S7Pos);
                case "BYTE":   return buffer[0];
                case "USINT":  return buffer[0];
                case "SINT":   return (sbyte)buffer[0];
                case "CHAR":   return ((char)buffer[0]).ToString();

                // ---- 16-bit primitives ---------------------------------------
                case "WORD":   return BinaryPrimitives.ReadUInt16BigEndian(buffer);
                case "UINT":   return BinaryPrimitives.ReadUInt16BigEndian(buffer);
                case "INT":    return BinaryPrimitives.ReadInt16BigEndian(buffer);
                case "WCHAR":  return ((char)BinaryPrimitives.ReadUInt16BigEndian(buffer)).ToString();

                // ---- 32-bit primitives ---------------------------------------
                case "DWORD":  return BinaryPrimitives.ReadUInt32BigEndian(buffer);
                case "UDINT":  return BinaryPrimitives.ReadUInt32BigEndian(buffer);
                case "DINT":   return BinaryPrimitives.ReadInt32BigEndian(buffer);
                case "REAL":   return S7Helpers.GetRealAt(buffer, 0);

                // ---- 64-bit primitives (NEW) ---------------------------------
                case "LWORD":  return BinaryPrimitives.ReadUInt64BigEndian(buffer);
                case "ULINT":  return BinaryPrimitives.ReadUInt64BigEndian(buffer);
                case "LINT":   return BinaryPrimitives.ReadInt64BigEndian(buffer);
                case "LREAL":  return S7Helpers.GetLRealAt(buffer, 0);

                // ---- variable-length strings ---------------------------------
                case "STRING":  return DecodeS7String(buffer);
                case "WSTRING": return DecodeS7WString(buffer);

                // ---- date / time / duration (NEW) ----------------------------
                case "DATE":              return DecodeDate(buffer);
                case "TIME":              return TimeSpan.FromMilliseconds(BinaryPrimitives.ReadInt32BigEndian(buffer));
                case "LTIME":             return DecodeLTime(buffer);
                case "S5TIME":            return DecodeS5Time(buffer);
                case "TOD":
                case "TIME_OF_DAY":       return DecodeTod(buffer);
                case "LTOD":
                case "LTIME_OF_DAY":      return DecodeLTod(buffer);
                case "DT":
                case "DATE_AND_TIME":     return DecodeDt(buffer);
                case "LDT":               return DecodeLdt(buffer);
                case "DTL":               return DecodeDtl(buffer);

                default:                  return null;
            }
        }

        private static string DecodeS7String(byte[] buffer)
        {
            if (buffer.Length < 2) return string.Empty;
            int len = buffer[1];
            int avail = Math.Min(len, buffer.Length - 2);
            return Encoding.ASCII.GetString(buffer, 2, avail);
        }

        private static string DecodeS7WString(byte[] buffer)
        {
            if (buffer.Length < 4) return string.Empty;
            // Header: 2 words. word[0] = max length, word[1] = current length (in chars).
            int currentChars = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2));
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

        private static byte[] Encode(object value, S7Form form)
        {
            int size = ResolveSize(form);
            if (size <= 0)
            {
                return null;
            }

            // Prefer canonical Siemens type when present.
            if (!string.IsNullOrEmpty(form.S7S7Type))
            {
                byte[] encoded = EncodeBySiemensType(value, form, size);
                if (encoded != null)
                {
                    return encoded;
                }
            }

            byte[] buffer = new byte[size];
            switch (form.Type)
            {
                case TypeString.Byte:
                    S7Helpers.SetByteAt(buffer, 0, Convert.ToByte(value, CultureInfo.InvariantCulture));
                    break;
                case TypeString.Short:
                    S7Helpers.SetIntAt(buffer, 0, Convert.ToInt16(value, CultureInfo.InvariantCulture));
                    break;
                case TypeString.Integer:
                    S7Helpers.SetDIntAt(buffer, 0, Convert.ToInt32(value, CultureInfo.InvariantCulture));
                    break;
                case TypeString.Long:
                    BinaryPrimitives.WriteInt64BigEndian(buffer, Convert.ToInt64(value, CultureInfo.InvariantCulture));
                    break;
                case TypeString.UnsignedLong:
                    BinaryPrimitives.WriteUInt64BigEndian(buffer, Convert.ToUInt64(value, CultureInfo.InvariantCulture));
                    break;
                case TypeString.Float:
                    S7Helpers.SetRealAt(buffer, 0, Convert.ToSingle(value, CultureInfo.InvariantCulture));
                    break;
                case TypeString.Double:
                    S7Helpers.SetLRealAt(buffer, 0, Convert.ToDouble(value, CultureInfo.InvariantCulture));
                    break;
                case TypeString.Duration:
                    BinaryPrimitives.WriteInt32BigEndian(buffer, (int)ConvertToTimeSpan(value).TotalMilliseconds);
                    break;
                case TypeString.DateTime:
                    EncodeDtl(buffer, ConvertToDateTime(value));
                    break;
                case TypeString.String:
                    int max = form.S7MaxLen > 0 ? form.S7MaxLen : Math.Max(0, size - 2);
                    S7Helpers.SetStringAt(buffer, 0, max, value?.ToString() ?? string.Empty);
                    break;
                default:
                    return null;
            }

            return buffer;
        }

        private static byte[] EncodeBySiemensType(object value, S7Form form, int size)
        {
            byte[] buffer = new byte[size];
            switch (form.S7S7Type.ToUpperInvariant())
            {
                // ---- bit / 8-bit primitives ----------------------------------
                case "BOOL":
                    S7Helpers.SetBitAt(buffer, 0, form.S7Pos, ConvertToBool(value));
                    return buffer;
                case "BYTE":
                case "USINT":
                    buffer[0] = Convert.ToByte(value, CultureInfo.InvariantCulture);
                    return buffer;
                case "SINT":
                    buffer[0] = (byte)Convert.ToSByte(value, CultureInfo.InvariantCulture);
                    return buffer;
                case "CHAR":
                    {
                        string sc = value?.ToString() ?? string.Empty;
                        buffer[0] = (byte)(sc.Length > 0 ? sc[0] : (char)0);
                        return buffer;
                    }

                // ---- 16-bit primitives ---------------------------------------
                case "WORD":
                case "UINT":
                    BinaryPrimitives.WriteUInt16BigEndian(buffer, Convert.ToUInt16(value, CultureInfo.InvariantCulture));
                    return buffer;
                case "INT":
                    BinaryPrimitives.WriteInt16BigEndian(buffer, Convert.ToInt16(value, CultureInfo.InvariantCulture));
                    return buffer;
                case "WCHAR":
                    {
                        string sc = value?.ToString() ?? string.Empty;
                        ushort cp = (ushort)(sc.Length > 0 ? sc[0] : (char)0);
                        BinaryPrimitives.WriteUInt16BigEndian(buffer, cp);
                        return buffer;
                    }

                // ---- 32-bit primitives ---------------------------------------
                case "DWORD":
                case "UDINT":
                    BinaryPrimitives.WriteUInt32BigEndian(buffer, Convert.ToUInt32(value, CultureInfo.InvariantCulture));
                    return buffer;
                case "DINT":
                    BinaryPrimitives.WriteInt32BigEndian(buffer, Convert.ToInt32(value, CultureInfo.InvariantCulture));
                    return buffer;
                case "REAL":
                    S7Helpers.SetRealAt(buffer, 0, Convert.ToSingle(value, CultureInfo.InvariantCulture));
                    return buffer;

                // ---- 64-bit primitives ---------------------------------------
                case "LWORD":
                case "ULINT":
                    BinaryPrimitives.WriteUInt64BigEndian(buffer, Convert.ToUInt64(value, CultureInfo.InvariantCulture));
                    return buffer;
                case "LINT":
                    BinaryPrimitives.WriteInt64BigEndian(buffer, Convert.ToInt64(value, CultureInfo.InvariantCulture));
                    return buffer;
                case "LREAL":
                    S7Helpers.SetLRealAt(buffer, 0, Convert.ToDouble(value, CultureInfo.InvariantCulture));
                    return buffer;

                // ---- variable-length strings ---------------------------------
                case "STRING":
                    {
                        int max = form.S7MaxLen > 0 ? form.S7MaxLen : Math.Max(0, size - 2);
                        S7Helpers.SetStringAt(buffer, 0, max, value?.ToString() ?? string.Empty);
                        return buffer;
                    }
                case "WSTRING":
                    EncodeS7WString(buffer, form, value?.ToString() ?? string.Empty);
                    return buffer;

                // ---- date / time / duration ----------------------------------
                case "DATE":
                    {
                        DateTime d = ConvertToDateTime(value);
                        ushort days = (ushort)(d.Date - new DateTime(1990, 1, 1)).TotalDays;
                        BinaryPrimitives.WriteUInt16BigEndian(buffer, days);
                        return buffer;
                    }
                case "TIME":
                    BinaryPrimitives.WriteInt32BigEndian(buffer, (int)ConvertToTimeSpan(value).TotalMilliseconds);
                    return buffer;
                case "LTIME":
                    BinaryPrimitives.WriteInt64BigEndian(buffer, ConvertToTimeSpan(value).Ticks * 100);
                    return buffer;
                case "S5TIME":
                    EncodeS5Time(buffer, ConvertToTimeSpan(value));
                    return buffer;
                case "TOD":
                case "TIME_OF_DAY":
                    BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)ConvertToTimeSpan(value).TotalMilliseconds);
                    return buffer;
                case "LTOD":
                case "LTIME_OF_DAY":
                    BinaryPrimitives.WriteUInt64BigEndian(buffer, (ulong)(ConvertToTimeSpan(value).Ticks * 100));
                    return buffer;
                case "DT":
                case "DATE_AND_TIME":
                    EncodeDt(buffer, ConvertToDateTime(value));
                    return buffer;
                case "LDT":
                    {
                        DateTime dt = ConvertToDateTime(value).ToUniversalTime();
                        long ns = (dt.Ticks - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks) * 100;
                        BinaryPrimitives.WriteInt64BigEndian(buffer, ns);
                        return buffer;
                    }
                case "DTL":
                    EncodeDtl(buffer, ConvertToDateTime(value));
                    return buffer;

                default:
                    return null;
            }
        }

        private static void EncodeS7WString(byte[] buffer, S7Form form, string text)
        {
            int max = form.S7MaxLen > 0 ? form.S7MaxLen : Math.Max(0, (buffer.Length - 4) / 2);
            int count = Math.Min(text.Length, max);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(0), (ushort)max);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2), (ushort)count);
            byte[] bytes = Encoding.BigEndianUnicode.GetBytes(text.AsSpan(0, count).ToArray());
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

        private static TimeSpan ConvertToTimeSpan(object value)
        {
            if (value is TimeSpan ts) return ts;
            if (value is long l) return TimeSpan.FromMilliseconds(l);
            if (value is int i) return TimeSpan.FromMilliseconds(i);
            if (value is double d) return TimeSpan.FromMilliseconds(d);
            string s = value?.ToString();
            if (string.IsNullOrEmpty(s)) return TimeSpan.Zero;
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
    }
}
