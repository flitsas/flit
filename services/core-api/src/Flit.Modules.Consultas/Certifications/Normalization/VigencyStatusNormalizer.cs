namespace Flit.Tramites.Domain.Certifications.Normalization;

/// <summary>
/// Traduce el estado crudo de un proveedor al vocabulario cerrado <see cref="VigencyStatus"/>.
/// </summary>
/// <remarks>
/// Hay dos métodos y no uno porque son dos afirmaciones distintas: un SOAT dice si el vehículo está
/// cubierto; el RUES dice si una sociedad sigue matriculada. Compartir el diccionario haría que
/// <c>ACTIVA</c> —que en el RUES significa sociedad viva— pudiera colarse como cobertura vigente de
/// una póliza. Se separan a propósito.
///
/// <para><b>Nada cae en <see cref="VigencyStatus.Vencido"/> por descarte.</b> Un valor no reconocido
/// va a <see cref="VigencyStatus.Unknown"/> conservando el crudo: el documento imprime lo que dijo la
/// fuente y ningún gate afirma un estado que nadie declaró.</para>
/// </remarks>
public static class VigencyStatusNormalizer
{
    /// <summary>
    /// Vigencia de una certificación de vehículo (SOAT / RTM).
    /// <para><c>APROBADA</c> queda en <see cref="VigencyStatus.Unknown"/> deliberadamente: es el
    /// resultado del trámite de la revisión, no su vigencia. Ver <see cref="VigencyStatus"/>.</para>
    /// </summary>
    public static CertifiedStatus ForVehicle(string? raw)
    {
        var key = Canonicalize(raw);
        if (key is null)
            return CertifiedStatus.Empty;

        var value = key switch
        {
            "VIGENTE" or "SI" or "S" or "TRUE" or "ACTIVO" => VigencyStatus.Vigente,
            "VENCIDO" or "NO VIGENTE" or "NO" or "N" or "FALSE" or "EXPIRADO" => VigencyStatus.Vencido,
            "NO APLICA" or "NA" => VigencyStatus.NoAplica,
            _ => VigencyStatus.Unknown,
        };

        return new CertifiedStatus(value, raw);
    }

    /// <summary>
    /// Estado del registro mercantil (RUES). D5: se guarda el crudo y se deriva el canónico; un
    /// estado no visto no rompe nada, se imprime tal cual y no bloquea.
    /// </summary>
    public static CertifiedStatus ForMerchantRegistration(string? raw)
    {
        var key = Canonicalize(raw);
        if (key is null)
            return CertifiedStatus.Empty;

        var value = key switch
        {
            "ACTIVA" or "ACTIVO" or "VIGENTE" or "MATRICULADO" or "MATRICULADA" or "INSCRITA"
                => VigencyStatus.Vigente,
            "CANCELADA" or "CANCELADO" or "LIQUIDADA" or "LIQUIDADO" or "INACTIVA" or "INACTIVO"
                or "NO VIGENTE" or "VENCIDO"
                => VigencyStatus.Vencido,
            _ => VigencyStatus.Unknown,
        };

        return new CertifiedStatus(value, raw);
    }

    /// <summary>Mayúsculas, sin acentos, guiones bajos a espacio y espacios colapsados.</summary>
    private static string? Canonicalize(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;

        Span<char> buffer = stackalloc char[text.Length];
        var length = 0;
        var lastWasSpace = false;

        foreach (var ch in text.ToUpperInvariant())
        {
            var c = ch switch
            {
                'Á' => 'A', 'É' => 'E', 'Í' => 'I', 'Ó' => 'O', 'Ú' => 'U', 'Ü' => 'U',
                '_' or '-' => ' ',
                _ => ch,
            };

            if (c == ' ')
            {
                if (lastWasSpace || length == 0)
                    continue;
                lastWasSpace = true;
            }
            else
            {
                lastWasSpace = false;
            }

            buffer[length++] = c;
        }

        while (length > 0 && buffer[length - 1] == ' ')
            length--;

        return length == 0 ? null : new string(buffer[..length]);
    }
}
