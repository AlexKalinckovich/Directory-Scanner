using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Directory_Scanner.Core.FileModels;
using Directory_Scanner.Core.ScannerEventArgs;

namespace Directory_Scanner.Core.Core;

public sealed class EventDispatcher : IDisposable
{
    private readonly ConcurrentQueue<object?> _eventQueue;
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly TaskCompletionSource<bool> _completionTcs;
    private bool _isDisposed;

    public event EventHandler<StartProcessingDirectoryEventArgs>? StartProcessingDirectory;
    public event EventHandler<DirectoryProcessedEventArgs>? DirectoryProcessed;
    public event EventHandler<FileProcessedEventArgs>? FileProcessed;
    public event EventHandler<ProcessingCompletedEventArgs>? ProcessingCompleted;

    public EventDispatcher()
    {
        _eventQueue = new ConcurrentQueue<object?>();
        _cancellationTokenSource = new CancellationTokenSource();
        _processingTask = Task.Run(ProcessEventsAsync);
        _completionTcs = new TaskCompletionSource<bool>();
        _isDisposed = false;
    }

    private async Task ProcessEventsAsync()
    {
        CancellationToken token = _cancellationTokenSource.Token;

        while (!token.IsCancellationRequested)
        {
            if (_eventQueue.TryDequeue(out object? eventData))
            {
                DispatchEvent(eventData);

                if (eventData is ProcessingCompletedEventArgs)
                {
                    _completionTcs.TrySetResult(true);
                }
            }
            else
            {
                await Task.Delay(1, token);
            }
        }
    }
    
    private void DispatchEvent(object? eventData)
    {
        if (eventData is StartProcessingDirectoryEventArgs startArgs)
        {
            StartProcessingDirectory?.Invoke(this, startArgs);
        }
        else if (eventData is FileProcessedEventArgs fileArgs)
        {
            FileProcessed?.Invoke(this, fileArgs);
        }
        else if (eventData is DirectoryProcessedEventArgs dirArgs)
        {
            DirectoryProcessed?.Invoke(this, dirArgs);
        }
        else if (eventData is ProcessingCompletedEventArgs completedArgs)
        {
            ProcessingCompleted?.Invoke(this, completedArgs);
        }
    }

    public void EnqueueStartProcessing(FileEntry entry)
    {
        StartProcessingDirectoryEventArgs args = new StartProcessingDirectoryEventArgs(entry);
        _eventQueue.Enqueue(args);
    }

    public void EnqueueFileProcessed(FileEntry entry)
    {
        FileProcessedEventArgs args = new FileProcessedEventArgs(entry);
        _eventQueue.Enqueue(args);
    }

    public void EnqueueDirectoryProcessed(FileEntry entry)
    {
        DirectoryProcessedEventArgs args = new DirectoryProcessedEventArgs(entry);
        _eventQueue.Enqueue(args);
    }

    public void EnqueueProcessingCompleted(FileEntry entry)
    {
        ProcessingCompletedEventArgs args = new ProcessingCompletedEventArgs(entry);
        _eventQueue.Enqueue(args);
    }

    public Task WaitForCompletionAsync()
    {
        return _completionTcs.Task;
    }

    public async Task StopAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await _cancellationTokenSource.CancelAsync();

        try
        {
            await _processingTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (AggregateException ex)
        {
            foreach (Exception innerEx in ex.InnerExceptions)
            {
                if (innerEx is not OperationCanceledException)
                {
                    throw;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        _cancellationTokenSource.Cancel();

        try
        {
            _processingTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }
        catch (TimeoutException)
        {
        }

        _cancellationTokenSource.Dispose();

        if (_processingTask.IsCompleted || _processingTask.IsCanceled || _processingTask.IsFaulted)
        {
            _processingTask.Dispose();
        }
    }
}