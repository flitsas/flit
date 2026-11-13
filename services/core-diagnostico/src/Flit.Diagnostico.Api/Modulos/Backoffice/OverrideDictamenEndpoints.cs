using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Lectura y reemplazo del dictamen vigente para el backoffice FLIT (corte 7). Cuelgan del grupo <c>/backoffice</c>
/// (<see cref="PoliticaBackoffice"/> de configuración; empresa explícita en <c>?empresa=</c>, y el GET queda auditado antes
/// de tocar la fila): el GET es el detalle y lo ve también el SuperAdmin de cualquier compañía. El <c>PUT</c> exige además la
/// política de operación FLIT (<see cref="PoliticaBackoffice.NombreOperacion"/>, ajustes 2026-11-11 punto 1) y
/// <c>diagnostico.dictamen.override</c>. La escritura de la otra compañía va por <see cref="EscrituraEntreEmpresas"/>.
/// No vuelve a evaluar, no redacta, no genera PDF y no toca el Excel ni el ZIP.
/// </summary>
internal static partial class OverrideDictamenEndpoints
{
    public const string Ruta = "/diagnosticos/{id:guid}/vehiculos/{vid:guid}/dictamen";

    /// <summary>Lo único que el override escribe en la compañía del lote.</summary>
    private static readonly Type[] TiposDelOverride = [typeof(DictamenVehiculo)];

    public static void Map(RouteGroupBuilder backoffice)
    {
        ArgumentNullException.ThrowIfNull(backoffice);
        backoffice.MapGet(Ruta, Obtener).WithName("ObtenerDictamenVehiculoBackoffice");
        backoffice.MapPut(Ruta, Reemplazar).RequiereOperacionFlit().RequirePermiso(Permisos.DictamenOverride)
            .WithName("CorregirDictamenVehiculoBackoffice");
    }

    private static async Task<IResult> Obtener(Guid id, Guid vid, HttpContext http, DiagnosticoDb db, CancellationToken ct)
    {
        var empresa = EmpresaExplicita.De(http);
        var fila = await VigenteAsync(db, empresa, id, vid, seguir: false, ct).ConfigureAwait(false);
        return Results.Ok(ConsultaDictamen.Backoffice(fila));
    }

