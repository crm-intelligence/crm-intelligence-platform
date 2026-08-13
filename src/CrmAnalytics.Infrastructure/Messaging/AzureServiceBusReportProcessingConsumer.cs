using Azure.Messaging.ServiceBus;
using CrmAnalytics.Application.Outbox;
using CrmAnalytics.Infrastructure.ReportProcessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrmAnalytics.Infrastructure.Messaging;

public sealed class AzureServiceBusReportProcessingConsumer : BackgroundService
{
    private readonly ServiceBusProcessor _processor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AzureServiceBusMessageValidator _messageValidator;
    private readonly ILogger<AzureServiceBusReportProcessingConsumer> _logger;
    private readonly MessagingRuntimeState _runtimeState;

    public AzureServiceBusReportProcessingConsumer(
        ServiceBusProcessor processor,
        IServiceScopeFactory scopeFactory,
        AzureServiceBusMessageValidator messageValidator,
        MessagingRuntimeState runtimeState,
        ILogger<AzureServiceBusReportProcessingConsumer> logger)
    {
        _processor = processor;
        _scopeFactory = scopeFactory;
        _messageValidator = messageValidator;
        _runtimeState = runtimeState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;
        await _processor.StartProcessingAsync(stoppingToken);
        _runtimeState.ConsumerRunning = true;
        _runtimeState.BrokerFailureObserved = false;
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await _processor.StopProcessingAsync(CancellationToken.None);
            _runtimeState.ConsumerRunning = false;
            _processor.ProcessMessageAsync -= ProcessMessageAsync;
            _processor.ProcessErrorAsync -= ProcessErrorAsync;
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var cancellationToken = args.CancellationToken;
        try
        {
            var envelope = _messageValidator.ValidateAndDeserialize(
                args.Message);
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<IReportProcessingMessageHandler>()
                .ProcessAsync(
                    envelope,
                    cancellationToken,
                    new ReportProcessingDeliveryContext(
                        args.Message.DeliveryCount,
                        args.Message.MessageId));
            await args.CompleteMessageAsync(args.Message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ReportProcessingPermanentException exception)
        {
            await DeadLetterAsync(args, exception.Reason,
                "The report processing request is not processable.");
        }
        catch (NotSupportedException)
        {
            await DeadLetterAsync(args, "UnsupportedSchemaVersion",
                "The message schema version is not supported.");
        }
        catch (Exception exception)
            when (exception is InvalidDataException or ArgumentException)
        {
            await DeadLetterAsync(args, "InvalidEnvelope",
                "The processing message envelope is invalid.");
        }
        catch (ReportProcessingTransientException)
        {
            await args.AbandonMessageAsync(args.Message,
                cancellationToken: cancellationToken);
        }
        catch (ServiceBusException exception)
            when (exception.Reason == ServiceBusFailureReason.MessageLockLost)
        {
            _logger.LogWarning("A report processing message lock was lost.");
        }
        catch (Exception)
        {
            await args.AbandonMessageAsync(args.Message,
                cancellationToken: cancellationToken);
        }
    }

    private static Task DeadLetterAsync(ProcessMessageEventArgs args,
        string reason, string description) =>
        args.DeadLetterMessageAsync(args.Message, reason, description,
            args.CancellationToken);

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(
            "Azure Service Bus processing failed with reason {ErrorSource}.",
            args.ErrorSource);
        _runtimeState.BrokerFailureObserved = true;
        return Task.CompletedTask;
    }
}
