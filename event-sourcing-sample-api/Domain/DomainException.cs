namespace event_sourcing_sample_api.Domain;

public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}

