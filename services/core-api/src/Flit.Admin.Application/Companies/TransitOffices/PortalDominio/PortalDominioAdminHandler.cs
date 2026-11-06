using System.Security.Cryptography;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.Domains;
using Flit.Admin.Application.Plataforma.PersonaNatural;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Admin.Domain.Companies.TransitOffices.PortalDominio;

namespace Flit.Admin.Application.Companies.TransitOffices.PortalDominio;

/// <summary>
/// HU #14381/#14382 — leer, registrar, verificar y retirar el dominio del portal de un organismo. El alcance (organismo propio o
/// SuperAdmin) lo resuelve el endpoint; aquí solo hay reglas de negocio. Un dominio por organismo; el host no se repite en toda la
/// plataforma (otro organismo o una red de marca blanca) ni puede ser uno reservado.
/// </summary>
public sealed class PortalDominioAdminHandler(
    IPortalDominioRepository repo, PortalDominioVerificador verificador, DomainOptions opciones, IAdminAuditWriter audit)
{
    public async Task<PortalDominioResultado<PortalDominioDto>> ObtenerAsync(Guid transitOfficeId, CancellationToken ct = default) =>
        await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false) is { } d
            ? PortalDominioResultado<PortalDominioDto>.De(PortalDominioEstados.ADto(d, opciones.EdgeIp))
            : Falla(PortalDominioErrores.SinDominio());

    public async Task<PortalDominioResultado<PortalDominioDto>> RegistrarAsync(
        Guid transitOfficeId, RegistrarPortalDominioRequest request, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!HostNormalizer.TryNormalize(request.Host, out var host, out _))
            return Falla(PortalDominioErrores.DominioInvalido());
        if (ReservedHosts.IsReserved(host, opciones.Reserved, opciones.Allowed))
            return Falla(PortalDominioErrores.DominioReservado(host));

        var registro = await repo.RegistrarAsync(transitOfficeId, host, NuevoToken(), actor, ct).ConfigureAwait(false);
        if (registro.Fallo == PortalDominioRegistroFallo.HostEnUso)
            return Falla(PortalDominioErrores.DominioEnUso(host));
        if (registro.Fallo == PortalDominioRegistroFallo.OrganismoYaTiene)
        {
            var actual = await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
            return Falla(PortalDominioErrores.YaTieneDominio(actual?.Host ?? host));
        }

        var dominio = registro.Dominio!;
        await EscribirAuditoriaAsync(actor, transitOfficeId, AuditVocabulary.Operations.Create, null, host, ct).ConfigureAwait(false);
        return PortalDominioResultado<PortalDominioDto>.De(PortalDominioEstados.ADto(dominio, opciones.EdgeIp));
    }

    public async Task<PortalDominioResultado<PortalDominioDto>> VerificarAsync(Guid transitOfficeId, Guid actor, CancellationToken ct = default)
    {
        var (dominio, esperar) = await verificador.VerificarAhoraAsync(transitOfficeId, actor, ct).ConfigureAwait(false);
        if (dominio is null)
            return Falla(PortalDominioErrores.SinDominio());
        return esperar > 0
            ? Falla(PortalDominioErrores.VerificacionReciente(esperar))
            : PortalDominioResultado<PortalDominioDto>.De(PortalDominioEstados.ADto(dominio, opciones.EdgeIp));
    }

    public async Task<PortalDominioResultado<PortalDominioDto>> RetirarAsync(Guid transitOfficeId, Guid actor, CancellationToken ct = default)
    {
        if (await repo.RetirarAsync(transitOfficeId, actor, ct).ConfigureAwait(false) is not { } retirado)
            return Falla(PortalDominioErrores.SinDominio());

        await EscribirAuditoriaAsync(actor, transitOfficeId, AuditVocabulary.Operations.Delete, retirado.Host, null, ct).ConfigureAwait(false);
        return PortalDominioResultado<PortalDominioDto>.De(PortalDominioEstados.ADto(retirado, opciones.EdgeIp));
    }

    private static PortalDominioResultado<PortalDominioDto> Falla(PortalDominioError e) => PortalDominioResultado<PortalDominioDto>.Falla(e);

    /// <summary>256 bits en base64url sin relleno (43 caracteres): cumple el CHECK de la tabla y no se reutiliza jamás.</summary>
    private static string NuevoToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private Task EscribirAuditoriaAsync(Guid actor, Guid officeId, string operacion, string? anterior, string? nuevo, CancellationToken ct) =>
        audit.WriteAsync(
            new AdminAuditEntry(
                TenantId: null,
                TenantType: null,
                Module: AuditVocabulary.Modules.Config,
                EntityName: "PortalDominio",
                Operation: operacion,
                Result: AuditVocabulary.Results.Success,
                ErrorCode: null,
                ActorUserId: actor,
                TargetEntityType: "TRANSIT_OFFICE_PORTAL_DOMINIO",
                TargetEntityId: officeId,
                ClientIp: null,
                UserAgent: null,
                OldValue: anterior is null ? null : PersonaNaturalAuditoria.Json(("host", anterior)),
                NewValue: nuevo is null ? null : PersonaNaturalAuditoria.Json(("host", nuevo))),
            ct);
}
