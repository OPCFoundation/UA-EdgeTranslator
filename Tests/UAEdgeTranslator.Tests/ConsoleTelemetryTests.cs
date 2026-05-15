namespace Opc.Ua.Edge.Translator.Tests
{
    using System.IO;
    using Microsoft.Extensions.Logging;
    using Opc.Ua.Cloud;
    using Xunit;

    [Collection(WorkingDirectoryCollection.Name)]
    public class ConsoleTelemetryTests
    {
        [Fact]
        public void Constructor_creates_logs_directory_and_publishes_instruments()
        {
            using TestWorkingDirectory tmp = new();

            using ConsoleTelemetry telemetry = new();

            string logsDir = Path.Combine(tmp.Path, "logs");
            Assert.True(Directory.Exists(logsDir), "logs directory should be created in working dir");

            Assert.NotNull(telemetry.LoggerFactory);
            Assert.NotNull(telemetry.ActivitySource);
            Assert.NotNull(telemetry.CreateMeter());

            // every counter is registered and reachable
            Assert.NotNull(telemetry.TagReads);
            Assert.NotNull(telemetry.TagReadErrors);
            Assert.NotNull(telemetry.TagWrites);
            Assert.NotNull(telemetry.TagWriteErrors);
            Assert.NotNull(telemetry.AssetReconnects);
            Assert.NotNull(telemetry.AssetReconnectFailures);

            // creating the meter twice returns the same shared instance to
            // avoid leaking instruments past the first GC.
            Assert.Same(telemetry.CreateMeter(), telemetry.CreateMeter());
        }

        [Fact]
        public void Counters_accept_increments_without_throwing()
        {
            using TestWorkingDirectory tmp = new();
            using ConsoleTelemetry telemetry = new();

            telemetry.TagReads.Add(1);
            telemetry.TagReadErrors.Add(2);
            telemetry.TagWrites.Add(3);
            telemetry.TagWriteErrors.Add(4);
            telemetry.AssetReconnects.Add(5);
            telemetry.AssetReconnectFailures.Add(6);
        }

        [Fact]
        public void Constructor_invokes_optional_logger_configuration_callback()
        {
            using TestWorkingDirectory tmp = new();
            bool invoked = false;

            using ConsoleTelemetry telemetry = new(builder =>
            {
                invoked = true;
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            Assert.True(invoked, "configure callback should run during ConsoleTelemetry construction");
        }

        [Fact]
        public void Dispose_can_be_called_multiple_times()
        {
            using TestWorkingDirectory tmp = new();
            ConsoleTelemetry telemetry = new();

            telemetry.Dispose();
            // double-dispose must not throw — Meter.Dispose() and Log.CloseAndFlush() are idempotent.
            telemetry.Dispose();
        }
    }
}
