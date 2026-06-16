using Microsoft.VSDiagnostics;

namespace Opc.Ua.Edge.Translator.Benchmarks
{
    using System;
    using System.IO;
    using System.Linq;
    using BenchmarkDotNet.Attributes;

    [CPUUsageDiagnoser]
    public class ProvisioningModeCheckBenchmarks
    {
        private string _certsPath;
        // Mirrors the new UANodeManager caching fields.
        private const int _provisioningModeTtlMs = 1000;
        private int _provisioningModeCheckedAtTick;
        private bool _provisioningModeChecked;
        private bool _provisioningModeCached;
        [GlobalSetup]
        public void Setup()
        {
            _certsPath = Path.Combine(Path.GetTempPath(), "uaedge-bench-certs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_certsPath);
            // A couple of issuer certs => operational (non-provisioning) mode,
            // matching a normally-commissioned server during normal operation.
            File.WriteAllText(Path.Combine(_certsPath, "issuer1.der"), "x");
            File.WriteAllText(Path.Combine(_certsPath, "issuer2.der"), "x");
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            try
            {
                Directory.Delete(_certsPath, true);
            }
            catch
            {
            // best-effort cleanup
            }
        }

        // BEFORE: filesystem enumeration + LINQ Count() materialization on every call.
        [Benchmark(Baseline = true)]
        public bool Old_EnumerateOnEveryCall()
        {
            return Directory.EnumerateFiles(_certsPath).Count() == 0;
        }

        // AFTER: cached check with a short TTL (mirrors UANodeManager.IsInProvisioningMode()).
        [Benchmark]
        public bool New_CachedWithTtl()
        {
            int now = Environment.TickCount;
            if (!_provisioningModeChecked || (uint)(now - _provisioningModeCheckedAtTick) >= _provisioningModeTtlMs)
            {
                _provisioningModeCached = !Directory.Exists(_certsPath) || !Directory.EnumerateFiles(_certsPath).Any();
                _provisioningModeCheckedAtTick = now;
                _provisioningModeChecked = true;
            }

            return _provisioningModeCached;
        }
    }
}