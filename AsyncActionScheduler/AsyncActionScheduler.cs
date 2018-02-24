using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncScheduler
{
    public class AsyncActionScheduler : IDisposable
    {
        private readonly ConcurrentQueue<Func<Task>> queue;

        private readonly ConcurrentQueue<Func<Task>> immediate;

        private readonly CancellationTokenSource canceller;

        private int state; // [ 0: stopped, 1: setup, 2: running ]

        private Task runner;

        public AsyncActionScheduler()
        {
            this.queue = new ConcurrentQueue<Func<Task>>();
            this.immediate = new ConcurrentQueue<Func<Task>>();
            this.canceller = new CancellationTokenSource();
        }

        public Task Add(Action<CancellationToken> action)
            => this.Add(token => { action(token); return Task.FromResult(default(object)); });

        public Task<T> Add<T>(Func<CancellationToken, T> action)
            => this.Add(token => Task.FromResult(action(token)));

        public Task Add(Func<CancellationToken, Task> action)
            => this.Add(token => action(token).ContinueWith(_ => default(object)));

        public Task<T> Add<T>(Func<CancellationToken, Task<T>> action)
            => this.Schedule(this.queue, action);

        public Task Interrupt(Action<CancellationToken> action)
            => this.Interrupt(token => { action(token); return Task.FromResult(default(object)); });

        public Task<T> Interrupt<T>(Func<CancellationToken, T> action)
            => this.Interrupt(token => Task.FromResult(action(token)));

        public Task Interrupt(Func<CancellationToken, Task> action)
            => this.Interrupt(token => action(token).ContinueWith(_ => default(object)));

        public Task<T> Interrupt<T>(Func<CancellationToken, Task<T>> action)
            => this.Schedule(this.immediate, action);

        private Task<T> Schedule<T>(ConcurrentQueue<Func<Task>> target, Func<CancellationToken, Task<T>> action)
        {
            if (this.canceller.IsCancellationRequested)
                return Task.FromCanceled<T>(this.canceller.Token);
            var tcs = new TaskCompletionSource<T>();
            target.Enqueue(() => InvokeAsync(action, tcs));
            this.Run();
            return tcs.Task;
        }

        private async Task InvokeAsync<T>(Func<CancellationToken, Task<T>> action, TaskCompletionSource<T> tcs)
        {
            try
            {
                this.canceller.Token.ThrowIfCancellationRequested();
                tcs.TrySetResult(await action(this.canceller.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException exception)
            {
                tcs.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                tcs.TrySetException(exception);
            }
        }

        private Task Run()
        {
            while (Interlocked.CompareExchange(ref this.state, 1, 0) != 0)
                if (this.state == 2)
                    return this.runner;
            this.runner = Task.Run(async () =>
            {
                while (this.immediate.TryDequeue(out var action) || this.queue.TryDequeue(out action))
                    await action().ConfigureAwait(false);
                while (Interlocked.CompareExchange(ref this.state, 0, 2) != 2) ;
                if (this.queue.Count != 0 || this.immediate.Count != 0)
                    await this.Run().ConfigureAwait(false);
            });
            Interlocked.Exchange(ref this.state, 2);
            return this.runner;
        }

        public void Dispose()
        {
            this.canceller.Cancel();
            this.Run().Wait();
        }
    }
}
