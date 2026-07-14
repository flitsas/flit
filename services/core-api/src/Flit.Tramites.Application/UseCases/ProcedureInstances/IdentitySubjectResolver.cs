using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Sujeto de identidad de una parte del trámite (HU #10688): quién valida biométricamente y a qué correo
/// se envía la validación. Persona natural → los datos del propio actor. Persona jurídica → los datos del
/// representante legal / apoderado (persona natural embebida en <c>actor.metadata</c>), que es quien puede
/// validar identidad. El NIT de la empresa no es validable biométricamente.
/// </summary>
public sealed record IdentitySubject(
    string? Nombre,
    string? TipoDocumento,
    string? NumeroDocumento,
    string? Email,
    bool EsRepresentanteLegal);

/// <summary>
/// Único punto donde se decide "quién valida la identidad" de una parte. Todos los consumidores (biometría,
/// gate de identidad, sello y certificado del FUR) resuelven el sujeto por aquí para no dispersar la lógica
/// jurídica y no introducir regresión en persona natural (para PN devuelve exactamente el actor).
/// </summary>
public static class IdentitySubjectResolver
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Resuelve el sujeto de identidad del actor. Para persona jurídica devuelve el representante legal
    /// SOLO si tiene documento (necesario para validar biométricamente y anclar la identidad); si el RL no
    /// tiene documento, cae al actor (comportamiento previo, la PJ saldrá "NO FIRMADO" hasta capturarlo).
    /// </summary>
    public static IdentitySubject For(ProcedureInstanceActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (ActorPersonTypes.IsJuridical(actor.PersonType))
        {
            var rl = ParseRepresentanteLegal(actor.Metadata);
            if (rl is not null
                && !string.IsNullOrWhiteSpace(rl.TipoDocumento)
                && !string.IsNullOrWhiteSpace(rl.NumeroDocumento))
            {
                return new IdentitySubject(
                    string.IsNullOrWhiteSpace(rl.NombreCompleto) ? actor.FullName : rl.NombreCompleto!.Trim(),
                    rl.TipoDocumento!.Trim(),
                    rl.NumeroDocumento!.Trim(),
                    string.IsNullOrWhiteSpace(rl.Email) ? actor.Email : rl.Email!.Trim(),
                    EsRepresentanteLegal: true);
            }
        }

        return new IdentitySubject(
            actor.FullName, actor.DocumentType, actor.DocumentNumber, actor.Email, EsRepresentanteLegal: false);
    }

    /// <summary>Lee el representante legal embebido en <c>actor.metadata</c>. Robusto ante null/"{}"/JSON inválido.</summary>
    private static ActorRepresentanteLegal? ParseRepresentanteLegal(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata) || metadata == "{}")
            return null;
        try
        {
            return JsonSerializer.Deserialize<MetadataShape>(metadata, Json)?.RepresentanteLegal;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Vista parcial del <c>actor.metadata</c>: solo el representante legal (mismo shape que ActorsCommand).</summary>
    private sealed record MetadataShape(ActorRepresentanteLegal? RepresentanteLegal);
}
