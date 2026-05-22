using System.Text;

namespace ZeloImpressao;

internal static class EscPosBuilder
{
    private const byte Esc = 0x1b;
    private const byte Gs = 0x1d;

    public static byte[] BuildTestReceipt()
    {
        var bytes = new List<byte>();
        Raw(bytes, Esc, 0x40);
        Center(bytes);
        Bold(bytes, true);
        Raw(bytes, Gs, 0x21, 0x11);
        Line(bytes, "ZELO IMPRESSAO");
        Raw(bytes, Gs, 0x21, 0x00);
        Bold(bytes, false);
        Line(bytes, "Teste de impressao");
        Left(bytes);
        Line(bytes, "--------------------------------");
        Line(bytes, "Se voce esta lendo isso,");
        Line(bytes, "a impressora esta configurada.");
        Line(bytes);
        Line(bytes, $"Data: {AppClock.SafeNow:dd/MM/yyyy HH:mm}");
        Line(bytes, "--------------------------------");
        Center(bytes);
        Line(bytes, "Zelo");
        Raw(bytes, Esc, 0x64, 0x03);
        Raw(bytes, Gs, 0x56, 0x42, 0x00);
        return bytes.ToArray();
    }

    private static void Raw(List<byte> bytes, params byte[] values) => bytes.AddRange(values);
    private static void Center(List<byte> bytes) => Raw(bytes, Esc, 0x61, 0x01);
    private static void Left(List<byte> bytes) => Raw(bytes, Esc, 0x61, 0x00);
    private static void Bold(List<byte> bytes, bool enabled) => Raw(bytes, Esc, 0x45, enabled ? (byte)1 : (byte)0);

    private static void Line(List<byte> bytes, string value = "")
    {
        var clean = RemoveDiacritics(value);
        bytes.AddRange(Encoding.ASCII.GetBytes(clean));
        bytes.Add(0x0a);
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var chars = normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray()).Normalize(NormalizationForm.FormC)
            .Replace("ç", "c")
            .Replace("Ç", "C");
    }
}
