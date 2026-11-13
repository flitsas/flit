using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Consultas;
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
/// Lectura y ajuste FLIT del dictamen vigente para el backoffice (corte 7; HU #14563 B7, ADR-0086 D-2). Cuelgan del grupo
/// <c>/backoffice</c> (<see cref="PoliticaBackoffice"/> de configuración; empresa explícita en <c>?empresa=</c>, y el GET queda
/// auditado antes de tocar la fila): el GET es el detalle y lo ve también el SuperAdmin de cualquier compañía. El <c>PUT</c> exige
/// además la política de operación FLIT (<see cref="PoliticaBackoffice.NombreOperacion"/>) y <c>diagnostico.dictamen.override</c>.
/// La escritura de la otra compañía va por <see cref="EscrituraEntreEmpresas"/>.
/// <para>
/// El PUT recibe el estado deseado: veredicto, motivo (obligatorio desde B-PR6), <c>version_esperada</c> y, opcional, los
/// alertamientos completos. Sin alertamientos reemplaza solo el veredicto (textos de plantilla, la cola los redacta); con ellos
/// nace una versión <c>MANUAL</c>/<c>COMPLETADA</c> (<see cref="EmisorDictamen.FilaDeAjuste"/>). Orden de bloqueo: cabecera →
/// vehículo (<see cref="EscritorCeldas.SubirVersionFilaAsync(DiagnosticoDb, IQueryable{CabeceraDiagnostico}, IQueryable{VehiculoDiagnostico}, Guid, Guid, CancellationToken)"/>)
/// → dictamen. Toda carrera termina en 409, nunca en 500. No notifica (D8), no genera PDF y no toca el Excel ni el ZIP.
/// </para>
/// <para>
/// <c>motivo</c> (<c>ajuste_motivo</c>, <c>@pii:low</c>) se valida solo en <see cref="LeerCuerpo"/> y se escribe solo en
/// <see cref="DictamenVehiculo.AjusteMotivo"/>. Este archivo no tiene logger y ningún mensaje de error repite lo digitado.
/// </para>
/// </summary>
internal static partial class OverrideDictamenEndpoints
{
    public const string Ruta = "/diagnosticos/{id:guid}/vehiculos/{vid:guid}/dictamen";

    /// <summary>Lo único que el ajuste escribe con <c>SaveChanges</c> en la compañía del lote (la versión de fila va por ExecuteUpdate).</summary>
    private static readonly Type[] TiposDelOverride = [typeof(DictamenVehiculo)];

