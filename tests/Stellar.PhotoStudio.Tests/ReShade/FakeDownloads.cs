using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio.Tests.ReShade;

/// <summary>IPluginDownloads whose calls complete when the test says so (continuations run inline: tests clear the
/// SynchronizationContext first).</summary>
internal sealed class FakeDownloads : IPluginDownloads
{
    public sealed record Call(DownloadRequest Request, IProgress<double>? Progress, TaskCompletionSource<DownloadResult> Done);

    public readonly List<Call> Calls = new();
    public string DataFolder => "/data";

    public Task<DownloadResult> DownloadAsync(DownloadRequest request, IProgress<double>? progress, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<DownloadResult>();
        ct.Register(() => tcs.TrySetCanceled(ct));
        Calls.Add(new Call(request, progress, tcs));
        return tcs.Task;
    }
}