    private static async Task<IResult> Reemplazar(
        Guid id, Guid vid, HttpContext http, JsonElement cuerpo, DiagnosticoDb db, UsuarioActual usuario, TimeProvider reloj,
        IPlatformOutbox outbox, IProveedorRedactor proveedor, CancellationToken ct)
    {
        var pedido = LeerCuerpo(cuerpo);
        var veredicto = pedido.Veredicto;
        var empresa = EmpresaExplicita.De(http);
        var autor = usuario.Id != Guid.Empty
            ? usuario.Id
            : throw ReglaException.Prohibido("SIN_USUARIO", "El token no identifica al usuario: el override no se puede auditar.");

        var dto = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var vigente = await VigenteAsync(db, empresa, id, vid, seguir: true, ct).ConfigureAwait(false);
            ExigirFrenteAVigente(pedido, vigente);
            var cabecera = await db.DeLaEmpresa<CabeceraDiagnostico>(empresa).SingleAsync(c => c.Id == id, ct).ConfigureAwait(false);
            var suspendida = await db.DeLaEmpresa<ConfiguracionEmpresa>(empresa)
                .Select(c => (bool?)c.RedaccionIaSuspendida).SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? false;
            var ahora = reloj.GetUtcNow();
            var fila = EmisorDictamen.FilaDeOverride(
                vigente, veredicto, confirmado: true, autor, cabecera.TipoTramiteCodigo, suspendida, proveedor.Configurado, ahora);
            fila.TenantId = empresa;
            fila.AjusteMotivo = pedido.Motivo;
            db.Add(fila);
            DictamenEmitido.Encolar(outbox, cabecera, fila, esOverride: true);
            try
            {
                await db.GuardarDeLaEmpresaAsync(empresa, TiposDelOverride, ct).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (EsConflictoDeDictamen(ex))
            {
                throw ReglaException.Conflicto(CodigosDictamen.DictamenEnConflicto, "Otro cambio de dictamen se guardó al mismo tiempo. Vuelve a leerlo.");
            }

            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            return ConsultaDictamen.Backoffice(fila);
        }).ConfigureAwait(false);
        return Results.Ok(dto);
    }

    /// <summary>
    /// <c>confirmado</c> distinto de true no sigue. Un veredicto que no es VIABLE ni BLOQUEADO tampoco. No escribe.
    /// </summary>
    internal static string ExigirCuerpo(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty("confirmado", out var confirmado)
            || confirmado.ValueKind != JsonValueKind.True)
            throw new ReglaException(CodigosDictamen.ConfirmadoRequerido, "Confirma el cambio de veredicto para reemplazarlo.");
        var veredicto = json.TryGetProperty("veredicto", out var valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;
        if (veredicto is not ("VIABLE" or "BLOQUEADO"))
            throw new ReglaException(CodigosDictamen.VeredictoInvalido, "El veredicto debe ser VIABLE o BLOQUEADO.");
        return veredicto;
    }

    /// <summary>
    /// Lee y valida el cuerpo del PUT (B7). Aquí, y solo aquí, se valida <c>motivo</c> (<c>ajuste_motivo</c>, <c>@pii:low</c>):
    /// recortado, 10 a 500 caracteres, sin filtro de dígitos (P3 = b). El motivo nunca se loguea ni viaja en el
    /// <c>ProblemDetails</c>: los mensajes de error de este método son textos fijos. El filtro de 6 o más dígitos aplica solo
    /// al texto de los alertamientos <c>AJUSTE_FLIT</c>, que ve la compañía. Un cuerpo nuevo (con <c>alertamientos</c> o
    /// <c>version_esperada</c>) exige motivo siempre (D9).
    /// </summary>
    internal static CuerpoAjusteDictamen LeerCuerpo(JsonElement json)
    {
        var veredicto = ExigirCuerpo(json);
        int? versionEsperada = null;
        if (json.TryGetProperty("version_esperada", out var version) && version.ValueKind != JsonValueKind.Null)
        {
            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v < 1)
                throw new ReglaException("VERSION_ESPERADA_INVALIDA", "version_esperada debe ser un entero mayor que cero.");
            versionEsperada = v;
        }

        string? motivo = null;
        if (json.TryGetProperty("motivo", out var valorMotivo) && valorMotivo.ValueKind != JsonValueKind.Null)
        {
            motivo = valorMotivo.ValueKind == JsonValueKind.String ? valorMotivo.GetString()?.Trim() : null;
            if (motivo is null || motivo.Length is < LargoMinimoMotivo or > LargoMaximoMotivo)
                throw MotivoRequerido();
        }

        var alertamientos = LeerAlertamientos(json);
        if (motivo is null && (alertamientos is not null || versionEsperada is not null))
            throw MotivoRequerido();

        var conTramite = json.TryGetProperty("confirmar_con_tramite", out var tramite) && tramite.ValueKind == JsonValueKind.True;
        return new CuerpoAjusteDictamen(veredicto, versionEsperada, motivo, conTramite, alertamientos);
    }

    /// <summary>
    /// Contra la vigente ya bloqueada: <c>version_esperada</c> distinta, 409 <c>DICTAMEN_DESACTUALIZADO</c>; cambio de
    /// veredicto sin motivo, 422 <c>MOTIVO_REQUERIDO</c> (D9). El cuerpo viejo sin motivo y con el mismo veredicto sigue
    /// a <c>VEREDICTO_SIN_CAMBIO</c> como hoy.
    /// </summary>
    internal static void ExigirFrenteAVigente(CuerpoAjusteDictamen pedido, DictamenVehiculo vigente)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        ArgumentNullException.ThrowIfNull(vigente);
        if (pedido.VersionEsperada is { } esperada && esperada != vigente.Version)
            throw ReglaException.Conflicto(CodigosDictamen.DictamenDesactualizado, "El dictamen cambió desde que lo leíste. Vuelve a leerlo.");
        if (pedido.Motivo is null && pedido.Veredicto != vigente.Veredicto)
            throw MotivoRequerido();
    }

    private const int LargoMinimoMotivo = 10;
    private const int LargoMaximoMotivo = 500;

    private static ReglaException MotivoRequerido() =>
        new(CodigosDictamen.MotivoRequerido, "motivo: escribe entre 10 y 500 caracteres que expliquen el ajuste.");

    private static List<AlertamientoAjuste>? LeerAlertamientos(JsonElement json)
    {
        if (!json.TryGetProperty("alertamientos", out var lista) || lista.ValueKind == JsonValueKind.Null)
            return null;
        if (lista.ValueKind != JsonValueKind.Array)
            throw new ReglaException("ALERTAMIENTOS_INVALIDOS", "alertamientos debe ser una lista.");
        var leidos = new List<AlertamientoAjuste>();
        foreach (var item in lista.EnumerateArray())
        {
            var alerta = new AlertamientoAjuste(
                Campo(item, "codigo") ?? string.Empty, Campo(item, "texto") ?? string.Empty, Campo(item, "efecto") ?? string.Empty, Campo(item, "fuente"));
            if (alerta.Codigo == CodigosDictamen.AjusteFlit && SeisOMasDigitos().IsMatch(alerta.Texto))
                throw new ReglaException(CodigosDictamen.AlertamientoConDigitos, "El texto de un alertamiento AJUSTE_FLIT no puede traer 6 o más dígitos seguidos.");
            leidos.Add(alerta);
        }

        return leidos;
    }

    private static string? Campo(JsonElement item, string campo) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    [GeneratedRegex(@"\d{6,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SeisOMasDigitos();

    /// <summary>
    /// Choque de dos overrides en <c>ux_dictamen_version</c> o <c>ux_dictamen_vigente</c>, o el <c>xmin</c> de la vigente
    /// (<see cref="DbUpdateConcurrencyException"/>): siempre 409, nunca 500.
    /// </summary>
    internal static bool EsConflictoDeDictamen(DbUpdateException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        if (ex is DbUpdateConcurrencyException)
            return true;
        if (ex.InnerException is not PostgresException pg || pg.SqlState != PostgresErrorCodes.UniqueViolation)
            return false;
        if (pg.ConstraintName is "ux_dictamen_version" or "ux_dictamen_vigente")
            return true;
        return pg.MessageText.Contains("ux_dictamen_version", StringComparison.Ordinal)
            || pg.MessageText.Contains("ux_dictamen_vigente", StringComparison.Ordinal);
    }

    private static async Task<DictamenVehiculo> VigenteAsync(DiagnosticoDb db, Guid empresa, Guid id, Guid vid, bool seguir, CancellationToken ct)
    {
        if (!await db.DeLaEmpresa<CabeceraDiagnostico>(empresa).AnyAsync(c => c.Id == id, ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("El diagnóstico");
        if (!await db.DeLaEmpresa<VehiculoDiagnostico>(empresa).AnyAsync(v => v.Id == vid && v.DiagnosticoId == id, ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("El vehículo");

        var consulta = seguir
            ? db.ParaEditarDeLaEmpresa<DictamenVehiculo>(empresa)
            : db.DeLaEmpresa<DictamenVehiculo>(empresa);
        return await consulta.SingleOrDefaultAsync(d => d.VehiculoId == vid && d.Vigente, ct).ConfigureAwait(false) ?? throw ConsultaDictamen.SinFila();
    }
}

/// <summary>Cuerpo del PUT del dictamen ya validado (B7). <see cref="Motivo"/> es <c>@pii:low</c>: no se loguea.</summary>
internal sealed record CuerpoAjusteDictamen(
    string Veredicto, int? VersionEsperada, string? Motivo, bool ConfirmarConTramite, IReadOnlyList<AlertamientoAjuste>? Alertamientos);

/// <summary>Alertamiento pedido en el ajuste FLIT (B7).</summary>
internal sealed record AlertamientoAjuste(string Codigo, string Texto, string Efecto, string? Fuente);
