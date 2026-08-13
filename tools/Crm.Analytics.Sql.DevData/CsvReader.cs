namespace Crm.Analytics.Sql.DevData;

/// <summary>
/// Olist CSV'leri icin kasitli olarak MINIMAL bir okuyucu.
/// </summary>
/// <remarks>
/// Kaynak dosyalarda hic tirnak karakteri yok ve her satirin alan sayisi sabit
/// (dogrulandi). Bu yuzden tam bir RFC 4180 ayristiricisi yerine basit bolme
/// kullaniliyor. Ancak bu varsayim SESSIZCE degil, ACIK SEKILDE korunuyor:
/// <list type="bullet">
///   <item>tirnak iceren bir satir gorulurse istisna atilir,</item>
///   <item>alan sayisi beklenenden farkli olan satirda istisna atilir,</item>
///   <item>baslik satiri beklenen kolon adlariyla karsilastirilir.</item>
/// </list>
/// Boylece CSV'ler ileride tirnakli olarak yeniden uretilirse arac veriyi
/// bozmak yerine durur.
/// </remarks>
internal static class CsvReader
{
    /// <summary>
    /// Basligi dogrular ve veri satirlarini alan dizisi olarak akitir.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Baslik uyusmazliginda, tirnak bulundugunda veya alan sayisi
    /// tutmadiginda.
    /// </exception>
    public static IEnumerable<string[]> ReadRows(string path, TableSpec spec)
    {
        var expectedFieldCount = spec.Columns.Count;
        var lineNumber = 0;

        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;

            if (line.Contains('"', StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"{spec.CsvFileName}:{lineNumber} — tirnak karakteri bulundu. " +
                    "Bu arac tirnaksiz CSV varsayar (bkz. CsvReader aciklamasi). " +
                    "Veriyi yanlis ayristirmamak icin duruldu.");
            }

            // Son satirdaki olasi bos satiri atla.
            if (lineNumber > 1 && line.Length == 0)
            {
                continue;
            }

            var fields = line.Split(',');

            if (fields.Length != expectedFieldCount)
            {
                throw new InvalidDataException(
                    $"{spec.CsvFileName}:{lineNumber} — {expectedFieldCount} alan " +
                    $"bekleniyordu, {fields.Length} bulundu.");
            }

            if (lineNumber == 1)
            {
                ValidateHeader(fields, spec);
                continue;
            }

            yield return fields;
        }
    }

    private static void ValidateHeader(string[] header, TableSpec spec)
    {
        for (var i = 0; i < header.Length; i++)
        {
            // UTF-8 BOM ilk kolon adina yapisabilir.
            var actual = header[i].Trim().TrimStart('﻿');
            var expected = spec.Columns[i].Name;

            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"{spec.CsvFileName} basligi sozlesmeyle uyusmuyor: " +
                    $"{i}. kolonda '{expected}' bekleniyordu, '{actual}' bulundu. " +
                    "Kolon sirasi degistiyse TableSpec.cs guncellenmelidir.");
            }
        }
    }
}
