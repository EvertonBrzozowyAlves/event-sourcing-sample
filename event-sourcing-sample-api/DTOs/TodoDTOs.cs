using System.ComponentModel.DataAnnotations;
using event_sourcing_sample_shared.Enums;

namespace event_sourcing_sample_api.DTOs;

public record CreateTodoRequest(
    long? Id,
    [Required] string Description,
    TodoStatus? Status,
    Guid? ExternalId
);

public record UpdateTodoRequest(
    [Required] string Description,
    [Required] TodoStatus Status
);

public record TodoResponse(
    long Id,
    Guid ExternalId,
    string Description,
    TodoStatus Status,
    bool IsDone,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt = null,
    DateTimeOffset? CompletedAt = null,
    DateTimeOffset? DeletedAt = null
);
