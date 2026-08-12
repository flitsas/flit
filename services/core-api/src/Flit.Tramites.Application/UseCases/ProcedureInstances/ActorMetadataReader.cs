using System.Text.Json;
using Flit.Tramites.Domain.Tramites.Enums;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Lectura robusta de <c>actor.metadata</c> (jsonb). Punto único para deserializar ciudad y demás
/// campos embebidos sin duplicar parsers frágiles en infraestructura o FUR.
/// </summary>
public static class ActorMetadataReader
{
    private static readonly JsonSerializerOptions MetadataJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Serializa ciudad/dirección + representante legal al JSON de <c>actor.metadata</c>.
    /// Sin ningún dato → "{}".
    /// </summary>
    public static string Serialize(
        string? ciudad,
        string? direccion,
        ActorRepresentanteLegal? rl,
        ActorMandante? mandante = null)
    {
        var c = string.IsNullOrWhiteSpace(ciudad) ? null : ciudad.Trim();
        var d = string.IsNullOrWhiteSpace(direccion) ? null : direccion.Trim();
        var repLegal = NormalizeRepresentanteLegal(rl);
        var mand = NormalizeMandante(mandante);
        return c is null && d is null && repLegal is null && mand is null
            ? "{}"
            : JsonSerializer.Serialize(new ActorMetadata(c, d, repLegal, mand), MetadataJson);
    }

    /// <summary>Ciudad del actor (sin dirección). <c>null</c> si ausente o JSON inválido.</summary>
    public static string? GetCiudad(string? metadata)
    {
        var (ciudad, _, _, _) = Parse(metadata);
        return ciudad;
    }

    /// <summary>
    /// Lee ciudad/dirección + representante legal + mandante. Robusto ante null/"{}"/JSON inválido.
    /// </summary>
    public static (string? Ciudad, string? Direccion, ActorRepresentanteLegal? RepresentanteLegal, ActorMandante? Mandante) Parse(
        string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata) || metadata == "{}")
            return (null, null, null, null);
        try
        {
            var m = JsonSerializer.Deserialize<ActorMetadata>(metadata, MetadataJson);
            return (m?.Ciudad, m?.Direccion, NormalizeRepresentanteLegal(m?.RepresentanteLegal), NormalizeMandante(m?.Mandante));
        }
        catch (JsonException)
        {
            return (null, null, null, null);
        }
    }

    private static ActorMandante? NormalizeMandante(ActorMandante? mandante)
    {
        if (mandante is null)
            return null;
        string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        var tipo = Clean(mandante.TipoDocumento);
        var numero = Clean(mandante.NumeroDocumento);
        var nombre = Clean(mandante.NombreCompleto);
        var email = Clean(mandante.Email);
        return tipo is null && numero is null && nombre is null && email is null
            ? null
            : new ActorMandante(tipo, numero, nombre, email);
    }

    private static ActorRepresentanteLegal? NormalizeRepresentanteLegal(ActorRepresentanteLegal? rl)
    {
        if (rl is null)
            return null;
        string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        var tipo = Clean(rl.TipoDocumento);
        var numero = Clean(rl.NumeroDocumento);
        var nombre = Clean(rl.NombreCompleto);
        var email = Clean(rl.Email);
        var telefono = Clean(rl.Telefono);
        var mecanismo = MecanismoFirma.Normalizar(rl.MecanismoFirma);
        return tipo is null && numero is null && nombre is null && email is null && telefono is null
            && mecanismo is null
            ? null
            : new ActorRepresentanteLegal(tipo, numero, nombre, email, telefono, mecanismo);
    }

    private sealed record ActorMetadata(
        string? Ciudad,
        string? Direccion,
        ActorRepresentanteLegal? RepresentanteLegal = null,
        ActorMandante? Mandante = null);
}
