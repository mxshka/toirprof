using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

// Использование: dotnet run --project tools/qr-labels -- <db/seed.sql> <docs/qr-labels.html>
var seed = args.Length > 0 ? args[0] : Path.Combine("db", "seed.sql");
var output = args.Length > 1 ? args[1] : Path.Combine("docs", "qr-labels.html");

var sql = File.ReadAllText(seed);
var start = sql.IndexOf("INSERT INTO equipment (id", StringComparison.Ordinal);
var end = sql.IndexOf(';', start);
var items = Regex.Matches(sql[start..end], @"\('[0-9a-f-]{36}',\s*'([^']+)',\s*'([^']+)'")
    .Select(m => (Inventory: m.Groups[1].Value, Name: m.Groups[2].Value))
    .ToList();

var writer = new QRCodeWriter();
var hints = new Dictionary<EncodeHintType, object>
{
    [EncodeHintType.CHARACTER_SET] = "UTF-8",
    [EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.M,
    [EncodeHintType.MARGIN] = 2,
};

var failures = 0;
var html = new StringBuilder();
html.Append("""
<!DOCTYPE html>
<html lang="ru"><head><meta charset="utf-8"><title>QR-наклейки оборудования · ТОиР-Проф</title>
<style>
  @page { size: A4; margin: 12mm; }
  body { font-family: "Segoe UI", Arial, sans-serif; margin: 0; color: #111; }
  h1 { font-size: 16pt; margin: 0 0 4mm; }
  p.hint { font-size: 9pt; color: #555; margin: 0 0 6mm; }
  .grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 5mm; }
  .label { border: 1px dashed #999; padding: 4mm; text-align: center; break-inside: avoid; }
  .label svg { width: 42mm; height: 42mm; }
  .inv { font-size: 18pt; font-weight: 700; margin-top: 2mm; }
  .name { font-size: 8.5pt; color: #333; }
</style></head><body>
<h1>QR-наклейки оборудования</h1>
<p class="hint">Код содержит инвентарный номер. В мобильном клиенте: вкладка «QR-код» → «Сканировать QR-код» → паспорт, типовые неисправности, инструкции ТО и кнопка «Сообщить о неисправности».</p>
<div class="grid">
""");

foreach (var (inventory, name) in items)
{
    var matrix = writer.encode(inventory, BarcodeFormat.QR_CODE, 0, 0, hints);
    var decoded = Decode(matrix);
    if (decoded != inventory)
    {
        Console.WriteLine($"[QR-FAIL] {inventory}: распознано «{decoded}»");
        failures++;
    }
    html.Append("<div class=\"label\">");
    html.Append(ToSvg(matrix));
    html.Append($"<div class=\"inv\">{WebUtility.HtmlEncode(inventory)}</div>");
    html.Append($"<div class=\"name\">{WebUtility.HtmlEncode(name)}</div></div>\n");
}

html.Append("</div></body></html>\n");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, html.ToString(), new UTF8Encoding(false));
Console.WriteLine($"QR-наклеек: {items.Count} → {output}; распознаны обратно: {items.Count - failures} из {items.Count}");
return failures == 0 ? 0 : 1;

// Обратное распознавание тем же путём, что и в AndroidPlatform: пиксели BGRA32 → ZXing.
static string? Decode(BitMatrix matrix)
{
    const int scale = 8;
    var width = matrix.Width * scale;
    var height = matrix.Height * scale;
    var bytes = new byte[width * height * 4];
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++)
        {
            var value = matrix[x / scale, y / scale] ? (byte)0 : (byte)255;
            var i = (y * width + x) * 4;
            bytes[i] = value;
            bytes[i + 1] = value;
            bytes[i + 2] = value;
            bytes[i + 3] = 255;
        }
    }

    var reader = new BarcodeReaderGeneric { Options = new DecodingOptions { CharacterSet = "UTF-8", TryHarder = true } };
    return reader.Decode(new RGBLuminanceSource(bytes, width, height, RGBLuminanceSource.BitmapFormat.BGRA32))?.Text;
}

static string ToSvg(BitMatrix matrix)
{
    var path = new StringBuilder();
    for (var y = 0; y < matrix.Height; y++)
    {
        for (var x = 0; x < matrix.Width; x++)
        {
            if (matrix[x, y])
            {
                path.Append($"M{x} {y}h1v1h-1z");
            }
        }
    }

    return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {matrix.Width} {matrix.Height}\" shape-rendering=\"crispEdges\">" +
           $"<rect width=\"100%\" height=\"100%\" fill=\"#fff\"/><path d=\"{path}\" fill=\"#000\"/></svg>";
}
