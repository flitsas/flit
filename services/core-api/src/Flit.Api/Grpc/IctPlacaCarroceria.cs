using System.Globalization;
using Flit.Ict.Grpc.Contracts;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Tramites.Catalog;

namespace Flit.Api.Grpc;

/// <summary>
/// Reglas puras de la materialización ICT para la PLACA de la matrícula (<c>plate_assignment_type</c>) y la
/// CARROCERÍA de la transformación 17. Sin I/O: el servicio gRPC las orquesta.
/// <para><b>Placa.</b> Tipo 2 = dígito de preferencia (NO es una placa), 3 = placa completa entregada,
/// 4 = placa de un rango que el OT asignó a la compañía. Los tipos 3 y 4 persisten <c>plate</c> con
/// <c>source = "ict"</c> para que el wizard la trate como placa entregada (no pide dígito); el tipo 2 jamás
/// escribe <c>plate</c> y deja <c>plate_preferred_last_digit</c>.</para>
/// <para><b>Carrocería.</b> El texto que manda ICT solo vale si coincide EXACTO (salvo mayúsculas y espacios
/// de más) con el catálogo de carrocerías de la clase del vehículo; si no, el gestor la elige.</para>
/// </summary>
internal static class IctPlacaCarroceria
{
    internal const string TipoAsignacionKey = "plate_assignment_type";
    internal const string DigitoPreferidoKey = "plate_preferred_last_digit";

    /// <summary><c>source</c> del field_value <c>plate</c> entregado por la integración ICT.</summary>
    internal const string SourceIct = "ict";

    internal const int TipoDigitoPreferencia = 2;
    internal const int TipoPlacaCompleta = 3;
    internal const int TipoPlacaDeRango = 4;

    /// <summary>La placa (tipo 4) no existe en el rango del OT para la compañía o no está disponible.</summary>
    internal const string ErrorPlacaFueraDeRango = "placa_fuera_de_rango";

    /// <summary>Tipo 4 sin OT conocido al crear el borrador: no hay rango contra el cual validar.</summary>
    internal const string ErrorPlacaRangoSinOt = "placa_rango_sin_ot";

    /// <summary>Aviso (el borrador SÍ se crea): la carrocería enviada no está en el catálogo de la clase.</summary>
    internal const string WarningCarroceriaFueraDeCatalogo = "carroceria_warning:fuera_de_catalogo";

    /// <summary>
    /// Aviso (el borrador SÍ se crea): el RUNT no reporta carrocería, el cambio de carrocería no aplica y no se
    /// activó (<c>cambio_carroceria="false"</c>).
    /// </summary>
    internal const string WarningVehiculoSinCarroceria = "carroceria_warning:vehiculo_sin_carroceria";

    /// <summary>Tipo de asignación de placa que declara core-ict (2/3/4), o <c>null</c> si no vino o es inválido.</summary>
    internal static int? TipoPlaca(CreateDraftFromIctRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var texto = ValorDe(request, TipoAsignacionKey);
        return int.TryParse(texto?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var tipo)
               && tipo is TipoDigitoPreferencia or TipoPlacaCompleta or TipoPlacaDeRango
            ? tipo
            : null;
    }

    /// <summary>Placa entregada (normalizada: trim + mayúsculas) para los tipos 3 y 4; <c>null</c> en los demás.</summary>
    internal static string? PlacaEntregada(CreateDraftFromIctRequest request)
    {
        if (TipoPlaca(request) is not (TipoPlacaCompleta or TipoPlacaDeRango))
        {
            return null;
        }

        var placa = ValorDe(request, "plate");
        return string.IsNullOrWhiteSpace(placa) ? null : placa.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Placa con la que se identifica el vehículo en el borrador (consulta RUNT precalculada). En el tipo 2
    /// el valor de <c>plate</c> es un dígito, no una placa: se ignora aunque un core-ict anterior lo mande.
    /// </summary>
    internal static string? PlacaDelVehiculo(CreateDraftFromIctRequest request) =>
        TipoPlaca(request) == TipoDigitoPreferencia ? null : ValorDe(request, "plate");

    /// <summary>
    /// Ajusta los field_values a sembrar según el tipo: 3/4 → <c>plate</c> normalizada con origen
    /// <see cref="SourceIct"/>; 2 → se retira <c>plate</c> y, si traía un dígito y no vino
    /// <c>plate_preferred_last_digit</c>, se convierte en este. Otros tipos: sin cambios.
    /// </summary>
    internal static void AjustarPlaca(List<FieldValueInput> items, int? tipo)
    {
        ArgumentNullException.ThrowIfNull(items);
        var indice = items.FindIndex(i => string.Equals(i.FieldKey, "plate", StringComparison.OrdinalIgnoreCase));
        if (indice < 0)
        {
            return;
        }

        switch (tipo)
        {
            case TipoPlacaCompleta or TipoPlacaDeRango:
                items[indice] = items[indice] with
                {
                    ValueText = items[indice].ValueText?.Trim().ToUpperInvariant(),
                    Source = SourceIct,
                };
                break;

            case TipoDigitoPreferencia:
                var valor = items[indice].ValueText?.Trim();
                items.RemoveAt(indice);
                var yaTieneDigito = items.Exists(i =>
                    string.Equals(i.FieldKey, DigitoPreferidoKey, StringComparison.OrdinalIgnoreCase));
                if (!yaTieneDigito && valor is { Length: 1 } && char.IsAsciiDigit(valor[0]))
                {
                    items.Add(new FieldValueInput(null, DigitoPreferidoKey, valor, null));
                }

                break;
        }
    }

    /// <summary>Normaliza para comparar: trim, espacios internos colapsados y mayúsculas (sin fuzzy ni contains).</summary>
    internal static string NormalizarTexto(string? valor) =>
        string.Join(' ', (valor ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();

    /// <summary>
    /// Carrocería del catálogo cuyo nombre (o código) coincide EXACTO con la enviada, salvo mayúsculas y
    /// espacios de más; <c>null</c> si no hay coincidencia o la enviada está vacía.
    /// </summary>
    internal static VehicleBodyworkEntry? CoincidenciaCarroceria(
        string? enviada, IReadOnlyList<VehicleBodyworkEntry> catalogo)
    {
        ArgumentNullException.ThrowIfNull(catalogo);
        var buscada = NormalizarTexto(enviada);
        return buscada.Length == 0
            ? null
            : catalogo.FirstOrDefault(c => NormalizarTexto(c.Name) == buscada);
    }

    private static string? ValorDe(CreateDraftFromIctRequest request, string key) =>
        request.FieldValues
            .FirstOrDefault(f => string.Equals(f.FieldKey, key, StringComparison.OrdinalIgnoreCase)
                                 && !string.IsNullOrWhiteSpace(f.ValueText))
            ?.ValueText;
}
