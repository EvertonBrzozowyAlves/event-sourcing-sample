using System.Text.Json.Serialization;
using event_sourcing_sample_shared.Enums;
using MongoDB.Bson.Serialization.Attributes;

namespace event_sourcing_sample_shared.Models;

public class TodoReadModel
{
    [BsonId]
    public long Id { get; set; }

    [BsonRepresentation(MongoDB.Bson.BsonType.String)]
    public Guid ExternalId { get; set; }

    public string Description { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    [BsonRepresentation(MongoDB.Bson.BsonType.String)]
    public TodoStatus Status { get; set; }

    public bool IsDone { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}

