using System.Text.Json;
using System.Text.Json.Serialization;
using EventStore.Client;
using event_sourcing_sample_api.Domain;
using event_sourcing_sample_shared.Events;

namespace event_sourcing_sample_api.Repositories;

public class TodoRepository : ITodoRepository
{
    private readonly EventStoreClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public TodoRepository(EventStoreClient client)
    {
        _client = client;
    }

    private static string GetStreamName(long id) => $"todo-{id}";

    public async Task<TodoAggregate?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var streamName = GetStreamName(id);

        try
        {
            var readResult = _client.ReadStreamAsync(
                Direction.Forwards,
                streamName,
                StreamPosition.Start,
                cancellationToken: ct
            );

            if (await readResult.ReadState == ReadState.StreamNotFound)
            {
                return null;
            }

            var events = new List<object>();

            await foreach (var resolvedEvent in readResult.WithCancellation(ct))
            {
                var @event = DeserializeEvent(resolvedEvent.Event.EventType, resolvedEvent.Event.Data.Span);
                if (@event != null)
                {
                    events.Add(@event);
                }
            }

            if (events.Count == 0)
            {
                return null;
            }

            var aggregate = new TodoAggregate();
            aggregate.LoadFromHistory(events);
            return aggregate;
        }
        catch (StreamNotFoundException)
        {
            return null;
        }
    }

    public async Task SaveAsync(TodoAggregate aggregate, CancellationToken ct = default)
    {
        var uncommittedEvents = aggregate.GetUncommittedEvents();
        if (uncommittedEvents.Count == 0)
        {
            return;
        }

        var streamName = GetStreamName(aggregate.Id);
        var eventDataList = new List<EventData>();

        foreach (var @event in uncommittedEvents)
        {
            var eventType = @event.GetType().Name;
            var payload = JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType(), JsonOptions);
            eventDataList.Add(new EventData(
                Uuid.NewUuid(),
                eventType,
                payload
            ));
        }

        try
        {
            if (aggregate.Version < 0)
            {
                await _client.AppendToStreamAsync(
                    streamName,
                    StreamState.NoStream,
                    eventDataList,
                    cancellationToken: ct
                );
            }
            else
            {
                await _client.AppendToStreamAsync(
                    streamName,
                    StreamRevision.FromInt64(aggregate.Version),
                    eventDataList,
                    cancellationToken: ct
                );
            }

            aggregate.ClearUncommittedEvents();
        }
        catch (WrongExpectedVersionException ex)
        {
            throw new DomainException($"Conflito de concorrência ou stream já existente para o Todo #{aggregate.Id}: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<object>> GetEventsAsync(long id, CancellationToken ct = default)
    {
        var streamName = GetStreamName(id);
        var events = new List<object>();

        try
        {
            var readResult = _client.ReadStreamAsync(
                Direction.Forwards,
                streamName,
                StreamPosition.Start,
                cancellationToken: ct
            );

            if (await readResult.ReadState == ReadState.StreamNotFound)
            {
                return events;
            }

            await foreach (var resolvedEvent in readResult.WithCancellation(ct))
            {
                var @event = DeserializeEvent(resolvedEvent.Event.EventType, resolvedEvent.Event.Data.Span);
                if (@event != null)
                {
                    events.Add(@event);
                }
            }
        }
        catch (StreamNotFoundException)
        {
            // Empty list
        }

        return events;
    }

    private static object? DeserializeEvent(string eventType, ReadOnlySpan<byte> data)
    {
        return eventType switch
        {
            nameof(TodoCreated) => JsonSerializer.Deserialize<TodoCreated>(data, JsonOptions),
            nameof(TodoUpdated) => JsonSerializer.Deserialize<TodoUpdated>(data, JsonOptions),
            nameof(TodoCompleted) => JsonSerializer.Deserialize<TodoCompleted>(data, JsonOptions),
            nameof(TodoDeleted) => JsonSerializer.Deserialize<TodoDeleted>(data, JsonOptions),
            _ => null
        };
    }
}

