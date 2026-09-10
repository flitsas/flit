using Flit.Admin.Application.GeneracionDocumental.Ports;

namespace Flit.Admin.Application.GeneracionDocumental.Prefill;

/// <summary>Entrada del prellenado de una parte natural: tipo y número de documento.</summary>
public sealed record PrefillPersonaNaturalCommand(Guid TenantId, string? DocumentType, string? DocumentNumber);

/// <summary>
/// Prellenado de una parte PERSONA NATURAL por documento (CF-25, HU #12206), con precedencia
/// explícita:
///
/// <list type="number">
///   <item><b>Cadena RUNT persona</b> (kyverum_runt_conductor → verifik_conductor), <b>sin
///   instancia</b>: el guardado en caché va con instancia nula, que es lo que ese servicio ya
///   admite.</item>
///   <item><b>contact-lookup</b> (histórico de actores del tenant) si el RUNT no responde.</item>
/// </list>
///
/// <para>El domicilio nunca lo trae el RUNT. Cuando el RUNT sí resolvió la identidad, el
/// contact-lookup se consulta igual —best-effort— solo para completar la ciudad, y ese campo declara
/// su propia fuente. Que una consulta complementaria falle no cambia el resultado.</para>
///
/// <para>Sin coincidencia en ninguna fuente: 200 con <c>found = false</c> y sin campos. No 404, y no
/// 502 mientras alguna fuente haya contestado.</para>
/// </summary>
public sealed class PrefillPersonaNaturalHandler
{
    /// <summary>Tipos admitidos por las dos fuentes. NIT no: para eso está el endpoint de persona jurídica.</summary>
    private static readonly HashSet<string> TiposValidos =
        new(StringComparer.OrdinalIgnoreCase) { "CC", "CE", "PAS", "TI" };

    private readonly IStandaloneRuntPersonPrefill _runt;
    private readonly IStandaloneActorContactLookup _contactos;

    public PrefillPersonaNaturalHandler(
        IStandaloneRuntPersonPrefill runt,
        IStandaloneActorContactLookup contactos)
    {
        _runt = runt ?? throw new ArgumentNullException(nameof(runt));
        _contactos = contactos ?? throw new ArgumentNullException(nameof(contactos));
    }

    public async Task<PrefillResult> HandleAsync(
        PrefillPersonaNaturalCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tipo = command.DocumentType?.Trim().ToUpperInvariant();
        var numero = command.DocumentNumber?.Trim();

        if (string.IsNullOrEmpty(tipo) || string.IsNullOrEmpty(numero) || !TiposValidos.Contains(tipo))
        {
            return PrefillResult.Invalid();
        }

        var attempts = new List<PrefillSourceAttempt>(2);

        // 1) RUNT primero: la identidad se consulta en vivo, nunca se hereda del histórico.
        var runt = await _runt.LookupAsync(command.TenantId, tipo, numero, cancellationToken).ConfigureAwait(false);

        attempts.Add(new PrefillSourceAttempt(
            PrefillSources.Runt,
            runt.Error is not null ? PrefillOutcomes.Error : runt.Found ? PrefillOutcomes.Found : PrefillOutcomes.NotFound,
            runt.Error));

        // 2) contact-lookup. Se consulta en los dos caminos, pero por motivos distintos: como
        // RESPALDO de identidad cuando el RUNT no respondió, y como COMPLEMENTO del domicilio cuando
        // sí. El domicilio no existe en el RUNT, así que sin esto el campo quedaría siempre vacío.
        var contacto = await _contactos
            .LookupAsync(command.TenantId, tipo, numero, cancellationToken)
            .ConfigureAwait(false);

        attempts.Add(new PrefillSourceAttempt(
            PrefillSources.ContactLookup,
            contacto.Error is not null ? PrefillOutcomes.Error : contacto.Found ? PrefillOutcomes.Found : PrefillOutcomes.NotFound,
            contacto.Error));

        var fields = new List<PrefilledField>();

        if (runt.Found)
        {
            fields.Add(new PrefilledField("tipo_persona", "PN", PrefillSources.Runt));
            fields.Add(new PrefilledField("tipo_doc", tipo, PrefillSources.Runt));
            fields.Add(new PrefilledField("no_doc", numero, PrefillSources.Runt));
            Add(fields, "nombre_razon_social", runt.FullName, PrefillSources.Runt);
        }
        else if (contacto.Found)
        {
            // El contact-lookup NO trae nombre ni documento —por diseño de su contrato—, así que el
            // respaldo hidrata lo que sí conoce: la identificación tecleada y el domicilio. El nombre
            // queda para captura manual, no se inventa.
            fields.Add(new PrefilledField("tipo_persona", "PN", PrefillSources.ContactLookup));
            fields.Add(new PrefilledField("tipo_doc", tipo, PrefillSources.ContactLookup));
            fields.Add(new PrefilledField("no_doc", numero, PrefillSources.ContactLookup));
        }

        if (fields.Count > 0)
        {
            Add(fields, "domicilio", contacto.Ciudad, PrefillSources.ContactLookup);
        }

        if (fields.Count == 0)
        {
            // Ninguna fuente resolvió. Es fallo de proveedor solo si NINGUNA contestó; si una dijo
            // "no está", la respuesta es una degradación normal con 200.
            var algunaContesto = attempts.Any(a => a.Outcome != PrefillOutcomes.Error);
            return algunaContesto ? PrefillResult.Empty(attempts) : PrefillResult.Unavailable(attempts);
        }

        return new PrefillResult(
            true,
            runt.Found ? PrefillSources.Runt : PrefillSources.ContactLookup,
            fields,
            attempts,
            null);
    }

    private static void Add(List<PrefilledField> fields, string key, string? value, string source)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields.Add(new PrefilledField(key, value.Trim(), source));
        }
    }
}
