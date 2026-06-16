/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Threading.Tasks;

namespace Opc.Ua.Cloud
{
    public class ConsoleTelemetry : ITelemetryContext, IDisposable
    {
        // Cap individual log files at 50 MB. Combined with rollOnFileSizeLimit
        // below, the sink rolls to a new numbered segment (e.g. "...-20250115_001.log")
        // when the cap is hit, so log entries are not dropped between daily rollovers.
        private const long _logFileSizeLimitBytes = 50L * 1024 * 1024;

        // Single shared Meter instance — creating a new Meter per call would
        // leak meters and cause registered instruments to be orphaned after
        // the first GC, breaking any external metrics consumer.
        private readonly Meter _meter = new("UA cloud app", "1.0.0");

        // In-process listener that observes our own counters so the diagnostics
        // dashboard can show running totals. Counter<T> is write-only by design,
        // so the only supported way to read back the accumulated value is to
        // subscribe to the measurements as they are recorded.
        private readonly MeterListener _meterListener;

        // Running totals keyed by instrument name, accumulated from the listener
        // callback. Guarded by a lock on the dictionary so reads from the
        // dashboard thread never observe a torn or partially-updated value.
        private readonly Dictionary<string, long> _counterTotals = new();

        // Hot-path instruments. These are exposed publicly so the rest of
        // the host (UANodeManager etc.) can record per-tag and per-asset
        // events without each component having to re-create its own meter
        // and risk publishing to multiple meter names.
        public Counter<long> TagReads { get; }

        public Counter<long> TagReadErrors { get; }

        public Counter<long> TagWrites { get; }

        public Counter<long> TagWriteErrors { get; }

        public Counter<long> AssetReconnects { get; }

        public Counter<long> AssetReconnectFailures { get; }

        // Running totals for the instruments above, accumulated in-process by
        // _meterListener. Exposed read-only so the diagnostics dashboard can
        // display cumulative southbound activity since host start.
        public long TagReadCount => ReadTotal("uaedge.tag.reads");

        public long TagReadErrorCount => ReadTotal("uaedge.tag.read_errors");

        public long TagWriteCount => ReadTotal("uaedge.tag.writes");

        public long TagWriteErrorCount => ReadTotal("uaedge.tag.write_errors");

        public long AssetReconnectCount => ReadTotal("uaedge.asset.reconnects");

        public long AssetReconnectFailureCount => ReadTotal("uaedge.asset.reconnect_failures");

        public ConsoleTelemetry(Action<ILoggingBuilder> configure = null)
        {
            string logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    path: "logs/ua-edgetranslator-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    fileSizeLimitBytes: _logFileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
                configure?.Invoke(builder);
            }).AddSerilog(Log.Logger);

            // Register instruments AFTER the meter is constructed so a metrics
            // listener (OTel, Prometheus, etc.) sees them as soon as the host
            // attaches to the meter name.
            TagReads = _meter.CreateCounter<long>("uaedge.tag.reads", description: "Number of southbound tag reads attempted.");
            TagReadErrors = _meter.CreateCounter<long>("uaedge.tag.read_errors", description: "Number of southbound tag reads that failed.");
            TagWrites = _meter.CreateCounter<long>("uaedge.tag.writes", description: "Number of southbound tag writes attempted.");
            TagWriteErrors = _meter.CreateCounter<long>("uaedge.tag.write_errors", description: "Number of southbound tag writes that failed.");
            AssetReconnects = _meter.CreateCounter<long>("uaedge.asset.reconnects", description: "Number of asset reconnect attempts initiated.");
            AssetReconnectFailures = _meter.CreateCounter<long>("uaedge.asset.reconnect_failures", description: "Number of asset reconnect attempts that did not restore connectivity.");

            // Subscribe an in-process listener to our own meter so the running
            // totals are available to the diagnostics dashboard. Only instruments
            // published by this exact meter instance are enabled, so we never pick
            // up unrelated meters living in the same process.
            _meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (ReferenceEquals(instrument.Meter, _meter))
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                }
            };
            _meterListener.SetMeasurementEventCallback<long>(OnMeasurementRecorded);
            _meterListener.Start();

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += Unobserved_TaskException;
        }

        private void OnMeasurementRecorded(
            Instrument instrument,
            long measurement,
            ReadOnlySpan<KeyValuePair<string, object>> tags,
            object state)
        {
            lock (_counterTotals)
            {
                _counterTotals.TryGetValue(instrument.Name, out long current);
                _counterTotals[instrument.Name] = current + measurement;
            }
        }

        private long ReadTotal(string instrumentName)
        {
            lock (_counterTotals)
            {
                return _counterTotals.TryGetValue(instrumentName, out long value) ? value : 0;
            }
        }

        public ILoggerFactory LoggerFactory { get; internal set; }

        public Meter CreateMeter() => _meter;

        public ActivitySource ActivitySource { get; } = new("UA cloud app", "1.0.0");

        public void Dispose()
        {
            _meterListener?.Dispose();
            _meter.Dispose();
            ActivitySource.Dispose();
            LoggerFactory?.Dispose();
            Log.CloseAndFlush();

            AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException -= Unobserved_TaskException;
        }

        private void CurrentDomain_UnhandledException(
            object sender,
            UnhandledExceptionEventArgs args)
        {
            Log.Logger.Error(
                args.ExceptionObject as Exception,
                "Unhandled Exception: (IsTerminating: {IsTerminating})",
                args.IsTerminating);
        }

        private void Unobserved_TaskException(
            object sender,
            UnobservedTaskExceptionEventArgs args)
        {
            Log.Logger.Error(
                args.Exception,
                "Unobserved Task Exception (Observed: {Observed})",
                args.Observed);
        }
    }
}
