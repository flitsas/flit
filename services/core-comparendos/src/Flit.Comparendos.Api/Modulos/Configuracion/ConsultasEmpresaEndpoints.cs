using Flit.Comparendos.Api.Modulos.Calendario;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Ingesta;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Configuracion;

/// <summary>
/// Configurador de consultas por empresa (HU #13521). El SuperAdmin define por canal si se consulta, con qué proveedor
/// y en qué agenda, y activa la configuración: tener el producto encendido no dispara ninguna consulta. La empresa solo
/// ve la agenda (canales, horas, días, última y próxima consulta): los proveedores son información interna de FLIT.
/// </summary>
public static class ConsultasEmpresaEndpoints
{
    public static readonly CanalIngesta[] Canales = [CanalIngesta.SimitNit, CanalIngesta.SimitPlaca, CanalIngesta.MunicipalNit, CanalIngesta.MunicipalPlaca];

    public sealed record CambioCanal(bool Encendido, string? Proveedor, string? Modo, IReadOnlyList<string>? Horas, int? IntervaloMinutos, string? Desde, string? Hasta, bool SoloDiasHabiles);

    public static void Map(RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin/empresas/{empresa:guid}/consultas").RequireAuthorization(AutorizacionExtensions.PoliticaSuperAdmin);
        admin.MapGet("", Obtener);
        admin.MapPut("/canales/{canal}", GuardarCanal);
        admin.MapPost("/activar", Activar);
        admin.MapPost("/pausar", Pausar);
        api.MapGet("/configuracion/consultas", Agenda).RequirePermiso(Permisos.ConfiguracionVer);
    }

    public static string Etiqueta(CanalIngesta c) => c switch
    {
        CanalIngesta.SimitNit => "SIMIT por NIT o documento",
        CanalIngesta.SimitPlaca => "SIMIT por placa",
        CanalIngesta.MunicipalNit => "Fuente municipal por NIT o documento",
        _ => "Fuente municipal por placa",
    };

