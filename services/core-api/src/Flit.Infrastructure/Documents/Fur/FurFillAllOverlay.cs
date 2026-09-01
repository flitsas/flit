namespace Flit.Infrastructure.Documents.Fur;

/// <summary>
/// Valores de calibración: un entry por cada campo del manifiesto. No es diligenciamiento normativo
/// (no pasa por <see cref="FurFieldMapper"/>). Solo el preview SuperAdmin «llenado completo».
/// </summary>
public static class FurFillAllOverlay
{
    public const string DummyText = "MMMMMMMMMMMM";
    public const string DummyLongName = "MMMMMMMMMMMM MMMMMMMMMMMM MMMMMM";
    public const string DummyMultiline =
        "CALIBRACION FUR CAMPO MULTILINEA MMMMMMMMMM XXXXXXXXXX OBSERVACIONES LARGAS PARA VER MARGEN Y FUENTE.";

    /// <summary>
    /// PNG RGBA de calibración (trazo semi-opaco sobre fondo transparente). El 1×1 opaco anterior
    /// se estiraba como un rectángulo sólido y tapaba la grilla del FUR.
    /// </summary>
    private static readonly byte[] SignaturePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAKAAAAAwCAYAAACWqXFuAAABH0lEQVR42u3XTY6CQBRGURfCwKW7EsaMXZEOjCEGEaSK+nnnJDXTTixuf91eLgAAAAAAAAAAABDPMFzvzzPOj1vhrPDuSwEKMt+FT/MjvG0BijHdhU9RI/wS3vu8XjPuPQrbd9nT2oka3sp7xZjqoj9+07uP8Eh4KWIMEeTW6JYu/Ncathpj6vCs48G12/jzmo/wrPDCrmPq6HpZxJLhhVjHnNG1uogl7iTUOuZeu1YXsaXwmlvHGqKrdRF7CK/KdTzyLbamCHPF2GN0qYLsdu1qWMRo4f0TY/g/JTkWUXjbg0waYAeXcyhC4RX6otHZZ9u9iMKjyCIKj1IRCo+iMQqv8Qd5S31OjlB4Aqz3eMICFDNiFiD+twUAAAAAqNQD3jlU/EmsrdwAAAAASUVORK5CYII=");

    public static IReadOnlyDictionary<string, FurFieldValue> FromManifest(FurFieldManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var dict = new Dictionary<string, FurFieldValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in manifest.Fields)
            dict[field.Id] = ValueFor(field);

        return dict;
    }

    private static FurFieldValue ValueFor(FurFieldDefinition field)
    {
        if (field.Type == FurFieldType.Checkbox)
            return new FurFieldValue("X");

        if (field.Type == FurFieldType.Image
            || field.Id.Contains("signature", StringComparison.OrdinalIgnoreCase))
        {
            return new FurFieldValue(
                null,
                SignaturePng,
                "CC 00000000\nNOMBRE CALIBRACION\nVIG 01/07/2026-31/08/2026\nHASH FILLALL01");
        }

        if (field.Type == FurFieldType.Multiline)
            return new FurFieldValue(DummyMultiline);

        return new FurFieldValue(field.Id switch
        {
            "processing_day" or "processing_month" => "08",
            "processing_year" => "2026",
            "plate_letter" => "ABC",
            "plate_number" => "123",
            "vehicle_owner_name" or "vehicle_buyer_name" => DummyLongName,
            _ => DummyText,
        });
    }
}
