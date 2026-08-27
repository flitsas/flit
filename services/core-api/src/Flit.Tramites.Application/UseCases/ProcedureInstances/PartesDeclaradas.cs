using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// ADR-0051 — traduce las capacidades declaradas del tipo (<c>biometricActors</c>,
/// <c>signatureActors</c>) a los <c>actor_type</c> internos en español
/// (<c>comprador</c>/<c>vendedor</c>/<c>locatario</c>) que usan el FUR, la biométrica, el listado de
/// operación, el consumidor de identidad y el proyector de correos.
///
/// <para><b>Por qué existe.</b> El §Contexto del ADR-0051 documenta que la misma pregunta ("¿quién
/// firma?", "¿quién valida identidad?") se respondía con cuatro criterios incompatibles repartidos
/// por el código. Las fases 2 y 3 sustituyeron esos criterios por el perfil, pero dejaron DOS
/// traductores catálogo→rol interno: <c>FurCommand.ResolveCatalogRoles</c> y el par
/// <c>PutActorsHandler.RolesQueValidanIdentidad</c> + <c>ActorTypeDeParteRol</c> de
/// <c>BiometricaCommand</c>. Esta clase es ese traductor, una sola vez: la nota del ADR para el
/// Backend Agent pide explícitamente reutilizarlo en vez de escribir una cuarta copia.</para>
///
/// <para><b>Orden.</b> <see cref="DeCatalogo"/> respeta el orden DECLARADO en el perfil (es el que
/// el FUR usa para estampar). Los consumidores que necesiten un orden estable de presentación —el
/// listado de validaciones biométricas— lo normalizan con <see cref="EnOrden"/>; no se impone aquí
/// para no alterar el estampado del FUR.</para>
/// </summary>
public static class PartesDeclaradas
{
    /// <summary>Orden canónico de presentación de las partes (el del listado de biométricas).</summary>
    private static readonly string[] Orden =
        [BiometricRules.ParteComprador, BiometricRules.ParteVendedor, ParteLocatario];

    private const string ParteLocatario = "locatario";

    /// <summary>
    /// Traduce roles del vocabulario de catálogo (<c>OWNER</c>/<c>BUYER</c>/<c>LESSEE</c>) al
    /// <c>actor_type</c> interno, con <see cref="RuntConsultaExigida.ActorTypeDeEntidad"/>. Un
    /// conjunto vacío —llave ausente del JSON o arreglo vacío, que el perfil no distingue— cae al
    /// comportamiento previo a ADR-0051: vendedor+comprador si el tipo exige parte vendedora, solo
    /// comprador si no. Los códigos que el catálogo no sabe traducir se descartan en silencio, igual
    /// que hacía <c>FurCommand</c>: un perfil con un rol desconocido no debe tumbar la generación.
    /// </summary>
    public static string[] DeCatalogo(IReadOnlyList<string> catalogRoles, bool requiresSeller)
    {
        if (catalogRoles.Count == 0)
            return requiresSeller
                ? [BiometricRules.ParteVendedor, BiometricRules.ParteComprador]
                : [BiometricRules.ParteComprador];

        return catalogRoles
            .Select(RuntConsultaExigida.ActorTypeDeEntidad)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToArray();
    }

    /// <summary>Partes que validan identidad (<c>biometricActors</c>), en el orden declarado.</summary>
    public static string[] Identidad(ProcedureTypeGateProfile profile) =>
        DeCatalogo(profile.BiometricActors, profile.RequiresSeller);

    /// <inheritdoc cref="Identidad(ProcedureTypeGateProfile)"/>
    public static string[] Identidad(ProcedureInstance instance) =>
        Identidad(ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile));

    /// <summary>
    /// Partes que firman (<c>signatureActors</c>), en el orden declarado. Se resuelve con
    /// <see cref="ProcedureTypeGateProfile.ResolveSignatureActors"/>, nunca con la propiedad cruda.
    /// </summary>
    public static string[] Firma(ProcedureTypeGateProfile profile) =>
        DeCatalogo(profile.ResolveSignatureActors(), profile.RequiresSeller);

    /// <inheritdoc cref="Firma(ProcedureTypeGateProfile)"/>
    public static string[] Firma(ProcedureInstance instance) =>
        Firma(ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile));

    /// <summary>
    /// Reordena un conjunto de partes al orden canónico de presentación (comprador, vendedor,
    /// locatario). Las partes que no estén en ese orden conocido se conservan al final, en el orden
    /// en que venían, para no perderlas si el catálogo crece.
    /// </summary>
    public static string[] EnOrden(IReadOnlyList<string> partes)
    {
        if (partes.Count <= 1)
            return [.. partes];

        var conocidas = Orden.Where(o =>
            partes.Any(p => string.Equals(p, o, StringComparison.OrdinalIgnoreCase)));
        var resto = partes.Where(p =>
            !Orden.Any(o => string.Equals(p, o, StringComparison.OrdinalIgnoreCase)));
        return [.. conocidas, .. resto];
    }

    /// <summary>¿Esta parte participa del conjunto declarado? Comparación insensible a mayúsculas.</summary>
    public static bool Incluye(IReadOnlyList<string> partes, string parte) =>
        partes.Any(p => string.Equals(p, parte, StringComparison.OrdinalIgnoreCase));
}
