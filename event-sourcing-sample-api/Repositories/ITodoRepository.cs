using event_sourcing_sample_api.Domain;

namespace event_sourcing_sample_api.Repositories;

public interface ITodoRepository
{
    Task<TodoAggregate?> GetByIdAsync(long id, CancellationToken ct = default);
    Task SaveAsync(TodoAggregate aggregate, CancellationToken ct = default);
    Task<IReadOnlyList<object>> GetEventsAsync(long id, CancellationToken ct = default);
}

