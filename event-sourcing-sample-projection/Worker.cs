using System.Text.Json;
using System.Text.Json.Serialization;
using EventStore.Client;
using event_sourcing_sample_shared.Enums;
using event_sourcing_sample_shared.Events;
using event_sourcing_sample_shared.Models;
using MongoDB.Driver;

namespace event_sourcing_sample_projection;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly EventStoreClient _eventStoreClient;
    private readonly IMongoDatabase _mongoDatabase;
    private readonly IMongoCollection<TodoReadModel> _todosCollection;
    private readonly IMongoCollection<ProjectionCheckpoint> _checkpointsCollection;

    private const string CheckpointId = "todos-projection";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public Worker(
        ILogger<Worker> logger,
        EventStoreClient eventStoreClient,
        IMongoDatabase mongoDatabase)
    {
        _logger = logger;
        _eventStoreClient = eventStoreClient;
        _mongoDatabase = mongoDatabase;
        _todosCollection = _mongoDatabase.GetCollection<TodoReadModel>("todos");
        _checkpointsCollection = _mongoDatabase.GetCollection<ProjectionCheckpoint>("checkpoints");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Iniciando serviço de projeção do KurrentDB para o MongoDB...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartSubscriptionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no loop da subscrição de projeção. Tentando reconectar em 5 segundos...");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task StartSubscriptionAsync(CancellationToken stoppingToken)
    {
        var checkpoint = await _checkpointsCollection
            .Find(x => x.Id == CheckpointId)
            .FirstOrDefaultAsync(stoppingToken);

        var startPosition = checkpoint != null
            ? FromAll.After(new Position(checkpoint.CommitPosition, checkpoint.PreparePosition))
            : FromAll.Start;

        _logger.LogInformation("Assinando stream $all a partir da posição: {Position}", 
            checkpoint != null ? $"{checkpoint.CommitPosition}/{checkpoint.PreparePosition}" : "Start");

        var subscription = _eventStoreClient.SubscribeToAll(
            startPosition,
            cancellationToken: stoppingToken
        );

        await foreach (var message in subscription.Messages.WithCancellation(stoppingToken))
        {
            if (message is StreamMessage.Event(var resolvedEvent))
            {
                await ProcessEventAsync(resolvedEvent, stoppingToken);
            }
        }
    }

    private async Task ProcessEventAsync(ResolvedEvent resolvedEvent, CancellationToken cancellationToken)
    {
        var eventType = resolvedEvent.Event.EventType;
        var streamId = resolvedEvent.Event.EventStreamId;

        // Ignora eventos de sistema do KurrentDB (iniciados com $)
        if (eventType.StartsWith("$") || !streamId.StartsWith("todo-", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _logger.LogInformation("Processando evento {EventType} do stream {StreamId}", eventType, streamId);

        var data = resolvedEvent.Event.Data.Span;

        switch (eventType)
        {
            case nameof(TodoCreated):
            {
                var @event = JsonSerializer.Deserialize<TodoCreated>(data, JsonOptions);
                if (@event != null)
                {
                    var readModel = new TodoReadModel
                    {
                        Id = @event.Id,
                        ExternalId = @event.ExternalId,
                        Description = @event.Description,
                        Status = @event.Status,
                        IsDone = @event.IsDone,
                        IsDeleted = @event.IsDeleted,
                        CreatedAt = @event.CreatedAt
                    };

                    await _todosCollection.ReplaceOneAsync(
                        x => x.Id == @event.Id,
                        readModel,
                        new ReplaceOptions { IsUpsert = true },
                        cancellationToken);

                    _logger.LogInformation("Projeção criada/atualizada para Todo #{Id}", @event.Id);
                }
                break;
            }

            case nameof(TodoUpdated):
            {
                var @event = JsonSerializer.Deserialize<TodoUpdated>(data, JsonOptions);
                if (@event != null)
                {
                    var updateDef = Builders<TodoReadModel>.Update
                        .Set(x => x.Description, @event.Description)
                        .Set(x => x.Status, @event.Status)
                        .Set(x => x.UpdatedAt, @event.UpdatedAt);

                    await _todosCollection.UpdateOneAsync(
                        x => x.Id == @event.Id,
                        updateDef,
                        cancellationToken: cancellationToken);

                    _logger.LogInformation("Projeção atualizada para Todo #{Id}", @event.Id);
                }
                break;
            }

            case nameof(TodoCompleted):
            {
                var @event = JsonSerializer.Deserialize<TodoCompleted>(data, JsonOptions);
                if (@event != null)
                {
                    var updateDef = Builders<TodoReadModel>.Update
                        .Set(x => x.IsDone, true)
                        .Set(x => x.Status, TodoStatus.Completed)
                        .Set(x => x.CompletedAt, @event.CompletedAt);

                    await _todosCollection.UpdateOneAsync(
                        x => x.Id == @event.Id,
                        updateDef,
                        cancellationToken: cancellationToken);

                    _logger.LogInformation("Projeção marcada como concluída para Todo #{Id}", @event.Id);
                }
                break;
            }

            case nameof(TodoDeleted):
            {
                var @event = JsonSerializer.Deserialize<TodoDeleted>(data, JsonOptions);
                if (@event != null)
                {
                    var updateDef = Builders<TodoReadModel>.Update
                        .Set(x => x.IsDeleted, true)
                        .Set(x => x.DeletedAt, @event.DeletedAt);

                    await _todosCollection.UpdateOneAsync(
                        x => x.Id == @event.Id,
                        updateDef,
                        cancellationToken: cancellationToken);

                    _logger.LogInformation("Projeção marcada como excluída para Todo #{Id}", @event.Id);
                }
                break;
            }
        }

        if (resolvedEvent.OriginalPosition.HasValue)
        {
            var pos = resolvedEvent.OriginalPosition.Value;
            var checkpoint = new ProjectionCheckpoint
            {
                Id = CheckpointId,
                CommitPosition = pos.CommitPosition,
                PreparePosition = pos.PreparePosition,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await _checkpointsCollection.ReplaceOneAsync(
                x => x.Id == CheckpointId,
                checkpoint,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);
        }
    }
}