    private const int LargoMinimoMotivo = 10;
    private const int LargoMaximoMotivo = 500;
    private const int LargoMaximoTexto = 500;
    private const int MaximoAlertamientos = 50;

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
        await ExigirLoteYVehiculoAsync(db, empresa, id, vid, ct).ConfigureAwait(false);
        var fila = await VigenteAsync(db, empresa, vid, seguir: false, ct).ConfigureAwait(false);
        return Results.Ok(ConsultaDictamen.Backoffice(fila));
    }

    private static async Task<IResult> Reemplazar(
        Guid id, Guid vid, HttpContext http, JsonElement cuerpo, DiagnosticoDb db, UsuarioActual usuario, TimeProvider reloj,
        IPlatformOutbox outbox, IProveedorRedactor proveedor, CancellationToken ct)
    {
        var pedido = LeerCuerpo(cuerpo);
        var empresa = EmpresaExplicita.De(http);
        var autor = usuario.Id != Guid.Empty
            ? usuario.Id
            : throw ReglaException.Prohibido("SIN_USUARIO", "El token no identifica al usuario: el override no se puede auditar.");

        var dto = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await ExigirLoteYVehiculoAsync(db, empresa, id, vid, ct).ConfigureAwait(false);

            // Orden de bloqueo canónico (ADR-0086 D-1): cabecera → vehículo con la versión de fila, y después el dictamen. Un PUT
            // concurrente espera aquí y, al seguir, lee la vigente nueva: su version_esperada ya no coincide (409).
            await EscritorCeldas.SubirVersionFilaAsync(
                db, db.ParaEditarDeLaEmpresa<CabeceraDiagnostico>(empresa), db.ParaEditarDeLaEmpresa<VehiculoDiagnostico>(empresa), id, vid, ct)
                .ConfigureAwait(false);
            var tramite = await db.DeLaEmpresa<VehiculoDiagnostico>(empresa).Where(v => v.Id == vid)
                .Select(v => v.TramiteId).SingleAsync(ct).ConfigureAwait(false);
            var vigente = await VigenteAsync(db, empresa, vid, seguir: true, ct).ConfigureAwait(false);
            ExigirFrenteAVigente(pedido, vigente);
            if (tramite is not null && !pedido.ConfirmarConTramite)
                throw ReglaException.Conflicto(
                    CodigosDictamen.VehiculoConTramite, "El vehículo ya tiene un trámite creado; el ajuste no lo modifica. Confírmalo para continuar.");

            var cabecera = await db.DeLaEmpresa<CabeceraDiagnostico>(empresa).SingleAsync(c => c.Id == id, ct).ConfigureAwait(false);
            var ahora = reloj.GetUtcNow();
            DictamenVehiculo fila;
            if (pedido.Alertamientos is { } alertamientos)
            {
                fila = EmisorDictamen.FilaDeAjuste(vigente, pedido.Veredicto, alertamientos, pedido.Motivo, autor, ahora);
            }
            else
            {
                var suspendida = await db.DeLaEmpresa<ConfiguracionEmpresa>(empresa)
                    .Select(c => (bool?)c.RedaccionIaSuspendida).SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? false;
                fila = EmisorDictamen.FilaDeOverride(
                    vigente, pedido.Veredicto, confirmado: true, autor, cabecera.TipoTramiteCodigo, suspendida, proveedor.Configurado, ahora);
                fila.AjusteMotivo = pedido.Motivo;
            }

            fila.TenantId = empresa;
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
    /// <c>confirmado</c>, si viene, debe ser true. Un veredicto que no es VIABLE ni BLOQUEADO tampoco sigue. No escribe.
    /// </summary>
    internal static string ExigirCuerpo(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object
            || (json.TryGetProperty("confirmado", out var confirmado) && confirmado.ValueKind != JsonValueKind.True))
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
    /// obligatorio en todo PUT (D9 + B-PR6: el cuerpo legado sin motivo se retiró), recortado, 10 a 500 caracteres, sin filtro de
    /// dígitos (P3 = b). Los mensajes de error son textos fijos: nunca repiten el motivo ni el texto digitado. Los alertamientos,
    /// si vienen, exigen <c>version_esperada</c>; cada uno con código, texto de 1 a 500, efecto <c>BLOQUEA</c>/<c>ADVIERTE</c> y
    /// fuente; un <c>AJUSTE_FLIT</c> (lo ve la compañía) no admite 6 o más dígitos seguidos (P3). Con alertamientos, el veredicto
    /// debe ser coherente: <c>VIABLE</c> sin ninguno que bloquee y <c>BLOQUEADO</c> con al menos uno.
    /// </summary>
    internal static CuerpoAjusteDictamen LeerCuerpo(JsonElement json)
    {
        var veredicto = ExigirCuerpo(json);
        int? versionEsperada = null;
        if (json.TryGetProperty("version_esperada", out var version) && version.ValueKind != JsonValueKind.Null)
        {
            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v < 1)
                throw new ReglaException(CodigosDictamen.VersionEsperadaInvalida, "version_esperada debe ser un entero mayor que cero.") { Campo = "version_esperada" };
            versionEsperada = v;
        }

        var motivo = json.TryGetProperty("motivo", out var valorMotivo) && valorMotivo.ValueKind == JsonValueKind.String
            ? valorMotivo.GetString()?.Trim()
            : null;
        if (motivo is null || motivo.Length is < LargoMinimoMotivo or > LargoMaximoMotivo)
            throw new ReglaException(CodigosDictamen.MotivoRequerido, "motivo: escribe entre 10 y 500 caracteres que expliquen el ajuste.") { Campo = "motivo" };

        var alertamientos = LeerAlertamientos(json);
        if (alertamientos is not null)
        {
            if (versionEsperada is null)
                throw new ReglaException(CodigosDictamen.VersionEsperadaInvalida, "Un ajuste de alertamientos exige version_esperada.") { Campo = "version_esperada" };
            var bloquea = alertamientos.Exists(a => a.Efecto == "BLOQUEA");
            if ((veredicto == "VIABLE" && bloquea) || (veredicto == "BLOQUEADO" && !bloquea))
                throw new ReglaException(
                    CodigosDictamen.DictamenIncoherente,
                    "Un dictamen Viable no puede tener alertamientos que bloqueen, y uno Bloqueado necesita al menos uno.")
                { Campo = "alertamientos" };
        }

        var conTramite = json.TryGetProperty("confirmar_con_tramite", out var tramite) && tramite.ValueKind == JsonValueKind.True;
        return new CuerpoAjusteDictamen(veredicto, versionEsperada, motivo, conTramite, alertamientos);
    }

    /// <summary>
    /// Contra la vigente ya bloqueada: <c>version_esperada</c> distinta, 409 <c>DICTAMEN_DESACTUALIZADO</c>. Cada alertamiento pedido
    /// usa un código del catálogo (<see cref="RotulosDiagnostico.Alertas"/>), <c>AJUSTE_FLIT</c> o uno que ya estaba en la vigente,
    /// y una fuente conocida (<see cref="RotulosDiagnostico.Fuentes"/>) o de la vigente; si no, 422 <c>ALERTAMIENTO_INVALIDO</c>.
    /// </summary>
    internal static void ExigirFrenteAVigente(CuerpoAjusteDictamen pedido, DictamenVehiculo vigente)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        ArgumentNullException.ThrowIfNull(vigente);
        if (pedido.VersionEsperada is { } esperada && esperada != vigente.Version)
            throw ReglaException.Conflicto(CodigosDictamen.DictamenDesactualizado, "El dictamen cambió desde que lo leíste. Vuelve a leerlo.");
        if (pedido.Alertamientos is null)
            return;

        var guardadas = EmisorDictamen.AlertasGuardadas(vigente.Alertamientos);
        foreach (var alerta in pedido.Alertamientos)
        {
            var codigoValido = alerta.Codigo == CodigosDictamen.AjusteFlit
                || RotulosDiagnostico.Alertas.ContainsKey(alerta.Codigo)
                || guardadas.Exists(g => g.Codigo == alerta.Codigo);
            var fuenteValida = alerta.Fuente is { } fuente
                && (RotulosDiagnostico.Fuentes.ContainsKey(fuente) || guardadas.Exists(g => g.Fuente == fuente));
            if (!codigoValido || !fuenteValida)
                throw AlertamientoInvalido("Un alertamiento usa un código o una fuente fuera del catálogo.");
        }
    }

    private static ReglaException AlertamientoInvalido(string mensaje) =>
        new(CodigosDictamen.AlertamientoInvalido, mensaje) { Campo = "alertamientos" };

    private static List<AlertamientoAjuste>? LeerAlertamientos(JsonElement json)
    {
        if (!json.TryGetProperty("alertamientos", out var lista) || lista.ValueKind == JsonValueKind.Null)
            return null;
        if (lista.ValueKind != JsonValueKind.Array || lista.GetArrayLength() > MaximoAlertamientos)
            throw AlertamientoInvalido($"alertamientos debe ser una lista de hasta {MaximoAlertamientos} elementos.");
        var leidos = new List<AlertamientoAjuste>();
        foreach (var item in lista.EnumerateArray())
        {
            var codigo = Campo(item, "codigo");
            var texto = Campo(item, "texto")?.Trim();
            var efecto = Campo(item, "efecto");
            var fuente = Campo(item, "fuente");
            if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrEmpty(texto) || texto.Length > LargoMaximoTexto
                || efecto is not ("BLOQUEA" or "ADVIERTE") || string.IsNullOrWhiteSpace(fuente))
                throw AlertamientoInvalido("Cada alertamiento lleva código, texto de 1 a 500 caracteres, efecto BLOQUEA o ADVIERTE y fuente.");
            if (codigo == CodigosDictamen.AjusteFlit && SeisOMasDigitos().IsMatch(texto))
                throw new ReglaException(CodigosDictamen.AlertamientoConDigitos, "El texto de un alertamiento AJUSTE_FLIT no puede traer 6 o más dígitos seguidos.")
                {
                    Campo = "alertamientos",
                };
            leidos.Add(new AlertamientoAjuste(codigo, texto, efecto, fuente));
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
    /// Choque de dos ajustes en <c>ux_dictamen_version</c> o <c>ux_dictamen_vigente</c>, o el <c>xmin</c> de la vigente
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

    /// <summary>404 <c>NO_ENCONTRADO</c> si el lote o el vehículo no son de <paramref name="empresa"/>. Lecturas sin bloqueo.</summary>
    internal static async Task ExigirLoteYVehiculoAsync(DiagnosticoDb db, Guid empresa, Guid id, Guid vid, CancellationToken ct)
    {
        if (!await db.DeLaEmpresa<CabeceraDiagnostico>(empresa).AnyAsync(c => c.Id == id, ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("El diagnóstico");
        if (!await db.DeLaEmpresa<VehiculoDiagnostico>(empresa).AnyAsync(v => v.Id == vid && v.DiagnosticoId == id, ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("El vehículo");
    }

    private static async Task<DictamenVehiculo> VigenteAsync(DiagnosticoDb db, Guid empresa, Guid vid, bool seguir, CancellationToken ct)
    {
        var consulta = seguir
            ? db.ParaEditarDeLaEmpresa<DictamenVehiculo>(empresa)
            : db.DeLaEmpresa<DictamenVehiculo>(empresa);
        return await consulta.SingleOrDefaultAsync(d => d.VehiculoId == vid && d.Vigente, ct).ConfigureAwait(false) ?? throw ConsultaDictamen.SinFila();
    }
}

/// <summary>
/// Cuerpo del PUT del dictamen ya validado (B7). <see cref="Motivo"/> es <c>@pii:low</c>: no se loguea ni viaja a la compañía,
/// a eventos ni a errores. <see cref="Alertamientos"/> null = solo veredicto.
/// </summary>
internal sealed record CuerpoAjusteDictamen(
    string Veredicto, int? VersionEsperada, string Motivo, bool ConfirmarConTramite, IReadOnlyList<AlertamientoAjuste>? Alertamientos);
