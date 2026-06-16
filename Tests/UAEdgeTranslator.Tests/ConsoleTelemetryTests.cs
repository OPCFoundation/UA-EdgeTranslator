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
        public void Counter_totals_accumulate_recorded_increments()
        {
            using TestWorkingDirectory tmp = new();
            using ConsoleTelemetry telemetry = new();

            telemetry.TagReads.Add(2);
            telemetry.TagReads.Add(3);
            telemetry.TagReadErrors.Add(1);
            telemetry.TagWrites.Add(4);
            telemetry.TagWriteErrors.Add(1);
            telemetry.AssetReconnects.Add(5);
            telemetry.AssetReconnectFailures.Add(6);

            Assert.Equal(5, telemetry.TagReadCount);
            Assert.Equal(1, telemetry.TagReadErrorCount);
            Assert.Equal(4, telemetry.TagWriteCount);
            Assert.Equal(1, telemetry.TagWriteErrorCount);
            Assert.Equal(5, telemetry.AssetReconnectCount);
            Assert.Equal(6, telemetry.AssetReconnectFailureCount);
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

        [Fact]
        public void CurrentDomain_UnhandledException_handler_logs_without_throwing()
        {
            using TestWorkingDirectory tmp = new();
            using ConsoleTelemetry telemetry = new();

            System.Reflection.MethodInfo handler = typeof(ConsoleTelemetry).GetMethod(
                "CurrentDomain_UnhandledException",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(handler);

            System.UnhandledExceptionEventArgs args = new(new System.InvalidOperationException("boom"), isTerminating: false);
            handler.Invoke(telemetry, new object[] { this, args });
        }

        [Fact]
        public void Unobserved_TaskException_handler_logs_without_throwing()
        {
            using TestWorkingDirectory tmp = new();
            using ConsoleTelemetry telemetry = new();

            System.Reflection.MethodInfo handler = typeof(ConsoleTelemetry).GetMethod(
                "Unobserved_TaskException",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(handler);

            // Build a faulted task and observe its exception, then create the
            // event args manually so we don't crash the test process if the
            // handler doesn't mark the exception observed.
            System.Threading.Tasks.Task faulted = System.Threading.Tasks.Task.FromException(new System.InvalidOperationException("boom"));
            System.AggregateException ex = faulted.Exception;
            System.Threading.Tasks.UnobservedTaskExceptionEventArgs args =
                (System.Threading.Tasks.UnobservedTaskExceptionEventArgs)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                    typeof(System.Threading.Tasks.UnobservedTaskExceptionEventArgs));
            typeof(System.Threading.Tasks.UnobservedTaskExceptionEventArgs)
                .GetField("m_exception", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(args, ex);

            handler.Invoke(telemetry, new object[] { this, args });
        }
    }
}
