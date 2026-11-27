using System.Collections.Frozen;
using Flit.Identidad.Grpc.V1;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Modules.Platform.Application.Companies;
using Flit.Modules.Security.Application.Products;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using EstadoNitEmpresa = Flit.Modules.Platform.Domain.Companies.EstadoNit;

namespace Flit.Identity.Api.Grpc;

/// <summary>
/// <c>flit.identidad.v1.EmpresasService</c>: las empresas que tienen un producto encendido, para que el SuperAdmin de un
/// producto elija la empresa de una lista y tome su nombre y su NIT de la plataforma (GDC, hallazgos B2-1 y B2-4). Es
/// global (no usa <c>x-flit-tenant-id</c>) y por eso exige su propio scope, <see cref="Scope"/>.
/// HU #14694: <see cref="ObtenerDatosAltaProveedor"/> va por UNA empresa (metadata obligatoria), solo para
/// <c>svc-impuestos</c> y con su scope <see cref="ScopeAlta"/> (lo exige <see cref="ServiceTokenPorMetodoInterceptor"/>).
/// </summary>
internal sealed class EmpresasGrpcService(IIdentityDb db, ObtenerDatosAltaProveedorHandler alta) : EmpresasService.EmpresasServiceBase
{
    public const string Scope = "platform.identidad.empresas";

    /// <summary>HU #14694 — scope del rpc del alta en el proveedor.</summary>
    public const string ScopeAlta = "platform.identidad.empresas.alta";

    /// <summary>Clientes que pueden pedir los datos del alta, con el producto de cada uno.</summary>
    private static readonly FrozenDictionary<string, string> ClientesAlta = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["svc-impuestos"] = ProductCodes.Impuestos,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Valor de <c>notification_channel</c> para «Colas FLIT», el de las empresas sin política.</summary>
    internal const string CanalFlit = "flit_smtp";

    /// <summary>Mismo tope que <c>ProductAccessStore</c> al subir por <c>parent_tenant_id</c>.</summary>
    private const int MaxHierarchyDepth = 8;

    public override async Task<ListarEmpresasConProductoResponse> ListarEmpresasConProducto(ListarEmpresasConProductoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var producto = request.Producto;
        if (string.IsNullOrEmpty(producto) || producto == ProductCodes.Plataforma || !ProductCodes.All.Contains(producto, StringComparer.Ordinal))
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Producto inválido: «{producto}»."));

        var ct = context.CancellationToken;
        var encendidas = (await db.Set<TenantProductEntity>().AsNoTracking()
            .Where(r => r.ProductCode == producto && r.Enabled)
            .Select(r => r.TenantId)
            .ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var empresas = await db.Tenants.AsNoTracking()
            .Select(t => new { t.Id, t.LegalName, t.TaxId, t.IsActive, t.ParentTenantId })
            .ToDictionaryAsync(t => t.Id, ct).ConfigureAwait(false);

        // Fail-closed (ADR-0057): la empresa y cada ancestro con el producto encendido; una cadena rota o demasiado larga
        // no cuenta.
        bool Habilitada(Guid id)
        {
            Guid? actual = id;
            for (var nivel = 0; actual is { } a; nivel++)
            {
                if (nivel >= MaxHierarchyDepth || !empresas.TryGetValue(a, out var fila) || !encendidas.Contains(a))
                    return false;
                actual = fila.ParentTenantId;
            }

            return true;
        }

        var listadas = encendidas.Where(id => empresas.TryGetValue(id, out var e) && e.IsActive && Habilitada(id)).ToList();
        // HU #13882: el canal de correo que la empresa eligió en Trámites; sin política, el de FLIT (como Trámites).
        var canales = await db.Set<TenantOperationalPolicy>().AsNoTracking()
            .Where(p => listadas.Contains(p.TenantId))
            .Select(p => new { p.TenantId, p.NotificationChannel })
            .ToDictionaryAsync(p => p.TenantId, p => p.NotificationChannel, ct).ConfigureAwait(false);

        var respuesta = new ListarEmpresasConProductoResponse();
        respuesta.Empresas.AddRange(listadas
            .Select(id => empresas[id])
            .OrderBy(e => e.LegalName, StringComparer.CurrentCultureIgnoreCase)
            .Select(e => new Empresa
            {
                Id = e.Id.ToString(),
                Nombre = e.LegalName,
                Nit = e.TaxId ?? string.Empty,
                CanalCorreo = canales.GetValueOrDefault(e.Id) is { Length: > 0 } canal ? canal : CanalFlit,
            }));
        return respuesta;
    }

    /// <summary>
    /// HU #14694 (plan §1.3 P1) — datos para dar de alta la empresa de la metadata en el proveedor del producto del cliente.
    /// Mismo handler que <c>GET /api/v1/platform/admin/tenants/{tenantId}/products/impuestos/alta-previa</c>.
    /// </summary>
    public override async Task<ObtenerDatosAltaProveedorResponse> ObtenerDatosAltaProveedor(ObtenerDatosAltaProveedorRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var caller = PlatformServiceCaller.From(context);
        if (caller.TenantId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Falta x-flit-tenant-id."));
        if (!ClientesAlta.TryGetValue(caller.ClientId, out var producto))
        {
            throw PlatformRpcErrors.ToRpcException(
                "CLIENTE_NO_PERMITIDO", "Este rpc solo lo atienden los productos con aprovisionamiento.", StatusCode.PermissionDenied);
        }

        var datos = await alta.HandleAsync(caller.TenantId, producto, context.CancellationToken).ConfigureAwait(false)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "La empresa no existe."));

        return new ObtenerDatosAltaProveedorResponse
        {
            LegalName = datos.LegalName,
            TaxIdBase = datos.TaxIdBase ?? string.Empty,
            EstadoNit = datos.EstadoNit switch
            {
                EstadoNitEmpresa.Valido => EstadoNit.Valido,
                EstadoNitEmpresa.Ambiguo => EstadoNit.Ambiguo,
                EstadoNitEmpresa.Ausente => EstadoNit.Ausente,
                EstadoNitEmpresa.IgualACabeza => EstadoNit.IgualACabeza,
                _ => EstadoNit.Unspecified,
            },
            ContactEmail = datos.ContactEmail ?? string.Empty,
            EmpresaActiva = datos.EmpresaActiva,
        };
    }
}
