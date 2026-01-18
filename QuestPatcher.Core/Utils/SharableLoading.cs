using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace QuestPatcher.Core.Utils
{
    public abstract class SharableLoading<T> : IDisposable where T : class
    {
        private readonly AsyncLock _lock = new();

        private Task<T>? _loadTask;
        private CancellationTokenSource? _cancellationTokenSource;
        private int _disposed;

        protected T? Data { get; private set; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            CancellationTokenSource? cts;
            using (_lock.Lock())
            {
                cts = _cancellationTokenSource;
                _cancellationTokenSource = null;
                Data = null;
                _loadTask = null;
            }

            cts?.Cancel();
            cts?.Dispose();
        }

        public void Init()
        {
            Task.Run(async () =>
            {
                try
                {
                    await GetOrLoadAsync(true);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    Log.Error(e, "Initial load failed for {Name}", GetType().Name);
                }
            });
        }

        protected abstract Task<T> LoadAsync(CancellationToken cToken);

        public async Task RefreshAsync()
        {
            await GetOrLoadAsync(true);
        }

        protected async Task<T> GetOrLoadAsync(bool refresh, CancellationToken cToken = default)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(SharableLoading<T>));
            }

            Task<T>? resultTask = null;
            CancellationTokenSource? oldCts = null;
            using (await _lock.LockAsync(cToken).ConfigureAwait(false))
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    throw new ObjectDisposedException(nameof(SharableLoading<T>));
                }

                if (!refresh)
                {
                    // check and return existing data / task
                    if (Data is not null)
                    {
                        return Data;
                    }

                    if (_loadTask != null &&
                        _loadTask.Status != TaskStatus.Canceled &&
                        _loadTask.Status != TaskStatus.Faulted)
                    {
                        resultTask = _loadTask;
                    }
                }

                if (resultTask is null)
                {
                    // we need to load new data
                    oldCts = _cancellationTokenSource;
                    var newCts = new CancellationTokenSource();

                    try
                    {
                        resultTask = LoadAsync(newCts.Token);
                    }
                    catch
                    {
                        newCts.Dispose();
                        throw;
                    }

                    _cancellationTokenSource = newCts;
                    _loadTask = resultTask;
                }
            }

            oldCts?.Cancel();
            oldCts?.Dispose();

            var result = await resultTask.ConfigureAwait(false);

            // silently return the result and don't do anything else
            if (Volatile.Read(ref _disposed) != 0)
            {
                return result;
            }

            using (await _lock.LockAsync(cToken).ConfigureAwait(false))
            {
                if (ReferenceEquals(resultTask, _loadTask))
                {
                    // it is the same task, there no new task created between our two lock acquires
                    // update cache data
                    Data = result;
                    _loadTask = null;
                }

                return result;
            }
        }
    }
}