    private static async Task<ConfiguracionConsultas> ConfiguracionAsync(ComparendosDb db, Guid empresa, Reloj reloj, Guid usuario, CancellationToken ct)
    {
        var cfg = await db.ConfiguracionesConsultas.FirstOrDefaultAsync(c => c.TenantId == empresa, ct).ConfigureAwait(false);
        if (cfg is not null)
            return cfg;
        if (!await db.ConfiguracionesEmpresa.AnyAsync(c => c.TenantId == empresa, ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("La empresa");
        cfg = new ConfiguracionConsultas { TenantId = empresa, CreadoEn = reloj.Ahora, CreadoPor = usuario };
        db.ConfiguracionesConsultas.Add(cfg);
        return cfg;
    }

    private static async Task<object> VistaAsync(ComparendosDb db, Guid empresa, bool conProveedores, IFuenteComparendos fuente, IProductosHabilitados productos,
        CalendarioService calendario, Reloj reloj, CancellationToken ct)
    {
        var cfg = await db.ConfiguracionesConsultas.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == empresa, ct).ConfigureAwait(false);
        var canales = await db.ConfiguracionConsultaCanales.AsNoTracking().Where(c => c.TenantId == empresa).ToListAsync(ct).ConfigureAwait(false);
        var cal = await calendario.ActualAsync(ct).ConfigureAwait(false);
        var local = reloj.AhoraColombia.DateTime;
        var activa = cfg?.Estado == EstadoConfiguracionConsultas.Activa;
        var filas = Canales.Select(canal =>
        {
            var c = canales.FirstOrDefault(x => x.Canal == canal);
            var agenda = c?.Agenda();
            var proxima = activa && c is { Encendido: true } && agenda is not null && agenda.Validar().Count == 0 ? agenda.Proxima(local, cal) : null;
            return new Dictionary<string, object?>
            {
                ["canal"] = canal.ToString(),
                ["etiqueta"] = Etiqueta(canal),
                ["configurado"] = c is not null && c.Proveedor.Length > 0,
                ["encendido"] = c?.Encendido ?? false,
                ["modo"] = (c?.Modo ?? ModoAgenda.HorasFijas).ToString(),
                ["horas"] = c?.Horas ?? [],
                ["intervaloMinutos"] = c?.IntervaloMinutos,
                ["desde"] = c?.VentanaDesde?.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                ["hasta"] = c?.VentanaHasta?.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                ["soloDiasHabiles"] = c?.SoloDiasHabiles ?? false,
                ["descripcion"] = agenda is not null && c!.Proveedor.Length > 0 ? agenda.Describir() : null,
                ["ultimaConsulta"] = c?.UltimaEjecucionEn,
                ["ultimoResultado"] = c?.UltimoResultado?.ToString(),
                // Con su zona (-05:00), igual que la última consulta lleva la suya: el cliente no adivina.
                ["proximaConsulta"] = proxima is { } p ? new DateTimeOffset(p, Domain.Tiempo.ColombiaTime.Offset) : null,
                ["proveedor"] = conProveedores ? c?.Proveedor : null,
            }.Where(kv => conProveedores || kv.Key != "proveedor").ToDictionary(kv => kv.Key, kv => kv.Value);
        }).ToList();

        var vista = new Dictionary<string, object?>
        {
            ["estado"] = (cfg?.Estado ?? EstadoConfiguracionConsultas.Borrador).ToString(),
            ["activadaEn"] = cfg?.ActivadaEn,
            ["canales"] = filas,
        };
        if (conProveedores)
        {
            vista["productoHabilitado"] = await productos.ComparendosHabilitadoAsync(empresa, ct).ConfigureAwait(false);
            vista["proveedores"] = (await fuente.ProveedoresAsync(ct).ConfigureAwait(false))
                .Select(p => new { p.Clave, p.Nombre, canales = p.Canales.Select(x => x.ToString()), p.Conectado, p.Motivo });
        }

        return vista;
    }

    private static async Task<IResult> Obtener(Guid empresa, ComparendosDb db, IFuenteComparendos fuente, IProductosHabilitados productos, CalendarioService calendario, Reloj reloj, CancellationToken ct)
    {
        if (!await db.ConfiguracionesEmpresa.AnyAsync(c => c.TenantId == empresa, ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("La empresa");
        return Results.Ok(await VistaAsync(db, empresa, conProveedores: true, fuente, productos, calendario, reloj, ct).ConfigureAwait(false));
    }

    /// <summary>La empresa ve su agenda, nunca los proveedores.</summary>
    private static async Task<IResult> Agenda(Guid? empresa, UsuarioActual usuario, ComparendosDb db, IFuenteComparendos fuente, IProductosHabilitados productos,
        CalendarioService calendario, Reloj reloj, CancellationToken ct)
    {
        var tenant = usuario.EmpresaLectura(empresa, obligatoria: true)!.Value;
        return Results.Ok(await VistaAsync(db, tenant, conProveedores: usuario.EsSuperAdmin, fuente, productos, calendario, reloj, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> GuardarCanal(Guid empresa, string canal, CambioCanal cambio, ComparendosDb db, IFuenteComparendos fuente, Auditoria auditoria,
        UsuarioActual usuario, Reloj reloj, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cambio);
        if (!Enum.TryParse<CanalIngesta>(canal, true, out var c) || !Canales.Contains(c))
            throw new ReglaException("CANAL_INVALIDO", "Canal inválido: SimitNit, SimitPlaca, MunicipalNit o MunicipalPlaca.");
        if (!Enum.TryParse<ModoAgenda>(cambio.Modo ?? nameof(ModoAgenda.HorasFijas), true, out var modo))
            throw new ReglaException("MODO_INVALIDO", "Modo de agenda inválido: HorasFijas o Intervalo.");
        var horas = (cambio.Horas ?? []).Select(h => AgendaConsultas.Hora(h) ?? throw new ReglaException("HORA_INVALIDA", $"«{h}» no es una hora válida (HH:mm).")).ToList();
        var agenda = new AgendaConsultas(modo, horas, cambio.IntervaloMinutos, AgendaConsultas.Hora(cambio.Desde), AgendaConsultas.Hora(cambio.Hasta), cambio.SoloDiasHabiles);
        if (agenda.Validar() is { Count: > 0 } errores)
            throw new ReglaException("AGENDA_INVALIDA", string.Join(" ", errores));
        var proveedor = cambio.Proveedor?.Trim() ?? string.Empty;
        if (proveedor.Length == 0)
            throw new ReglaException("PROVEEDOR_REQUERIDO", "Elige el proveedor del canal.");
        var disponible = (await fuente.ProveedoresAsync(ct).ConfigureAwait(false)).FirstOrDefault(p => p.Clave == proveedor)
            ?? throw new ReglaException("PROVEEDOR_INVALIDO", $"El proveedor «{proveedor}» no existe en este ambiente.");
        if (!disponible.Canales.Contains(c))
            throw new ReglaException("PROVEEDOR_NO_ATIENDE", $"{disponible.Nombre} no atiende {Etiqueta(c)}.");
        if (cambio.Encendido && !disponible.Conectado)
            throw new ReglaException("PROVEEDOR_NO_CONECTADO", $"{disponible.Nombre} no está conectado en este ambiente: {disponible.Motivo}");

        var cfg = await ConfiguracionAsync(db, empresa, reloj, usuario.Id, ct).ConfigureAwait(false);
        var fila = await db.ConfiguracionConsultaCanales.FirstOrDefaultAsync(x => x.TenantId == empresa && x.Canal == c, ct).ConfigureAwait(false);
        var horasTexto = horas.OrderBy(h => h).Select(h => h.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)).ToList();
        if (fila is null)
        {
            fila = new ConfiguracionConsultaCanal { Id = Guid.CreateVersion7(), TenantId = empresa, Canal = c, CreadoEn = reloj.Ahora, CreadoPor = usuario.Id };
            db.ConfiguracionConsultaCanales.Add(fila);
        }

        var antes = (fila.Encendido, fila.Proveedor, fila.Agenda().Describir());
        (fila.Encendido, fila.Proveedor, fila.Modo, fila.Horas, fila.IntervaloMinutos, fila.VentanaDesde, fila.VentanaHasta, fila.SoloDiasHabiles) =
            (cambio.Encendido, proveedor, modo, modo == ModoAgenda.HorasFijas ? horasTexto : [], modo == ModoAgenda.Intervalo ? cambio.IntervaloMinutos : null,
             modo == ModoAgenda.Intervalo ? agenda.Desde : null, modo == ModoAgenda.Intervalo ? agenda.Hasta : null, cambio.SoloDiasHabiles);
        (fila.ActualizadoEn, fila.ActualizadoPor, cfg.ActualizadoEn, cfg.ActualizadoPor) = (reloj.Ahora, usuario.Id, reloj.Ahora, usuario.Id);
        auditoria.Registrar(empresa, "configuracion_consulta_canal", c.ToString(), "GUARDAR", Auditoria.Diferencias(
            ("encendido", antes.Encendido, fila.Encendido), ("proveedor", antes.Proveedor, fila.Proveedor), ("agenda", antes.Item3, fila.Agenda().Describir())));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> Activar(Guid empresa, ComparendosDb db, IFuenteComparendos fuente, IProductosHabilitados productos, Auditoria auditoria,
        UsuarioActual usuario, Reloj reloj, CancellationToken ct)
    {
        if (!await productos.ComparendosHabilitadoAsync(empresa, ct).ConfigureAwait(false))
            throw ReglaException.Conflicto("PRODUCTO_NO_HABILITADO", "La empresa no tiene Comparendos habilitado en la plataforma (Compañías → Productos).");
        var encendidos = await db.ConfiguracionConsultaCanales.AsNoTracking().Where(c => c.TenantId == empresa && c.Encendido && c.Proveedor != "")
            .ToListAsync(ct).ConfigureAwait(false);
        if (encendidos.Count == 0)
            throw new ReglaException("SIN_CANALES", "Enciende al menos un canal con su proveedor y agenda antes de activar.");
        var proveedores = await fuente.ProveedoresAsync(ct).ConfigureAwait(false);
        var caidos = encendidos.Where(c => proveedores.FirstOrDefault(p => p.Clave == c.Proveedor) is not { Conectado: true }).Select(c => Etiqueta(c.Canal)).ToList();
        if (caidos.Count > 0)
            throw new ReglaException("PROVEEDOR_NO_CONECTADO", $"El proveedor no está conectado para: {string.Join(", ", caidos)}.");

        var cfg = await ConfiguracionAsync(db, empresa, reloj, usuario.Id, ct).ConfigureAwait(false);
        auditoria.Registrar(empresa, "configuracion_consultas", empresa, "ACTIVAR", new CambioCampo("estado", cfg.Estado.ToString(), nameof(EstadoConfiguracionConsultas.Activa)));
        (cfg.Estado, cfg.ActivadaEn, cfg.ActivadaPor, cfg.ActualizadoEn, cfg.ActualizadoPor) = (EstadoConfiguracionConsultas.Activa, reloj.Ahora, usuario.Id, reloj.Ahora, usuario.Id);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> Pausar(Guid empresa, ComparendosDb db, Auditoria auditoria, UsuarioActual usuario, Reloj reloj, CancellationToken ct)
    {
        var cfg = await db.ConfiguracionesConsultas.FirstOrDefaultAsync(c => c.TenantId == empresa, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La configuración de consultas");
        auditoria.Registrar(empresa, "configuracion_consultas", empresa, "PAUSAR", new CambioCampo("estado", cfg.Estado.ToString(), nameof(EstadoConfiguracionConsultas.Pausada)));
        (cfg.Estado, cfg.ActualizadoEn, cfg.ActualizadoPor) = (EstadoConfiguracionConsultas.Pausada, reloj.Ahora, usuario.Id);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }
}
