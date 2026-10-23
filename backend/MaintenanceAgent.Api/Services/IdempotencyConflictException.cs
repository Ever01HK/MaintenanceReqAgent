namespace MaintenanceAgent.Api.Services;

public class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException(string message)
        : base(message)
    {
    }
}