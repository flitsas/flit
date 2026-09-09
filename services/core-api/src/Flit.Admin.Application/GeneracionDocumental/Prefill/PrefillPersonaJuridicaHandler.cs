using System.Globalization;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.Common;
using Flit.Admin.Domain.Companies.LegalRepresentatives;

namespace Flit.Admin.Application.GeneracionDocumental.Prefill;

/// <summary>Entrada del prellenado de una parte jurídica: solo el NIT.</summary>
public sealed record PrefillPersonaJuridicaCommand(Guid TenantId, string? Nit);

/// <summary>
/// Prellenado de una parte PERSONA JURÍDICA por NIT (CF-25, HU #12206), con precedencia explícita
/// y probada en backend:
///
/// <list type="number">
///   <item><b>Directorio de representantes legales del tenant</b> — dato propio de la compañía, con
///   el nombre y la cédula del representante, que el RUES no entrega.</item>
///   <item><b>RUES</b>, solo si el directorio no responde: cubre razón social, domicilio y matrícula
///   mercantil, pero no el representante.</item>
/// </list>
///
/// <para>«No responde» incluye las dos formas de no responder: que el NIT no esté en el directorio y
/// que la lectura del directorio falle. En ambos casos se cae al respaldo; lo que nunca ocurre es
/// consultar el RUES teniendo el dato en casa.</para>
///
/// <para>El <b>dígito de verificación se calcula</b> con <see cref="NitVerificationDigit"/> y viaja
/// con fuente <c>CALCULADO</c>: no se consulta a ningún proveedor y no lo teclea el usuario.</para>
///
/// <para>No recibe repositorio ni storage de documentos: el prellenado no persiste.</para>
/// </summary>
public sealed class PrefillPersonaJuridicaHandler
{
    private readonly ILegalRepresentativeReader _directorio;
    private readonly IStandaloneRuesCompanyLookup _rues;

    public PrefillPersonaJuridicaHandler(
        ILegalRepresentativeReader directorio,
        IStandaloneRuesCompanyLookup rues)
    {
        _directorio = directorio ?? throw new ArgumentNullException(nameof(directorio));
        _rues = rues ?? throw new ArgumentNullException(nameof(rues));
    }

    public async Task<PrefillResult> HandleAsync(
        PrefillPersonaJuridicaCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var nit = Sanitize(command.Nit);
        if (nit is null)
        {
            return PrefillResult.Invalid();
        }

        var attempts = new List<PrefillSourceAttempt>(2);

        // 1) Directorio del tenant. Va primero: es el dato que la propia compañía capturó.
        LegalRepresentativeItem? representante = null;
        try
        {
            representante = await _directorio
                .FindActiveByCompanyNitAsync(command.TenantId, nit, cancellationToken)
                .ConfigureAwait(false);

            attempts.Add(new PrefillSourceAttempt(
                PrefillSources.Directorio,
                representante is null ? PrefillOutcomes.NotFound : PrefillOutcomes.Found));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            // El directorio caído no puede tumbar el prellenado: se registra el intento y se sigue
            // con el respaldo. Sin mensaje ni NIT en la traza (PII, Ley 1581).
            attempts.Add(new PrefillSourceAttempt(
                PrefillSources.Directorio, PrefillOutcomes.Error, "directory_unavailable"));
        }

        if (representante is not null)
        {
            return Build(nit, PrefillSources.Directorio, attempts, BuildDirectorioFields(nit, representante));
        }

        // 2) RUES como respaldo, y solo aquí.
        var rues = await _rues.ConsultAsync(command.TenantId, nit, cancellationToken).ConfigureAwait(false);

        if (rues.Error is not null)
        {
            attempts.Add(new PrefillSourceAttempt(PrefillSources.Rues, PrefillOutcomes.Error, rues.Error));

            // Todas las fuentes cayeron: solo entonces es un fallo de proveedor. Si el directorio
            // había contestado "no está", esto sigue siendo una degradación normal.
            var directorioContesto = attempts.Any(a =>
                a.Source == PrefillSources.Directorio && a.Outcome == PrefillOutcomes.NotFound);

            return directorioContesto
                ? PrefillResult.Empty(attempts)
                : PrefillResult.Unavailable(attempts);
        }

        if (!rues.Found)
        {
            attempts.Add(new PrefillSourceAttempt(PrefillSources.Rues, PrefillOutcomes.NotFound));
            return PrefillResult.Empty(attempts);
        }

        attempts.Add(new PrefillSourceAttempt(PrefillSources.Rues, PrefillOutcomes.Found));
        return Build(nit, PrefillSources.Rues, attempts, BuildRuesFields(nit, rues.Fields));
    }

