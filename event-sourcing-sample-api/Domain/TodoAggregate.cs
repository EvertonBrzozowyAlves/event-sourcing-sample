using event_sourcing_sample_shared.Enums;
using event_sourcing_sample_shared.Events;

namespace event_sourcing_sample_api.Domain;

public class TodoAggregate
{
    private readonly List<object> _uncommittedEvents = new();

    public long Id { get; private set; }
    public Guid ExternalId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public TodoStatus Status { get; private set; }
    public bool IsDone { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public long Version { get; private set; } = -1;

    public IReadOnlyCollection<object> GetUncommittedEvents() => _uncommittedEvents.AsReadOnly();
    public void ClearUncommittedEvents() => _uncommittedEvents.Clear();

    public static TodoAggregate Create(long id, Guid externalId, string description, TodoStatus status)
    {
        if (id <= 0)
        {
            throw new DomainException("O Id do Todo deve ser maior que zero.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new DomainException("A descrição do Todo não pode ser vazia.");
        }

        var aggregate = new TodoAggregate();
        var @event = new TodoCreated(
            Id: id,
            ExternalId: externalId == Guid.Empty ? Guid.NewGuid() : externalId,
            Description: description.Trim(),
            Status: status,
            IsDone: false,
            IsDeleted: false,
            CreatedAt: DateTimeOffset.UtcNow
        );

        aggregate.RaiseEvent(@event);
        return aggregate;
    }

    public void Update(string description, TodoStatus status)
    {
        if (IsDeleted)
        {
            throw new DomainException($"O Todo #{Id} não pode ser alterado pois está marcado como excluído.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new DomainException("A descrição do Todo não pode ser vazia.");
        }

        var @event = new TodoUpdated(
            Id: Id,
            Description: description.Trim(),
            Status: status,
            UpdatedAt: DateTimeOffset.UtcNow
        );

        RaiseEvent(@event);
    }

    public void MarkAsCompleted()
    {
        if (IsDeleted)
        {
            throw new DomainException($"O Todo #{Id} não pode ser concluído pois está marcado como excluído.");
        }

        if (IsDone)
        {
            throw new DomainException($"O Todo #{Id} já se encontra concluído.");
        }

        var @event = new TodoCompleted(
            Id: Id,
            CompletedAt: DateTimeOffset.UtcNow
        );

        RaiseEvent(@event);
    }

    public void Delete()
    {
        if (IsDeleted)
        {
            throw new DomainException($"O Todo #{Id} já está excluído.");
        }

        var @event = new TodoDeleted(
            Id: Id,
            DeletedAt: DateTimeOffset.UtcNow
        );

        RaiseEvent(@event);
    }

    public void LoadFromHistory(IEnumerable<object> history)
    {
        foreach (var @event in history)
        {
            Apply(@event);
            Version++;
        }
    }

    private void RaiseEvent(object @event)
    {
        Apply(@event);
        _uncommittedEvents.Add(@event);
    }

    private void Apply(object @event)
    {
        switch (@event)
        {
            case TodoCreated created:
                Id = created.Id;
                ExternalId = created.ExternalId;
                Description = created.Description;
                Status = created.Status;
                IsDone = created.IsDone;
                IsDeleted = created.IsDeleted;
                CreatedAt = created.CreatedAt;
                break;

            case TodoUpdated updated:
                Description = updated.Description;
                Status = updated.Status;
                UpdatedAt = updated.UpdatedAt;
                break;

            case TodoCompleted completed:
                IsDone = true;
                Status = TodoStatus.Completed;
                CompletedAt = completed.CompletedAt;
                break;

            case TodoDeleted deleted:
                IsDeleted = true;
                DeletedAt = deleted.DeletedAt;
                break;
        }
    }
}

