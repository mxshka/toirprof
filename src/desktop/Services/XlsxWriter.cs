using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;

namespace ToirProf.Desktop.Services;

/// <summary>
/// Выгрузка таблицы в Excel (XLSX) средствами стандартной библиотеки.
///
/// XLSX — это ZIP-архив с несколькими XML-файлами; для одного листа
/// хватает шести. Внешняя библиотека ради одной кнопки не нужна, а
/// <see cref="ZipArchive"/> есть в .NET из коробки и работает на РедОС
/// так же, как на Windows.
///
/// Числа пишутся числовыми ячейками (по ним работают формулы и
/// сортировка), текст — inline-строками, без таблицы sharedStrings.
/// </summary>
public static class XlsxWriter
{
    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Записать лист: заголовок жирным и закреплён, затем строки.</summary>
    public static void Write(string path, string sheetTitle, IReadOnlyList<string> header, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var all = new List<IReadOnlyList<object?>> { header.Cast<object?>().ToList() };
        all.AddRange(rows);

        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        Add(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "</Types>");

        Add(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");

        Add(zip, "xl/workbook.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            $"<workbook xmlns=\"{MainNs}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            $"<sheets><sheet name=\"{Escape(SheetName(sheetTitle))}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");

        Add(zip, "xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>");

        Add(zip, "xl/styles.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            $"<styleSheet xmlns=\"{MainNs}\">" +
            "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
            "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
            "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>");

        Add(zip, "xl/worksheets/sheet1.xml", BuildSheet(header.Count, all));
    }

    private static string BuildSheet(int columns, List<IReadOnlyList<object?>> all)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append($"<worksheet xmlns=\"{MainNs}\">");
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");

        // Ширина столбца по самому длинному значению: Excel сам этого не делает.
        sb.Append("<cols>");
        for (var c = 0; c < columns; c++)
        {
            var longest = all.Max(row => c < row.Count ? Text(row[c]).Length : 0);
            var width = Math.Clamp(longest + 2, 8, 60);
            sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{width}\" customWidth=\"1\"/>");
        }

        sb.Append("</cols><sheetData>");
        for (var r = 0; r < all.Count; r++)
        {
            sb.Append($"<row r=\"{r + 1}\">");
            var row = all[r];
            for (var c = 0; c < row.Count; c++)
            {
                AppendCell(sb, row[c], $"{ColumnName(c)}{r + 1}", bold: r == 0);
            }

            sb.Append("</row>");
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void AppendCell(StringBuilder sb, object? value, string reference, bool bold)
    {
        var style = bold ? " s=\"1\"" : string.Empty;
        switch (value)
        {
            case null:
                return;
            case int or long or short or decimal or double or float:
                var number = Convert.ToString(value, CultureInfo.InvariantCulture);
                sb.Append($"<c r=\"{reference}\"{style}><v>{number}</v></c>");
                return;
            default:
                var text = Text(value);
                if (text.Length == 0)
                {
                    return;
                }

                sb.Append($"<c r=\"{reference}\" t=\"inlineStr\"{style}><is><t xml:space=\"preserve\">{Escape(text)}</t></is></c>");
                return;
        }
    }

    private static string Text(object? value) => value switch
    {
        null => string.Empty,
        DateOnly d => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>0 → A, 25 → Z, 26 → AA.</summary>
    private static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + ((n - 1) % 26)) + name;
        }

        return name;
    }

    /// <summary>Имя листа: до 31 символа и без []:*?/\.</summary>
    private static string SheetName(string title)
    {
        var cleaned = new string(title.Select(ch => "[]:*?/\\".Contains(ch) ? ' ' : ch).ToArray()).Trim();
        if (cleaned.Length == 0)
        {
            cleaned = "Лист1";
        }

        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    /// <summary>XML-экранирование; управляющие символы XML 1.0 запрещены.</summary>
    private static string Escape(string value)
    {
        var filtered = new string(value.Where(ch => ch >= 0x20 || ch is '\t' or '\n' or '\r').ToArray());
        return SecurityElement.Escape(filtered) ?? string.Empty;
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Utf8NoBom);
        writer.Write(content);
    }
}
