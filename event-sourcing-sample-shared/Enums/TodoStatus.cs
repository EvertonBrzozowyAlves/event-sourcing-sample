using System.Text.Json.Serialization;

namespace event_sourcing_sample_shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TodoStatus
{
    Pending = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4
}
