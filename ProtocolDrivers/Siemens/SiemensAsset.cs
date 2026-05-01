namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using Sharp7;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
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
            S7WordLength wordLength = (form.Type == TypeString.Boolean) ? S7WordLength.Bit : S7WordLength.Byte;

            int amount = (form.Type == TypeString.Boolean) ? 1 : sizeBytes;

            // S7AreaDB requires the DB number; the other areas ignore it.
            int dbNumber = (form.S7Target == S7Target.DataBlock) ? form.S7DBNumber : 0;

            // For BOOL the "start" passed to ReadArea is the absolute bit
            // address (byteOffset * 8 + bitOffset). For everything else it's
            // the byte offset.
            int start = (form.Type == TypeString.Boolean)
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

            if (form.Type == TypeString.Boolean)
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

        private static int ResolveSize(S7Form form)
        {
            if (form.S7Size > 0)
            {
                return form.S7Size;
            }

            return form.Type switch
            {
                TypeString.Boolean => 1,
                TypeString.Byte => 1,
                TypeString.Short => 2,
                TypeString.Integer => 4,
                TypeString.Float => 4,
                TypeString.Double => 8,
                TypeString.String => form.S7MaxLen > 0 ? form.S7MaxLen + 2 : 0,
                _ => 0,
            };
        }

        private static object Decode(byte[] buffer, S7Form form)
        {
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
                case TypeString.Float:
                    return S7Helpers.GetRealAt(buffer, 0);
                case TypeString.Double:
                    return S7Helpers.GetLRealAt(buffer, 0);
                case TypeString.String:
                    // S7 STRING: byte 0 = max len, byte 1 = current len, bytes 2..n = chars
                    if (buffer.Length >= 2 && form.S7MaxLen > 0)
                    {
                        return S7Helpers.GetStringAt(buffer, 0);
                    }
                    return System.Text.Encoding.ASCII.GetString(buffer);
                default:
                    return null;
            }
        }

        private static byte[] Encode(object value, S7Form form)
        {
            int size = ResolveSize(form);
            if (size <= 0)
            {
                return null;
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
                case TypeString.Float:
                    S7Helpers.SetRealAt(buffer, 0, Convert.ToSingle(value, CultureInfo.InvariantCulture));
                    break;
                case TypeString.Double:
                    S7Helpers.SetLRealAt(buffer, 0, Convert.ToDouble(value, CultureInfo.InvariantCulture));
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
    }
}
