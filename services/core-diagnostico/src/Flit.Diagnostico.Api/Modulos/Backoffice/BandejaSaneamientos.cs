using System.Globalization;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Domain.Vehiculos;
using Microsoft.EntityFrameworkCore;
using SaneamientoEntidad = Flit.Diagnostico.Api.Modulos.Saneamiento.Saneamiento;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>Filtros ya validados de la bandeja (plan §6.2; SP-10). <see cref="Desde"/>/<see cref="Hasta"/>: rango semiabierto en hora Colombia.</summary>
internal sealed record FiltroBandeja(
    IReadOnlySet<EstadoSaneamiento> Estados, Guid? Empresa, string? Placa, DateTimeOffset? Desde, DateTimeOffset? Hasta, int Pagina, int Tamano)
{
    public const int TamanoPorDefecto = 25;
    /// <summary>UA-2: 1..100 (el selector del kit ofrece 10, 20, 30 y 50; 25 por defecto).</summary>
    public const int TamanoMaximo = 100;
    public const int DesplazamientoMaximo = 10_000;
    public const int PlacaMaxima = 20;

    /// <summary>
    /// Lee la consulta: <c>estado</c> repetible de la lista cerrada, <c>empresa</c> uuid, <c>placa</c> normalizada como el identificador
    /// (prefijo, hasta 20), <c>desde</c>/<c>hasta</c> AAAA-MM-DD, <c>pagina</c> ≥ 1, <c>tamano</c> 1..100 y desplazamiento ≤ 10 000. Todo
    /// fuera de rango es 400 (nunca se recorta en silencio) y un parámetro único repetido también.
    /// </summary>
    public static FiltroBandeja Leer(IQueryCollection query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var estados = new HashSet<EstadoSaneamiento>();
        foreach (var valor in query["estado"].Where(v => !string.IsNullOrEmpty(v)))
        {
            if (!CodigosDeEnum.TryLeer<EstadoSaneamiento>(valor, out var estado))
                throw Invalido(FiltroListado.CodigoFiltroInvalido, "estado es SOLICITADO, EN_GESTION, SANEADO, NO_PROCEDE o ANULADO.", "estado");
            estados.Add(estado);
        }

        Guid? empresa = null;
        if (Unico(query, "empresa") is { Length: > 0 } texto)
            empresa = Guid.TryParse(texto, out var id) && id != Guid.Empty ? id : throw Invalido(FiltroListado.CodigoFiltroInvalido, "empresa es el id de una compañía.", "empresa");

        string? placa = null;
        if (Unico(query, "placa") is { Length: > 0 } crudo)
        {
            if (crudo.Length > PlacaMaxima)
                throw Invalido(FiltroListado.CodigoFiltroInvalido, $"placa admite hasta {PlacaMaxima} caracteres.", "placa");
            placa = ValidadorIdentificador.Normalizar(crudo);
            if (placa.Length == 0)
                throw Invalido(FiltroListado.CodigoFiltroInvalido, "placa debe tener letras o dígitos.", "placa");
        }

        var fechas = FiltroListado.Leer(Unico(query, "desde"), Unico(query, "hasta"), null, null, null, "1", "1");
        var pagina = Entero(Unico(query, "pagina"), "pagina", 1);
        if (pagina < 1)
            throw Invalido(FiltroListado.CodigoPaginacionInvalida, "pagina empieza en 1.", "pagina");
        var tamano = Entero(Unico(query, "tamano"), "tamano", TamanoPorDefecto);
        if (tamano is < 1 or > TamanoMaximo)
            throw Invalido(FiltroListado.CodigoPaginacionInvalida, $"tamano va de 1 a {TamanoMaximo}.", "tamano");
        if ((long)pagina * tamano > DesplazamientoMaximo)
            throw Invalido(FiltroListado.CodigoPaginacionInvalida, $"pagina × tamano no puede pasar de {DesplazamientoMaximo}.", "pagina");
        return new FiltroBandeja(estados, empresa, placa, fechas.Desde, fechas.Hasta, pagina, tamano);
    }

    private static string? Unico(IQueryCollection query, string campo)
    {
        var valores = query[campo];
        if (valores.Count > 1)
            throw Invalido(FiltroListado.CodigoFiltroInvalido, $"{campo} va una sola vez.", campo);
        return valores.Count == 0 ? null : valores[0];
    }

    private static int Entero(string? valor, string campo, int porDefecto)
    {
        if (string.IsNullOrEmpty(valor))
            return porDefecto;
        return int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw Invalido(FiltroListado.CodigoPaginacionInvalida, $"{campo} es un número entero.", campo);
    }

    private static ReglaException Invalido(string codigo, string mensaje, string campo) =>
        new(codigo, mensaje, StatusCodes.Status400BadRequest) { Campo = campo };
}

