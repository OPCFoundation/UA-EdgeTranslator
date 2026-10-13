namespace Opc.Ua.Edge.Translator.WotCon
{
    using System;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// Provisioning-mode gate for the WoT.con registry data plane. Mirrors the
    /// WoT v1.02 node manager: while no issuer certificate is present, asset reads and
    /// writes are refused unless <c>IGNORE_PROVISIONING_MODE</c> is set. The check
    /// is cached for a second so the hot path does not hit the filesystem per call.
    /// </summary>
    internal static class ProvisioningGate
    {
        private const int TtlMs = 1000;

        private static int s_checkedAtTick;

        private static bool s_checked;

        private static volatile bool s_provisioning;

        public static bool IsAssetAccessBlocked()
        {
            return IsInProvisioningMode()
                && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IGNORE_PROVISIONING_MODE"));
        }

        private static bool IsInProvisioningMode()
        {
            int now = Environment.TickCount;
            if (!s_checked || (uint)(now - s_checkedAtTick) >= TtlMs)
            {
                string certsPath = Path.Combine(Directory.GetCurrentDirectory(), "pki", "issuer", "certs");
                s_provisioning = !Directory.Exists(certsPath) || !Directory.EnumerateFiles(certsPath).Any();
                s_checkedAtTick = now;
                s_checked = true;
            }

            return s_provisioning;
        }
    }
}
