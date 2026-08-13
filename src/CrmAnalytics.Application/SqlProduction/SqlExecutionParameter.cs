namespace CrmAnalytics.Application.SqlProduction;

public sealed record SqlExecutionParameter(
    string Name,
    SqlExecutionParameterKind Kind,
    object? Value,
    bool IsUnicode,
    int? Size = null,
    byte? Precision = null,
    byte? Scale = null)
{
    public override string ToString() =>
        "SqlExecutionParameter { Sensitive data redacted }";
}

public enum SqlExecutionParameterKind
{
    Text = 1,
    Integer = 2,
    Decimal = 3,
    Boolean = 4,
    Date = 5
}
