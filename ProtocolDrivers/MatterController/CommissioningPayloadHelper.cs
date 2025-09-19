namespace Matter.Core.Commissioning
{
    using System;

    public class CommissioningPayloadHelper
    {
        public static CommissioningPayload ParseManualSetupCode(string manualSetupCode)
        {
            manualSetupCode = manualSetupCode.Replace("-", "");

            if (manualSetupCode.Length != 11)
            {
                throw new ArgumentException("Manual setup code must be 11 digits long.");
            }

            var isValid = Verhoeff.validateVerhoeff(manualSetupCode);

            if (!isValid)
            {
                throw new ArgumentException("Manual setup code failed checksum.");
            }

            byte byte1 = byte.Parse(manualSetupCode.Substring(0, 1));

            ushort discriminator = (ushort)(byte1 << 10);

            ushort byte2to6 = ushort.Parse(manualSetupCode.Substring(1, 5));

            discriminator |= (ushort)((byte2to6 & 0xC000) >> 6);

            uint passcode = (uint)(byte2to6 & 0x3FFF);

            ushort byte7to10 = ushort.Parse(manualSetupCode.Substring(6, 4));

            passcode |= (uint)(byte7to10 << 14);

            return new CommissioningPayload()
            {
                Discriminator = discriminator,
                Passcode = passcode
            };
        }

        public CommissioningPayload ParseQRCode(string qrCodePayload)
        {
            return new CommissioningPayload();
        }
    }
}
