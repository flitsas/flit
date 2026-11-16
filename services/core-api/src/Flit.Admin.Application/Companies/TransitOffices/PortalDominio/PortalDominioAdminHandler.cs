using System.Security.Cryptography;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.Domains;
using Flit.Admin.Application.Plataforma.PersonaNatural;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Admin.Domain.Companies.TransitOffices.PortalDominio;

namespace Flit.Admin.Application.Companies.TransitOffices.PortalDominio;

/// <summary>
/// HU #14381/#14382 — listar, registrar, verificar, cambiar el principal y retirar los dominios del portal de un organismo. El
/// alcance (organismo propio o SuperAdmin) lo resuelve el endpoint; aquí además un <c>id</c> de otro organismo da 403. Un organismo
/// tiene varios dominios (hasta <see cref="DomainOptions.PortalMaxPorOrganismo"/>), cada uno con su propio estado; el host no se
/// repite en toda la plataforma (otro organismo o una red de marca blanca) ni puede ser uno reservado.
/// </summary>
public sealed class PortalDominioAdminHandler(
    IPortalDominioRepository repo, PortalDominioVerificador verificador, DomainOptions opciones, IAdminAuditWriter audit)
{
    private int Maximo => Math.Max(1, opciones.PortalMaxPorOrganismo);

    /// <summary>Lista vacía (no un error) si el organismo no tiene dominios.</summary>
    public async Task<IReadOnlyList<PortalDominioDto>> ListarAsync(Guid transitOfficeId, CancellationToken ct = default) =>
        (await repo.ListarAsync(transitOfficeId, ct).ConfigureAwait(false)).Select(d => PortalDominioEstados.ADto(d, opciones.EdgeIp)).ToList();

    public async Task<PortalDominioResultado<PortalDominioDto>> RegistrarAsync(
        Guid transitOfficeId, RegistrarPortalDominioRequest request, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!HostNormalizer.TryNormalize(request.Host, out var host, out _))
            return Falla(PortalDominioErrores.DominioInvalido());
        if (ReservedHosts.IsReserved(host, opciones.Reserved, opciones.Allowed))
            return Falla(PortalDominioErrores.DominioReservado(host));

        var registro = await repo.RegistrarAsync(transitOfficeId, host, NuevoToken(), actor, Maximo, ct).ConfigureAwait(false);
        if (registro.Fallo == PortalDominioRegistroFallo.HostEnUso)
            return Falla(PortalDominioErrores.DominioEnUso(host));
        if (registro.Fallo == PortalDominioRegistroFallo.LimiteAlcanzado)
            return Falla(PortalDominioErrores.LimiteAlcanzado(Maximo));

        var dominio = registro.Dominio!;
        await EscribirAuditoriaAsync(actor, transitOfficeId, AuditVocabulary.Operations.Create, null, host, ct).ConfigureAwait(false);
        return PortalDominioResultado<PortalDominioDto>.De(PortalDominioEstados.ADto(dominio, opciones.EdgeIp));
    }

    public async Task<PortalDominioResultado<PortalDominioDto>> VerificarAsync(Guid transitOfficeId, Guid id, Guid actor, CancellationToken ct = default)
    {
        var (propio, error) = await PropioAsync(transitOfficeId, id, ct).ConfigureAwait(false);
        if (error is not null)
            return Falla(error);

        var (dominio, esperar) = await verificador.VerificarAhoraAsync(propio!.Id, actor, ct).ConfigureAwait(false);
        if (dominio is null)
            return Falla(PortalDominioErrores.SinDominio());
        return esperar > 0
            ? Falla(PortalDominioErrores.VerificacionReciente(esperar))
            : PortalDominioResultado<PortalDominioDto>.De(PortalDominioEstados.ADto(dominio, opciones.EdgeIp));
    }

    /// <summary>Solo un dominio activo puede ser el principal; si no, el motivo exacto. Repetir sobre el principal es idempotente.</summary>
    public async Task<PortalDominioResultado<PortalDominioDto>> PrincipalAsync(Guid transitOfficeId, Guid id, Guid actor, CancellationToken ct = default)
    {
        var (propio, error) = await PropioAsync(transitOfficeId, id, ct).ConfigureAwait(false);
        if (error is not null)
            return Falla(error);
        if (propio!.Status != TenantDomainStatus.Active)
            return Falla(PortalDominioErrores.NoActivoParaPrincipal(propio.Host, PortalDominioEstados.Contrato(propio.Status)));

        var principal = await repo.EstablecerPrincipalAsync(id, actor, ct).ConfigureAwait(false);
        if (principal is null)
            return Falla(PortalDominioErrores.NoActivoParaPrincipal(propio.Host, "no activo"));

        if (!propio.IsPrimary)
            await EscribirAuditoriaAsync(actor, transitOfficeId, AuditVocabulary.Operations.Update, null, principal.Host, ct).ConfigureAwait(false);
        return PortalDominioResultado<PortalDominioDto>.De(PortalDominioEstados.ADto(principal, opciones.EdgeIp));
    }

    public async Task<PortalDominioResultado<PortalDominioDto>> RetirarAsync(Guid transitOfficeId, Guid id, Guid actor, CancellationToken ct = default)
    {
        var (_, error) = await PropioAsync(transitOfficeId, id, ct).ConfigureAwait(false);
        if (error is not null)
            return Falla(error);
        if (await repo.RetirarAsync(id, actor, ct).ConfigureAwait(false) is not { } retirado)
            return Falla(PortalDominioErrores.SinDominio());

        await EscribirAuditoriaAsync(actor, transitOfficeId, AuditVocabulary.Operations.Delete, retirado.Host, null, ct).ConfigureAwait(false);
        return PortalDominioResultado<PortalDominioDto>.De(PortalDominioEstados.ADto(retirado, opciones.EdgeIp));
    }

    /// <summary>Un id inexistente es 404; el de otro organismo es 403 y no revela nada más.</summary>
    private async Task<(DominioDePortal? Dominio, PortalDominioError? Error)> PropioAsync(Guid transitOfficeId, Guid id, CancellationToken ct)
    {
        if (await repo.ObtenerPorIdAsync(id, ct).ConfigureAwait(false) is not { } dominio)
            return (null, PortalDominioErrores.SinDominio());
        return dominio.TransitOfficeId == transitOfficeId ? (dominio, null) : (null, PortalDominioErrores.DominioAjeno());
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