/// <summary>
/// Bandeja de saneamientos entre compañías (plan §6.2 grupo A, ADR-0087 D-5; HU #14576 AC40–AC42; SP-4, SP-5, SP-10, SP-21). Es la
/// única lectura del servicio que recorre los saneamientos de todas las compañías: el acceso es privado de este archivo y no es
/// genérico (solo las tablas de la fila: saneamiento, vehículo, cabecera, dictamen vigente y renglones), y cada unión compara también
/// <c>tenant_id</c> (SP-5). Orden de la página y del Excel (SP-4): leer las filas sin seguimiento, auditar en UN guardado cada empresa
/// distinta presente (y la del filtro <c>empresa</c> aunque la página salga vacía) y solo entonces responder; si la auditoría falla, la
/// petición falla sin filas. El resumen no audita: es un conteo.
/// </summary>
internal sealed class BandejaSaneamientos(DiagnosticoDb db, AuditoriaLecturaBackoffice auditoria, IConfiguration configuracion)
{
    public const string ClaveMaximoExcel = "Diagnostico:Saneamiento:ExcelBandejaMaximoFilas";
    public const int MaximoExcelPorDefecto = 5000;
    public const string CodigoDemasiadasFilas = "DEMASIADAS_FILAS";

    public async Task<ListaBandejaDto> ListarAsync(HttpRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var filtro = FiltroBandeja.Leer(request.Query);
        var consulta = Consulta(filtro);
        var total = await consulta.CountAsync(ct).ConfigureAwait(false);
        var filas = await consulta.OrderBy(f => f.SolicitadoEn).ThenBy(f => f.Id)
            .Skip((filtro.Pagina - 1) * filtro.Tamano).Take(filtro.Tamano)
            .ToListAsync(ct).ConfigureAwait(false);
        await AuditarAsync(filtro, filas, request, ct).ConfigureAwait(false);
        return new ListaBandejaDto(filtro.Pagina, filtro.Tamano, total, [.. filas.Select(f => f.Dto())]);
    }

    public async Task<ResumenBandejaDto> ResumenAsync(CancellationToken ct) =>
        new(await Saneamientos().CountAsync(s => s.Estado == EstadoSaneamiento.Solicitado, ct).ConfigureAwait(false));

    /// <summary>El Excel con los filtros de la bandeja, sin paginar; más de <see cref="MaximoExcelPorDefecto"/> filas: 422.</summary>
    public async Task<byte[]> ExcelAsync(HttpRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var filtro = FiltroBandeja.Leer(request.Query);
        var consulta = Consulta(filtro);
        var maximo = configuracion.GetValue<int?>(ClaveMaximoExcel) is { } valor && valor > 0 ? valor : MaximoExcelPorDefecto;
        if (await consulta.CountAsync(ct).ConfigureAwait(false) > maximo)
            throw new ReglaException(CodigoDemasiadasFilas, $"El Excel admite hasta {maximo} filas: ajusta los filtros.");
        var filas = await consulta.OrderBy(f => f.SolicitadoEn).ThenBy(f => f.Id).ToListAsync(ct).ConfigureAwait(false);
        await AuditarAsync(filtro, filas, request, ct).ConfigureAwait(false);
        return ExcelBandejaSaneamientos.Generar([.. filas.Select(f => f.Dto())]);
    }

    private Task AuditarAsync(FiltroBandeja filtro, IReadOnlyList<FilaLeida> filas, HttpRequest request, CancellationToken ct)
    {
        var empresas = filas.Select(f => f.Empresa).ToList();
        if (filtro.Empresa is { } empresa)
            empresas.Add(empresa);
        return auditoria.RegistrarVariasAsync(empresas, request, ct);
    }

