namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Documento del SUJETO de identidad de una parte (HU #10688: el representante legal en persona
/// jurídica, el actor en persona natural). Lo usa el emparejamiento del listado para no atribuirle a
/// una parte intentos que son de OTRA persona.
/// </summary>
/// <param name="TipoDocumento">Tipo de documento del sujeto (puede venir vacío en datos antiguos).</param>
/// <param name="NumeroDocumento">Número de documento del sujeto.</param>
internal readonly record struct ParteDocumento(string? TipoDocumento, string? NumeroDocumento);

/// <summary>
/// Bug #11615 — PREVALENCIA de la identidad vigente en el listado de biométricas de un trámite
/// (<see cref="ListBiometriaHandler"/>).
///
/// <para>El listado alimenta a TODOS los consumidores de la ruta
/// <c>GET /api/v1/tramites/instances/{id}/biometric</c>: el paso de Identidad del asistente, el panel
/// consolidado de identidad, la pestaña de identidad del expediente y el resumen del paso FUR. Cada
/// consumidor elige "la validación de la parte" con una heurística POSICIONAL propia: unos toman la
/// PRIMERA coincidencia del rol y otros la ÚLTIMA. Mientras el trámite tuviera un intento local
/// (rechazado / expirado / en proceso) además de la identidad vigente REFERENCIADA de otro trámite
/// (HU #10350), las dos heurísticas devolvían entradas distintas: el paso de Identidad mostraba la
/// aprobada y el resumen mostraba el intento fallido, pedía validar otra vez y el botón respondía
/// "ya la tienes activa" — ciclo infinito, trámite imposible de terminar.</para>
///
/// <para><b>Regla:</b> cuando una parte TIENE una identidad aprobada y vigente (propia o referenciada),
/// esa entrada es la única que representa el estado de la parte en <c>validations</c>: va de PRIMERA y
/// los intentos que NO están aprobados+vigentes de esa misma parte salen de la lista hacia
/// <c>supersededValidations</c> (siguen viajando en la respuesta: no se pierde el histórico, solo deja
/// de suplantar al estado vigente). Así la primera y la última coincidencia del rol son la misma
/// entrada y todos los consumidores coinciden, sin depender de la heurística de cada uno.</para>
///
/// <para><b>Sin identidad aprobada y vigente no se toca nada:</b> la lista queda tal cual (todos los
/// intentos, por fecha de creación), de modo que la parte se sigue viendo pendiente/rechazada y el
/// sistema sigue pidiendo validación. Cero falsos positivos.</para>
/// </summary>
internal static class BiometricListPrevalence
{
    /// <summary>Rol "comprador" tal como lo rotula el listado (los DTO ya vienen normalizados).</summary>
    private const string Comprador = "comprador";

    /// <summary>
    /// Reordena/segrega el listado aplicando la prevalencia de la identidad vigente.
    /// </summary>
    /// <param name="dtos">Listado ya compuesto (filas propias + identidad referenciada).</param>
    /// <param name="prevalecientePorParte">
    /// Id de la validación APROBADA Y VIGENTE que representa a cada CLAVE (propia o referenciada). Una
    /// clave sin identidad vigente simplemente no aparece en el mapa.
    /// <para>ADR-0053 (Múltiple Propietario) — la clave es el rol puro cuando ese rol tiene UN SOLO
    /// actor (histórico, comportamiento idéntico al anterior), o <c>"{rol}#{ordinal}"</c> cuando el rol
    /// tiene 2+ actores, para no mezclar la prevalencia de un copropietario con la de otro.</para>
    /// </param>
    /// <param name="vigentesPorParte">
    /// Ids que, además del prevaleciente, están aprobados y vigentes para esa misma clave: se conservan
    /// en la lista (no contradicen el estado) pero después del prevaleciente.
    /// </param>
    /// <param name="documentoPorParte">
    /// Documento del sujeto de identidad de cada clave. Es lo que evita relegar el intento de OTRA
    /// persona: en matrícula la parte "comprador" recoge también las filas históricas sin rol, y sin
    /// comparar documento un rechazo ajeno salía de <c>validations</c> y quedaba oculto para el gestor.
    /// Con clave compuesta (rol con 2+ actores) también discrimina entre copropietarios del mismo rol.
    /// </param>
    /// <param name="esTraspaso">
    /// Determina el emparejamiento rol↔entrada igual que los consumidores: en traspaso el rol debe
    /// coincidir exactamente; en matrícula la parte "comprador" también recoge las filas sin rol.
    /// </param>
    public static (IReadOnlyList<BiometricValidationDto> Validations,
                   IReadOnlyList<BiometricValidationDto> Superseded) Apply(
        IReadOnlyList<BiometricValidationDto> dtos,
        IReadOnlyDictionary<string, Guid> prevalecientePorParte,
        IReadOnlyDictionary<string, IReadOnlySet<Guid>> vigentesPorParte,
        IReadOnlyDictionary<string, ParteDocumento> documentoPorParte,
        bool esTraspaso)
    {
        ArgumentNullException.ThrowIfNull(dtos);
        ArgumentNullException.ThrowIfNull(prevalecientePorParte);
        ArgumentNullException.ThrowIfNull(vigentesPorParte);
        ArgumentNullException.ThrowIfNull(documentoPorParte);

        if (prevalecientePorParte.Count == 0)
            return (dtos, []);

        var prevalecientes = new HashSet<Guid>(prevalecientePorParte.Values);
        var relegadosIds = new HashSet<Guid>();
        var relegados = new List<BiometricValidationDto>();

        foreach (var (clave, prevalecienteId) in prevalecientePorParte)
        {
            var vigentes = vigentesPorParte.TryGetValue(clave, out var v) ? v : (IReadOnlySet<Guid>)new HashSet<Guid>();
            var documento = documentoPorParte.TryGetValue(clave, out var d) ? d : default;
            foreach (var dto in dtos)
            {
                if (!Pertenece(dto, clave, documento, esTraspaso))
                    continue;
                if (dto.Id == prevalecienteId || vigentes.Contains(dto.Id))
                    continue;
                if (relegadosIds.Add(dto.Id))
                    relegados.Add(dto);
            }
        }

        // Sin nada que relegar no hay nada que arreglar: las entradas que quedan de la parte están todas
        // aprobadas y vigentes, así que la primera y la última coincidencia del rol dicen lo mismo y
        // reordenar solo movería filas sin cambiar lo que ve ningún consumidor.
        //
        // (La condición original añadía `&& prevalecientes.Count == 0`, que es INALCANZABLE: el retorno
        // temprano de arriba garantiza al menos un prevaleciente, así que el listado se reordenaba
        // siempre, incluso cuando no había un solo intento que relegar.)
        if (relegadosIds.Count == 0)
            return (dtos, []);

        // OrderBy de LINQ-to-objects es ESTABLE: el resto del listado conserva su orden por fecha.
        var validations = dtos
            .Where(d => !relegadosIds.Contains(d.Id))
            .OrderBy(d => prevalecientes.Contains(d.Id) ? 0 : 1)
            .ToList();

        return (validations, relegados);
    }

    /// <summary>
    /// ¿La entrada representa a esta CLAVE? Réplica del emparejamiento que hacen los consumidores del
    /// listado: en traspaso por rol; en matrícula, "comprador" recoge también las filas históricas sin
    /// rol (<c>partyRole = null</c>).
    ///
    /// <para>Las filas SIN ROL solo se atribuyen a la parte si además coinciden en documento con su
    /// sujeto de identidad. Sin esa comparación, un intento rechazado de OTRA persona rotulado sin rol
    /// salía de <c>validations</c> hacia <c>supersededValidations</c> y el gestor dejaba de ver ese
    /// rechazo en la vista principal. Si no se conoce el documento de la parte, la fila sin rol se deja
    /// donde está (no relegar es siempre la opción conservadora: no oculta información).</para>
    ///
    /// <para><b>ADR-0053 (Múltiple Propietario)</b> — <paramref name="clave"/> puede venir en dos
    /// formas: el ROL puro (rol con un solo actor, forma histórica — comportamiento idéntico al
    /// anterior) o <c>"{rol}#{ordinal}"</c> (rol con 2+ actores). En la forma compuesta, el rol solo YA
    /// NO basta para decidir pertenencia (dos copropietarios comparten <c>PartyRole</c>): se exige
    /// ADEMÁS el documento del sujeto de ESE actor — sin este refinamiento, los intentos rechazados de
    /// un copropietario se mezclaban con la prevalencia del otro.</para>
    /// </summary>
    private static bool Pertenece(
        BiometricValidationDto dto, string clave, ParteDocumento documento, bool esTraspaso)
    {
        var separador = clave.IndexOf('#', StringComparison.Ordinal);
        var multiActor = separador >= 0;
        var parte = multiActor ? clave[..separador] : clave;

        if (string.Equals(dto.PartyRole, parte, StringComparison.OrdinalIgnoreCase))
            return !multiActor || DocumentoCoincide(dto, documento);

        if (esTraspaso || dto.PartyRole is not null)
            return false;

        return string.Equals(parte, Comprador, StringComparison.OrdinalIgnoreCase)
            && DocumentoCoincide(dto, documento);
    }

    /// <summary>
    /// Comparación de documento entre una entrada del listado y el sujeto de una parte. Misma semántica
    /// que <c>BiometricRules.DocumentoCoincide</c> sobre la entidad: lo desconocido no descarta, pero
    /// dos documentos conocidos y distintos sí.
    /// </summary>
    private static bool DocumentoCoincide(BiometricValidationDto dto, ParteDocumento documento)
    {
        if (string.IsNullOrWhiteSpace(documento.NumeroDocumento) || string.IsNullOrWhiteSpace(dto.DocumentNumber))
            return false;

        if (!string.Equals(dto.DocumentNumber.Trim(), documento.NumeroDocumento.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(documento.TipoDocumento) || string.IsNullOrWhiteSpace(dto.DocumentType))
            return true;

        return string.Equals(dto.DocumentType.Trim(), documento.TipoDocumento.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
