using System.Text.Json;

namespace Coral.PrinterBridge.Console.Bridge;

public sealed record ApiResult(bool Ok, string Message);
public enum PrinterTarget { Kitchen, Register }
public enum TextAlign { Left, Center, Right }
public sealed record PrintLine(string Text, TextAlign Align = TextAlign.Left, bool Bold = false, bool Underline = false, int Size = 1);
public sealed record PrintJob(IReadOnlyList<PrintLine> Lines, int Feed = 3, bool Cut = true, bool OpenDrawer = false, PrinterTarget Printer = PrinterTarget.Kitchen, bool Logo = false);

public static class PrintJobParser
{
    public static bool TryParse(JsonElement root, out PrintJob? job, out string error)
    {
        job = null; error = "Invalid payload";
        if (root.ValueKind != JsonValueKind.Object) { error = "Body must be a JSON object"; return false; }
        if (!root.TryGetProperty("lines", out var rawLines) || rawLines.ValueKind != JsonValueKind.Array) { error = "\"lines\" array is required"; return false; }
        if (rawLines.GetArrayLength() is < 1 or > 500) { error = "\"lines\" must contain 1..500 entries"; return false; }
        var lines = new List<PrintLine>();
        foreach (var item in rawLines.EnumerateArray())
        {
            string text; var align = TextAlign.Left; var bold = false; var underline = false; var size = 1;
            if (item.ValueKind == JsonValueKind.String) text = item.GetString()!;
            else if (item.ValueKind == JsonValueKind.Object)
            {
                if (!item.TryGetProperty("text", out var textElement) || textElement.ValueKind != JsonValueKind.String) { error = "Each line object requires string text"; return false; }
                text = textElement.GetString()!;
                if (item.TryGetProperty("align", out var rawAlign) && (rawAlign.ValueKind != JsonValueKind.String || !Enum.TryParse<TextAlign>(rawAlign.GetString(), true, out align))) { error = "\"align\" must be left, center or right"; return false; }
                if (!TryBool(item, "bold", out bold) || !TryBool(item, "underline", out underline) || !TryInt(item, "size", 1, out size)) { error = "Invalid line property"; return false; }
            }
            else { error = "Each line must be a string or object"; return false; }
            if (text.Length > 200 || size is < 1 or > 4) { error = text.Length > 200 ? "A line exceeds 200 characters" : "\"size\" must be 1..4"; return false; }
            lines.Add(new PrintLine(text, align, bold, underline, size));
        }
        if (!TryInt(root, "feed", 3, out var feed) || feed is < 0 or > 10 || !TryBool(root, "cut", out var cut) || !TryBool(root, "openDrawer", out var openDrawer) || !TryBool(root, "logo", out var logo)) { error = "Invalid print-job property"; return false; }
        var printer = openDrawer ? PrinterTarget.Register : PrinterTarget.Kitchen;
        if (root.TryGetProperty("printer", out var rawPrinter) && (rawPrinter.ValueKind != JsonValueKind.String || !Enum.TryParse<PrinterTarget>(rawPrinter.GetString(), true, out printer))) { error = "\"printer\" must be kitchen or register"; return false; }
        if ((openDrawer || logo) && printer != PrinterTarget.Register) { error = openDrawer ? "Only the register printer is connected to the cash drawer" : "The logo can only be printed on the register printer"; return false; }
        job = new PrintJob(lines, feed, cut, openDrawer, printer, logo); return true;
    }

    private static bool TryBool(JsonElement value, string name, out bool result) { result = false; return !value.TryGetProperty(name, out var property) || (property.ValueKind == JsonValueKind.True ? (result = true) : property.ValueKind == JsonValueKind.False); }
    private static bool TryInt(JsonElement value, string name, int fallback, out int result) { result = fallback; return !value.TryGetProperty(name, out var property) || (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out result)); }
}
