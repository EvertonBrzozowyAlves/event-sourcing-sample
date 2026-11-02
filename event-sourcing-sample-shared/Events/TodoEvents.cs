using event_sourcing_sample_shared.Enums;

namespace event_sourcing_sample_shared.Events;

public record TodoCreated(
    long Id,
    Guid ExternalId,
    string Description,
    TodoStatus Status,
    bool IsDone,
    bool IsDeleted,
    DateTimeOffset CreatedAt
);

public record TodoUpdated(
    long Id,
    string Description,
    TodoStatus Status,
    DateTimeOffset UpdatedAt
);

public record TodoCompleted(
    long Id,
    DateTimeOffset CompletedAt
);

public record TodoDeleted(
    long Id,
    DateTimeOffset DeletedAt
);