    /// <summary>Campos §5.2/§5.3 del anexo que sí trae el directorio (incluido el representante).</summary>
    private static List<PrefilledField> BuildDirectorioFields(string nit, LegalRepresentativeItem item)
    {
        var compania = item.Companies.FirstOrDefault(c =>
            string.Equals(c.Nit, nit, StringComparison.Ordinal));

        var fields = new List<PrefilledField>
        {
            new("tipo_persona", "PJ", PrefillSources.Directorio),
            new("tipo_doc", "NIT", PrefillSources.Directorio),
            new("no_doc", nit, PrefillSources.Directorio),
        };

        Add(fields, "razon_social", compania?.Name ?? item.CompanyName, PrefillSources.Directorio);
        Add(fields, "domicilio", compania?.City ?? item.City, PrefillSources.Directorio);
        Add(fields, "representante_legal", NombreCompleto(item), PrefillSources.Directorio);
        Add(fields, "cc_rl", item.DocumentNumber, PrefillSources.Directorio);
        Add(fields, "tipo_doc_rl", item.DocumentType, PrefillSources.Directorio);

        return fields;
    }

    /// <summary>
    /// Campos que trae el RUES. Deliberadamente NO incluye representante legal ni su cédula: el RUES
    /// devuelve la facultad de representación, no la persona, y rellenar esos campos con otra cosa
    /// sería inventar el dato.
    /// </summary>
    private static List<PrefilledField> BuildRuesFields(string nit, IReadOnlyDictionary<string, string?> campos)
    {
        var fields = new List<PrefilledField>
        {
            new("tipo_persona", "PJ", PrefillSources.Rues),
            new("tipo_doc", "NIT", PrefillSources.Rues),
            new("no_doc", nit, PrefillSources.Rues),
        };

        Add(fields, "razon_social", Get(campos, "rues_razon_social"), PrefillSources.Rues);
        Add(fields, "domicilio", Get(campos, "rues_municipio"), PrefillSources.Rues);
        Add(fields, "matricula_mercantil", Get(campos, "rues_matricula_mercantil"), PrefillSources.Rues);

        return fields;
    }

    /// <summary>
    /// Cierra la respuesta agregando el DV. Va aparte del resto porque su fuente NO es la que
    /// resolvió la identidad: sale de un cálculo local, y la respuesta lo declara así.
    /// </summary>
    private static PrefillResult Build(
        string nit,
        string source,
        IReadOnlyList<PrefillSourceAttempt> attempts,
        List<PrefilledField> fields)
    {
        if (NitVerificationDigit.TryCompute(nit, out var dv))
        {
            fields.Add(new PrefilledField(
                "digito_verificacion",
                dv.ToString(CultureInfo.InvariantCulture),
                PrefillSources.Calculado));
        }

        return new PrefillResult(true, source, fields, attempts, null);
    }

    private static string NombreCompleto(LegalRepresentativeItem item) => string.Join(
        ' ',
        new[] { item.Name, item.FirstLastName, item.SecondLastName }
            .Where(p => !string.IsNullOrWhiteSpace(p)));

    private static void Add(List<PrefilledField> fields, string key, string? value, string source)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields.Add(new PrefilledField(key, value.Trim(), source));
        }
    }

    private static string? Get(IReadOnlyDictionary<string, string?> campos, string key) =>
        campos.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    /// <summary>NIT sin puntos ni guiones: el directorio guarda el número limpio.</summary>
    private static string? Sanitize(string? nit)
    {
        if (string.IsNullOrWhiteSpace(nit))
        {
            return null;
        }

        var limpio = new string([.. nit.Where(char.IsAsciiDigit)]);
        return limpio.Length == 0 ? null : limpio;
    }
}