    private IQueryable<FilaLeida> Consulta(FiltroBandeja filtro)
    {
        var saneamientos = Saneamientos();
        if (filtro.Estados.Count > 0)
        {
            var estados = filtro.Estados.ToList();
            saneamientos = saneamientos.Where(s => estados.Contains(s.Estado));
        }

        if (filtro.Empresa is { } empresa)
            saneamientos = saneamientos.Where(s => s.TenantId == empresa);
        if (filtro.Desde is { } desde)
            saneamientos = saneamientos.Where(s => s.SolicitadoEn >= desde);
        if (filtro.Hasta is { } hasta)
            saneamientos = saneamientos.Where(s => s.SolicitadoEn < hasta);

        var vehiculos = db.Set<VehiculoDiagnostico>().IgnoreQueryFilters().AsNoTracking();
        if (filtro.Placa is { } placa)
            vehiculos = vehiculos.Where(v => v.IdentificadorNorm.StartsWith(placa));
        var cabeceras = db.Set<CabeceraDiagnostico>().IgnoreQueryFilters().AsNoTracking();
        var dictamenes = db.Set<DictamenVehiculo>().IgnoreQueryFilters().AsNoTracking().Where(d => d.Vigente);
        var renglones = db.Set<RenglonSaneamiento>().IgnoreQueryFilters().AsNoTracking().Where(r => r.QuitadoEn == null);

        return from s in saneamientos
               join v in vehiculos on new { s.TenantId, Id = s.VehiculoId } equals new { v.TenantId, v.Id }
               join c in cabeceras on new { s.TenantId, Id = s.DiagnosticoId } equals new { c.TenantId, c.Id }
               select new FilaLeida
               {
                   Id = s.Id,
                   Empresa = s.TenantId,
                   DiagnosticoId = s.DiagnosticoId,
                   DiagnosticoNombre = c.Nombre,
                   VehiculoId = s.VehiculoId,
                   Placa = v.Placa,
                   Vin = v.Vin,
                   Identificador = v.IdentificadorOriginal,
                   Modo = c.Modo,
                   Veredicto = dictamenes.Where(d => d.TenantId == s.TenantId && d.VehiculoId == s.VehiculoId).Select(d => d.Veredicto).FirstOrDefault(),
                   Estado = s.Estado,
                   SolicitadoEn = s.SolicitadoEn,
                   TomadoEn = s.TomadoEn,
                   CerradoEn = s.CerradoEn,
                   RenglonesTotal = renglones.Count(r => r.TenantId == s.TenantId && r.SaneamientoId == s.Id),
                   RenglonesPendientes = renglones.Count(r => r.TenantId == s.TenantId && r.SaneamientoId == s.Id && r.Resultado == ResultadoRenglonSaneamiento.Pendiente),
               };
    }

    /// <summary>Los saneamientos de todas las compañías: solo aquí (ADR-0087 D-5, <c>IgnoreQueryFiltersSoloEnBackofficeTests</c>).</summary>
    private IQueryable<SaneamientoEntidad> Saneamientos() => db.Set<SaneamientoEntidad>().IgnoreQueryFilters().AsNoTracking();

    /// <summary>Fila leída de la base (inicializador de miembros, para que EF ordene y pagine); el DTO se arma en memoria.</summary>
    private sealed class FilaLeida
    {
        public Guid Id { get; init; }
        public Guid Empresa { get; init; }
        public Guid DiagnosticoId { get; init; }
        public string DiagnosticoNombre { get; init; } = string.Empty;
        public Guid VehiculoId { get; init; }
        public string? Placa { get; init; }
        public string? Vin { get; init; }
        public string? Identificador { get; init; }
        public Domain.Diagnosticos.ModoDiagnostico Modo { get; init; }
        public string? Veredicto { get; init; }
        public EstadoSaneamiento Estado { get; init; }
        public DateTimeOffset SolicitadoEn { get; init; }
        public DateTimeOffset? TomadoEn { get; init; }
        public DateTimeOffset? CerradoEn { get; init; }
        public int RenglonesTotal { get; init; }
        public int RenglonesPendientes { get; init; }

        public FilaBandejaDto Dto() => new(
            Id, Empresa, DiagnosticoId, DiagnosticoNombre, VehiculoId, Placa, CodigosDeEnum.Codigo(Modo), Veredicto, CodigosDeEnum.Codigo(Estado),
            SolicitadoEn, TomadoEn, CerradoEn, RenglonesTotal, RenglonesPendientes, Vin, Identificador);
    }
}
