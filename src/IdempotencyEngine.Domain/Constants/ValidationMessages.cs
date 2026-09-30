namespace IdempotencyEngine.Domain.Constants;

public static class ValidationMessages
{
    public const string TextCannotBeNullOrWhitespace = "Text cannot be null or whitespace.";
    public const string EventIdCannotBeNullOrWhitespace = "EventId cannot be null or whitespace.";
    public const string KindCannotBeNullOrWhitespace = "Kind cannot be null or whitespace.";
    public const string DomainCannotBeNullOrWhitespace = "Domain cannot be null or whitespace.";
    public const string ContextCannotBeNull = "Context dictionary cannot be null.";
    public const string SolutionTextCannotBeNullOrWhitespace = "Solution text cannot be null or whitespace.";
}
