using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Routing;

/// <summary>Sonuc yapisina onerilen gorsel tipi.</summary>
public enum VisualType
{
    /// <summary>Tek skaler deger.</summary>
    KpiCard,

    /// <summary>Zaman ekseninde seyir.</summary>
    LineChart,

    /// <summary>Kategoriye gore karsilastirma.</summary>
    BarChart,

    /// <summary>Iki veya daha fazla boyutun kesisimi.</summary>
    Matrix,

    /// <summary>Olcum icermeyen satir listesi.</summary>
    Table
}

/// <summary>
/// Sonuc yapisi ve onerilen gorsel.
/// </summary>
/// <param name="MetricCount">Olcum sayisi.</param>
/// <param name="DimensionCount">Kirilim sayisi.</param>
/// <param name="HasTimeDimension">Kirilimlardan biri zaman ekseni mi.</param>
/// <param name="SuggestedVisual">Onerilen gorsel tipi.</param>
/// <param name="Rationale">
/// Onerinin gerekcesi. BI tarafi oneriyi kabul etmek zorunda degil; gerekce olmadan
/// "neden bar degil cizgi" tartismasi her rapor icin bastan yapilirdi.
/// </param>
public sealed record ResultShape(
    int MetricCount,
    int DimensionCount,
    bool HasTimeDimension,
    VisualType SuggestedVisual,
    string Rationale);

/// <summary>
/// Canonical Request'ten sonuc yapisini siniflandirir ve gorsel tipi onerir.
/// </summary>
/// <remarks>
/// <para>
/// Sinifllandirma <b>sonuc setine degil talebe</b> bakar: oneri, sorgu calismadan once
/// uretilebilmeli ki BI rapor sayfasi hazirlanabilsin.
/// </para>
/// <para>
/// Bu bir <b>oneridir</b>, karar degil. Gorsel secimi BI tarafinin sorumlulugunda; buradan
/// cikan deger sonuc sozlesmesinin bir alanidir.
/// </para>
/// </remarks>
public static class ResultShapeClassifier
{
    public static ResultShape Classify(CanonicalRequest request, MetricCatalogDocument catalog)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalog);

        var metricCount = request.Metrics.Count;
        var dimensionCount = request.Dimensions.Count;

        var hasTimeDimension = request.Dimensions
            .Select(catalog.FindDimension)
            .Any(dimension => dimension?.IsTimeDimension == true);

        var (visual, rationale) = Decide(metricCount, dimensionCount, hasTimeDimension, request.Grain);

        return new ResultShape(metricCount, dimensionCount, hasTimeDimension, visual, rationale);
    }

    private static (VisualType Visual, string Rationale) Decide(
        int metricCount,
        int dimensionCount,
        bool hasTimeDimension,
        TimeGrain grain)
    {
        if (metricCount == 0)
        {
            return (VisualType.Table, "Olcum yok; sonuc bir satir listesi.");
        }

        if (dimensionCount == 0)
        {
            return (VisualType.KpiCard, "Kirilim yok; sonuc tek satir ve tek deger kumesi.");
        }

        if (dimensionCount >= 2)
        {
            return (VisualType.Matrix,
                $"{dimensionCount} kirilim var; iki boyutun kesisimi tablo/matrix olarak okunur.");
        }

        // Tek kirilim. Zaman ekseni ise seyir, degilse karsilastirma.
        if (hasTimeDimension)
        {
            // Grain None ise tarih kolonu ham deger olarak gelir; yine de zaman eksenidir.
            var grainNote = grain == TimeGrain.None
                ? "kirilim ham tarih degeri"
                : $"kirilim {grain} bazinda";

            return (VisualType.LineChart, $"Tek zaman kirilimi ({grainNote}); seyir cizgi ile okunur.");
        }

        return (VisualType.BarChart, "Tek kategorik kirilim; kategoriler arasi karsilastirma bar ile okunur.");
    }
}
