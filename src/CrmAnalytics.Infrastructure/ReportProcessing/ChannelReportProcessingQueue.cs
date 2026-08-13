using System.Threading.Channels;
using CrmAnalytics.Application.ReportProcessing;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.ReportProcessing;

public sealed class ChannelReportProcessingQueue
    : IReportProcessingQueue
{
    private readonly Channel<QueuedReportProcessingRequest> _channel;

    public ChannelReportProcessingQueue(
        IOptions<ReportProcessingQueueOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var capacity = options.Value.Capacity;

        if (capacity is < 1 or > 10000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Queue capacity must be between 1 and 10000.");
        }

        _channel = Channel.CreateBounded<QueuedReportProcessingRequest>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
    }

    public ValueTask EnqueueAsync(
        QueuedReportProcessingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _channel.Writer.WriteAsync(request, cancellationToken);
    }

    public ValueTask<QueuedReportProcessingRequest> DequeueAsync(
        CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }
}
