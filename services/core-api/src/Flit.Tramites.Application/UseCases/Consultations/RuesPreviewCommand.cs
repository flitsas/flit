namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Consulta RUES por NIT SIN trámite creado (HU sin ADO, backend-agent 2026-08-11): segunda tanda
/// del trabajo de la casilla 19 "EMPRESA VINCULADORA" del FUR. El operador necesita saber la razón
/// social de la empresa vinculadora en el PASO 1 del wizard de matrícula inicial (tipo de servicio
/// PÚBLICO), antes de que exista ninguna instancia — es el análogo, para RUES, de
/// <see cref="ProcedureInstances.RunPreflightPreviewHandler"/> para el vehículo.
///
/// <para><b>NO persiste NADA</b>: ni <c>field_values</c> (no hay <c>procedure_instance_id</c> al que
/// colgarlos) ni el almacén canónico de certificaciones. ADR-0041 exige
/// <c>procedure_instance_id NOT NULL</c> para toda fila de <c>company_registrations</c> —condición
/// que aquí es imposible de cumplir porque el trámite todavía no existe—, así que este handler ni
/// siquiera lo intenta: es una consulta efímera, de usar y tirar. La ingesta canónica ocurre después,
/// por la vía normal, cuando el trámite ya existe: al crear el trámite
/// (<see cref="ProcedureInstances.CreateProcedureInstanceFromConsultaHandler"/>, que persiste
/// <c>empresa_vinculadora_nit</c>/<c>empresa_vinculadora_razon_social</c> en <c>field_values</c> si el
/// operador ya trae esos datos) o si más adelante alguien vuelve a consultar el RUES con instancia
/// (<see cref="RuesPersonLookupHandler"/>, que sí persiste field_values y certificaciones).</para>
///
/// <para>Reusa <see cref="RuesActorJuridicalLookup"/> para resolver el proveedor y consultar — la
/// misma lógica que ya usa <see cref="RuesPersonLookupHandler"/> — para no duplicar la resolución del
/// provider ni la plantilla <c>RUES_ACTOR_JURIDICAL</c>.</para>
/// </summary>
public sealed record RuesPreviewResult(bool Found, string Nit, string? RazonSocial);

public sealed class RuesPreviewHandler(IConsultationProviderRegistry registry)
{
    public async Task<(RuesPreviewResult? Result, string? Error)> HandleAsync(
        string? documentNumber,
        Guid tenantId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentNumber))
            return (null, "invalid_request");

        var nit = documentNumber.Trim();

        // instanceId = Guid.Empty: mismo convenio "sin trámite" que RunPreflightPreviewHandler.
        var (result, error) = await RuesActorJuridicalLookup.ConsultAsync(
            registry, Guid.Empty, tenantId, nit, ct);
        if (error is not null)
            return (null, error);

        // "No existe ese NIT" y "el proveedor no respondió" producen AMBOS cero campos hidratados:
        // mirar solo la razón social los confundía y le decía al operador que su NIT no existe cuando
        // en realidad el servicio estaba caído o mal configurado. El proveedor SÍ distingue los dos
        // casos en sus checks (`NotFound` → status "unknown"; `ProviderUnavailable` → status "error"),
        // así que la señal ya estaba ahí y solo hacía falta leerla.
        if (TieneFalloDeProveedor(result!))
            return (null, "provider_unavailable");

        var razonSocial = RuesActorJuridicalLookup.GetHydrated(result!.HydratedFields, "rues_razon_social");
        var found = !string.IsNullOrWhiteSpace(razonSocial);

        return (new RuesPreviewResult(found, nit, found ? razonSocial : null), null);
    }

    /// <summary>
    /// ¿La consulta falló por el lado del proveedor (no-200, timeout, red, respuesta ilegible) en vez
    /// de por no existir la empresa? Es la diferencia entre pedirle al operador que corrija el NIT y
    /// pedirle que reintente en unos minutos.
    /// </summary>
    private static bool TieneFalloDeProveedor(ConsultationResult result)
    {
        foreach (var check in result.Checks)
        {
            if (string.Equals(check.Status, "error", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
