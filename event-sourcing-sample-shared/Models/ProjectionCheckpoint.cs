using MongoDB.Bson.Serialization.Attributes;

namespace event_sourcing_sample_shared.Models;

public class ProjectionCheckpoint
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    public ulong CommitPosition { get; set; }

    public ulong PreparePosition { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

