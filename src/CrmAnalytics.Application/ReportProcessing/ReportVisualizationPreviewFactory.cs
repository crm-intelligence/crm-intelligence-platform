using System.Globalization;
using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.Notifications;

namespace CrmAnalytics.Application.ReportProcessing;

internal static class ReportVisualizationPreviewFactory
{
    internal const int MaximumDataPoints = 8;

    public static ReportVisualizationPreview? Create(
        QueryExecutionResult? result,
        SqlResultShapeMetadata? shape)
    {
        if (result is null || shape is null || result.RowCount == 0
            || shape.Metrics.Count != 1)
            return null;

        var metricName = shape.Metrics.Single();
        var metricColumn = FindColumn(result, metricName);
        if (metricColumn is null || !IsNumeric(metricColumn.ValueKind))
            return null;

        var metricMetadata = FindMetadata(shape, metricName);
        var metricLabel = NormalizeLabel(
            metricMetadata?.Label, metricName);

        if (shape.Dimensions.Count == 0
            && IsVisual(shape, "KpiCard")
            && result.RowCount == 1
            && TryReadNumber(result.Rows[0], metricColumn, out var scalar))
        {
            return new ReportVisualizationPreview
            {
                Kind = ReportVisualizationKinds.Kpi,
                Title = metricLabel,
                ValueLabel = metricLabel,
                DataPoints =
                [
                    new ReportVisualizationDataPoint
                    {
                        Category = string.Empty,
                        Value = scalar
                    }
                ],
                TotalRowCount = result.RowCount
            };
        }

        if (shape.Dimensions.Count != 1)
            return null;

        var dimensionName = shape.Dimensions.Single();
        var dimensionColumn = FindColumn(result, dimensionName);
        if (dimensionColumn is null)
            return null;

        var dimensionMetadata = FindMetadata(shape, dimensionName);
        var dimensionLabel = NormalizeLabel(
            dimensionMetadata?.Label, dimensionName);
        var kind = IsVisual(shape, "LineChart")
            || dimensionMetadata?.IsTimeDimension == true
                ? ReportVisualizationKinds.Line
                : IsVisual(shape, "BarChart")
                    ? ReportVisualizationKinds.Bar
                    : null;
        if (kind is null) return null;

        var points = new List<ReportVisualizationDataPoint>(
            Math.Min(result.RowCount, MaximumDataPoints));
        foreach (var row in result.Rows)
        {
            if (!TryReadCategory(row, dimensionColumn, out var category)
                || !TryReadNumber(row, metricColumn, out var number))
                return null;

            points.Add(new ReportVisualizationDataPoint
            {
                Category = category,
                Value = number
            });
        }

        if (kind == ReportVisualizationKinds.Bar)
        {
            points = points
                .OrderByDescending(point => point.Value)
                .Take(MaximumDataPoints)
                .ToList();
        }
        else
        {
            points = points.Take(MaximumDataPoints).ToList();
        }

        return new ReportVisualizationPreview
        {
            Kind = kind,
            Title = $"{dimensionLabel} bazında {metricLabel}",
            CategoryLabel = dimensionLabel,
            ValueLabel = metricLabel,
            DataPoints = points,
            TotalRowCount = result.RowCount
        };
    }

    private static QueryResultColumn? FindColumn(
        QueryExecutionResult result,
        string name) => result.Columns.SingleOrDefault(column =>
            string.Equals(column.Name, name,
                StringComparison.OrdinalIgnoreCase));

    private static SqlResultColumnMetadata? FindMetadata(
        SqlResultShapeMetadata shape,
        string name) => shape.Columns.SingleOrDefault(column =>
            string.Equals(column.Name, name,
                StringComparison.OrdinalIgnoreCase));

    private static bool IsVisual(
        SqlResultShapeMetadata shape,
        string expected) => string.Equals(
            shape.SuggestedVisual,
            expected,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsNumeric(QueryResultValueKind kind) => kind is
        QueryResultValueKind.Int32
        or QueryResultValueKind.Int64
        or QueryResultValueKind.Decimal
        or QueryResultValueKind.Double;

    private static bool TryReadNumber(
        QueryResultRow row,
        QueryResultColumn column,
        out double value)
    {
        value = default;
        if (column.Ordinal < 0 || column.Ordinal >= row.Values.Count)
            return false;
        var cell = row.Values[column.Ordinal];
        if (cell.Value is null || !IsNumeric(cell.Kind)) return false;
        try
        {
            value = Convert.ToDouble(cell.Value, CultureInfo.InvariantCulture);
            return double.IsFinite(value);
        }
        catch (Exception exception) when (exception is FormatException
            or InvalidCastException or OverflowException)
        {
            return false;
        }
    }

    private static bool TryReadCategory(
        QueryResultRow row,
        QueryResultColumn column,
        out string value)
    {
        value = string.Empty;
        if (column.Ordinal < 0 || column.Ordinal >= row.Values.Count)
            return false;
        var cell = row.Values[column.Ordinal];
        if (cell.Value is null || cell.Kind == QueryResultValueKind.Null)
            return false;

        value = cell.Value switch
        {
            DateOnly date => date.ToString("yyyy-MM-dd",
                CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd",
                CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString(
                "yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(
                null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => cell.Value.ToString() ?? string.Empty
        };
        value = NormalizeSingleLine(value, 100);
        return value.Length > 0;
    }

    private static string NormalizeLabel(string? label, string semanticKey)
    {
        var value = string.IsNullOrWhiteSpace(label)
            ? semanticKey.Replace('_', ' ')
            : label;
        return NormalizeSingleLine(value, 100);
    }

    private static string NormalizeSingleLine(string value, int maximumLength)
    {
        var normalized = string.Join(' ', value
            .Split(['\r', '\n', '\t'],
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries));
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }
}
