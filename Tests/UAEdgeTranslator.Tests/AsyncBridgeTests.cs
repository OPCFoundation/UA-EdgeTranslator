namespace Opc.Ua.Edge.Translator.Tests
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    public class AsyncBridgeTests
    {
        [Fact]
        public void RunSync_void_completes_async_work()
        {
            int captured = 0;

            AsyncBridge.RunSync(async () =>
            {
                await Task.Yield();
                captured = 42;
            });

            Assert.Equal(42, captured);
        }

        [Fact]
        public void RunSync_T_returns_result_of_async_work()
        {
            int result = AsyncBridge.RunSync(async () =>
            {
                await Task.Yield();
                return 7;
            });

            Assert.Equal(7, result);
        }

        [Fact]
        public void RunSync_void_propagates_exception()
        {
            Assert.Throws<InvalidOperationException>(() =>
                AsyncBridge.RunSync(async () =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("boom");
                }));
        }

        [Fact]
        public void RunSync_T_propagates_exception()
        {
            Assert.Throws<InvalidOperationException>(() =>
                AsyncBridge.RunSync<int>(async () =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("boom");
                }));
        }

        [Fact]
        public void RunSync_throws_on_null_factory_void()
        {
            Assert.Throws<ArgumentNullException>(() => AsyncBridge.RunSync((Func<Task>)null));
        }

        [Fact]
        public void RunSync_throws_on_null_factory_T()
        {
            Assert.Throws<ArgumentNullException>(() => AsyncBridge.RunSync((Func<Task<int>>)null));
        }

        [Fact]
        public void RunSync_does_not_deadlock_under_a_captured_SynchronizationContext()
        {
            // Reproduce the textbook "GetAwaiter().GetResult() inside a captured
            // context" scenario. AsyncBridge is supposed to be safe here because
            // it offloads to the thread pool with no captured context.
            var previous = SynchronizationContext.Current;
            var ctx = new SingleThreadedSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(ctx);
            try
            {
                int value = AsyncBridge.RunSync(async () =>
                {
                    await Task.Delay(10).ConfigureAwait(true);
                    return 123;
                });

                Assert.Equal(123, value);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
                ctx.Dispose();
            }
        }

        // Minimal pumping context that captures continuations onto a single worker
        // thread. Mirrors the WinForms / classic ASP.NET hostile environment that
        // makes naked .GetAwaiter().GetResult() deadlock.
        private sealed class SingleThreadedSynchronizationContext : SynchronizationContext, IDisposable
        {
            private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Cb, object State)> _queue = new();
            private readonly Thread _worker;

            public SingleThreadedSynchronizationContext()
            {
                _worker = new Thread(() =>
                {
                    foreach (var item in _queue.GetConsumingEnumerable())
                    {
                        try
                        {
                            item.Cb(item.State);
                        }
                        catch
                        {
                            // swallow: this is a test-only pump
                        }
                    }
                })
                {
                    IsBackground = true,
                    Name = nameof(SingleThreadedSynchronizationContext)
                };
                _worker.Start();
            }

            public override void Post(SendOrPostCallback d, object state) => _queue.Add((d, state));

            public override void Send(SendOrPostCallback d, object state) => d(state);

            public void Dispose()
            {
                _queue.CompleteAdding();
                _worker.Join(TimeSpan.FromSeconds(5));
                _queue.Dispose();
            }
        }
    }
}
