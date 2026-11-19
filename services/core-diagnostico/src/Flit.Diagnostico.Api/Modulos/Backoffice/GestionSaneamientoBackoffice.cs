using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Saneamiento;
using Microsoft.EntityFrameworkCore;
using SaneamientoEntidad = Flit.Diagnostico.Api.Modulos.Saneamiento.Saneamiento;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Gestión FLIT de un saneamiento de otra compañía (plan §6.2 grupo B y «tomar varios»; HU #14576 AC9–AC24, AC27, AC43, AC44, AC48–AC50;
/// ADR-0087 D-5; SP-1, SP-3, SP-6, SP-13 con E3, SP-22). Toda escritura:
/// <list type="number">
/// <item>valida el cuerpo (422 <see cref="CodigoVersionInvalida"/>, <c>VALOR_INVALIDO</c> o <c>TEXTO_INVALIDO</c> con campo y mensaje fijo);</item>
/// <item>abre una transacción, lee la cadena empresa → saneamiento → renglón (404 sin distinguir el eslabón, SP-6) y bloquea la cabecera y
/// la fila del vehículo (<see cref="EscritorCeldas.SubirVersionFilaAsync"/> con las filas de la empresa), y solo entonces relee el
/// saneamiento con seguimiento: dos operadores quedan en fila;</item>
/// <item>exige EN_GESTION en las ediciones (409 <see cref="CodigoNoEditable"/>) y la <c>version</c> vigente (409
/// <see cref="CodigoVersionDesactualizada"/>; un <see cref="DbUpdateConcurrencyException"/> también, brief C6);</item>
/// <item>escribe por <see cref="EscritorSaneamiento"/>, suma los avisos agrupados y guarda con
/// <see cref="EscrituraEntreEmpresas.GuardarDeLaEmpresaAsync"/> y la lista cerrada <see cref="EscritorSaneamiento.TiposDelSaneamiento"/>.</item>
/// </list>
/// Nunca toca el dictamen (AC24). Logs: ids, estado y evento; nunca placa ni textos libres.
/// </summary>
internal sealed partial class GestionSaneamientoBackoffice(
    DiagnosticoDb db, UsuarioActual usuario, TimeProvider reloj, LecturasVehiculo lecturas, ILogger<GestionSaneamientoBackoffice> logger)
{
    public const string CodigoNoEditable = "NO_EDITABLE";
    public const string CodigoVersionDesactualizada = "VERSION_DESACTUALIZADA";
    public const string CodigoVersionInvalida = "VERSION_INVALIDA";
    public const string CodigoListaInvalida = "LISTA_INVALIDA";
    public const string CodigoCuerpoInvalido = "CUERPO_INVALIDO";
    public const string MotivoNoEncontrado = "NO_ENCONTRADO";

    /// <summary>Tope de «tomar varios» (§6.2), sobre los pares distintos.</summary>
    public const int MaximoTomar = 200;

    private static readonly Type[] Tipos = [.. EscritorSaneamiento.TiposDelSaneamiento];

    /// <summary>GET: el saneamiento de la empresa con su historial completo; 404 si no es de esa empresa.</summary>
    public async Task<SaneamientoBackofficeDto> DetalleAsync(Guid empresa, Guid sid, CancellationToken ct)
    {
        var saneamiento = await db.DeLaEmpresa<SaneamientoEntidad>(empresa).SingleOrDefaultAsync(s => s.Id == sid, ct).ConfigureAwait(false)
            ?? throw NoEncontrado();
        var cabecera = await db.DeLaEmpresa<CabeceraDiagnostico>(empresa).Where(c => c.Id == saneamiento.DiagnosticoId)
            .Select(c => c.Nombre).SingleAsync(ct).ConfigureAwait(false);
        var placa = await db.DeLaEmpresa<VehiculoDiagnostico>(empresa).Where(v => v.Id == saneamiento.VehiculoId)
            .Select(v => v.Placa ?? v.IdentificadorNorm).SingleAsync(ct).ConfigureAwait(false);
        var veredicto = await db.DeLaEmpresa<DictamenVehiculo>(empresa).Where(d => d.VehiculoId == saneamiento.VehiculoId && d.Vigente)
            .Select(d => d.Veredicto).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var renglones = await db.DeLaEmpresa<RenglonSaneamiento>(empresa)
            .Where(r => r.SaneamientoId == sid && r.QuitadoEn == null)
            .OrderBy(r => r.CreadoEn).ThenBy(r => r.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        var historial = await db.DeLaEmpresa<EventoSaneamiento>(empresa)
            .Where(e => e.SaneamientoId == sid)
            .OrderBy(e => e.OcurridoEn).ThenBy(e => e.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return new SaneamientoBackofficeDto(
            saneamiento.Id, saneamiento.Xmin, empresa, saneamiento.DiagnosticoId, cabecera, saneamiento.VehiculoId, placa, veredicto,
            CodigosDeEnum.Codigo(saneamiento.Estado), saneamiento.SolicitadoEn, saneamiento.TomadoEn, saneamiento.CerradoEn, saneamiento.MotivoCierre,
            saneamiento.MotivoAnulacion, saneamiento.ObservacionFlit,
            [.. renglones.Select(ProyeccionSaneamiento.Renglon)], [.. historial.Select(ProyeccionSaneamiento.Evento)]);
    }

    /// <summary>Un renglón no quitado del saneamiento, para la respuesta de su PUT o de la evidencia.</summary>
    public async Task<RenglonClienteDto> RenglonDtoAsync(Guid empresa, Guid sid, Guid rid, CancellationToken ct) =>
        ProyeccionSaneamiento.Renglon(await db.DeLaEmpresa<RenglonSaneamiento>(empresa)
            .SingleOrDefaultAsync(r => r.Id == rid && r.SaneamientoId == sid && r.QuitadoEn == null, ct).ConfigureAwait(false) ?? throw NoEncontrado());

    public Task<SaneamientoBackofficeDto> TomarAsync(Guid empresa, Guid sid, CancellationToken ct) =>
        TransicionAsync(empresa, sid, AccionSaneamiento.Tomar, version: null, motivo: null, ct);

    public async Task<SaneamientoBackofficeDto> AnularAsync(Guid empresa, Guid sid, HttpRequest request, CancellationToken ct)
    {
        var cuerpo = await CuerpoSaneamiento.LeerAsync(request, ct).ConfigureAwait(false);
        return await TransicionAsync(empresa, sid, AccionSaneamiento.AnularFlit, CuerpoSaneamiento.Version(cuerpo), CuerpoSaneamiento.Cadena(cuerpo, "motivo"), ct)
            .ConfigureAwait(false);
    }

    public async Task<SaneamientoBackofficeDto> ReabrirAsync(Guid empresa, Guid sid, HttpRequest request, CancellationToken ct)
    {
        var cuerpo = await CuerpoSaneamiento.LeerAsync(request, ct).ConfigureAwait(false);
        return await TransicionAsync(empresa, sid, AccionSaneamiento.Reabrir, CuerpoSaneamiento.Version(cuerpo), CuerpoSaneamiento.Cadena(cuerpo, "motivo"), ct)
            .ConfigureAwait(false);
    }

    /// <summary>Cierre: <c>{"resultado": "SANEADO" | "NO_PROCEDE", "motivo"?, "version"}</c>.</summary>
    public async Task<SaneamientoBackofficeDto> CerrarAsync(Guid empresa, Guid sid, HttpRequest request, CancellationToken ct)
    {
        var cuerpo = await CuerpoSaneamiento.LeerAsync(request, ct).ConfigureAwait(false);
        var accion = CuerpoSaneamiento.Cadena(cuerpo, "resultado") switch
        {
            "SANEADO" => AccionSaneamiento.CerrarSaneado,
            "NO_PROCEDE" => AccionSaneamiento.CerrarNoProcede,
            _ => throw CuerpoSaneamiento.ValorInvalido("resultado", "resultado es SANEADO o NO_PROCEDE."),
        };
        var version = CuerpoSaneamiento.Version(cuerpo);
        var motivo = accion == AccionSaneamiento.CerrarNoProcede ? CuerpoSaneamiento.Cadena(cuerpo, "motivo") : null;
        return await TransicionAsync(empresa, sid, accion, version, motivo, ct).ConfigureAwait(false);
    }

    /// <summary>Observación general: <c>{"observacion": string | null, "version"}</c>, 0 a 1000 caracteres; vacía se guarda null.</summary>
    public async Task<SaneamientoBackofficeDto> ObservacionAsync(Guid empresa, Guid sid, HttpRequest request, CancellationToken ct)
    {
        var cuerpo = await CuerpoSaneamiento.LeerAsync(request, ct).ConfigureAwait(false);
        var version = CuerpoSaneamiento.Version(cuerpo);
        var texto = CuerpoSaneamiento.TextoOpcional(cuerpo, "observacion", 0, 1000);
        await EscribirAsync(empresa, sid, (s, ahora) =>
        {
            ExigirEditable(s);
            ExigirVersion(s.Xmin, version);
            s.ObservacionFlit = string.IsNullOrEmpty(texto) ? null : texto;
            EscritorSaneamiento.RegistrarEdicion(db, s, TipoEventoSaneamiento.ObservacionEditada, usuario.Id, ahora);
            LogEdicion(logger, s.Id, null, nameof(TipoEventoSaneamiento.ObservacionEditada));
            return Task.CompletedTask;
        }, ct).ConfigureAwait(false);
        return await DetalleAsync(empresa, sid, ct).ConfigureAwait(false);
    }

    /// <summary>POST: agrega un renglón OTRO con <c>{"descripcion"}</c> de 3 a 200; devuelve el saneamiento completo (D-65).</summary>
    public async Task<SaneamientoBackofficeDto> AgregarRenglonAsync(Guid empresa, Guid sid, HttpRequest request, CancellationToken ct)
    {
        var cuerpo = await CuerpoSaneamiento.LeerAsync(request, ct).ConfigureAwait(false);
        var descripcion = CuerpoSaneamiento.TextoOpcional(cuerpo, "descripcion", 3, 200)
            ?? throw CuerpoSaneamiento.ValorInvalido("descripcion", "descripcion es obligatoria, de 3 a 200 caracteres.");
        await EscribirAsync(empresa, sid, (s, ahora) =>
        {
            ExigirEditable(s);
            var renglon = new RenglonSaneamiento
            {
                Id = Guid.CreateVersion7(ahora),
                TenantId = s.TenantId,
                SaneamientoId = s.Id,
                Fuente = FuenteSaneamiento.Otro,
                Descripcion = descripcion,
                Automatico = false,
                Resultado = ResultadoRenglonSaneamiento.Pendiente,
                CreadoPor = usuario.Id,
                CreadoEn = ahora,
            };
            db.Add(renglon);
            EscritorSaneamiento.RegistrarEdicion(db, s, TipoEventoSaneamiento.RenglonAgregado, usuario.Id, ahora, renglon);
            LogEdicion(logger, s.Id, renglon.Id, nameof(TipoEventoSaneamiento.RenglonAgregado));
            return Task.CompletedTask;
        }, ct).ConfigureAwait(false);
        return await DetalleAsync(empresa, sid, ct).ConfigureAwait(false);
    }

    /// <summary>PUT renglón: <c>{"resultado", "nota" | null, "version"}</c> con la version del renglón; devuelve el renglón.</summary>
    public async Task<RenglonClienteDto> EditarRenglonAsync(Guid empresa, Guid sid, Guid rid, HttpRequest request, CancellationToken ct)
    {
        var cuerpo = await CuerpoSaneamiento.LeerAsync(request, ct).ConfigureAwait(false);
        if (!CodigosDeEnum.TryLeer<ResultadoRenglonSaneamiento>(CuerpoSaneamiento.Cadena(cuerpo, "resultado"), out var resultado))
            throw CuerpoSaneamiento.ValorInvalido("resultado", "resultado es PENDIENTE, SANEADO o NO_PROCEDE.");
        var nota = CuerpoSaneamiento.TextoOpcional(cuerpo, "nota", 0, 500);
        var version = CuerpoSaneamiento.Version(cuerpo);
        await EscribirAsync(empresa, sid, async (s, ahora) =>
        {
            var renglon = await RenglonParaEditarAsync(empresa, sid, rid, ct).ConfigureAwait(false);
            ExigirEditable(s);
            ExigirVersion(renglon.Xmin, version);
            renglon.Resultado = resultado;
            renglon.Nota = string.IsNullOrEmpty(nota) ? null : nota;
            renglon.ActualizadoPor = usuario.Id;
            renglon.ActualizadoEn = ahora;
            EscritorSaneamiento.RegistrarEdicion(db, s, TipoEventoSaneamiento.RenglonActualizado, usuario.Id, ahora, renglon);
            LogEdicion(logger, s.Id, renglon.Id, nameof(TipoEventoSaneamiento.RenglonActualizado));
        }, ct).ConfigureAwait(false);
        return await RenglonDtoAsync(empresa, sid, rid, ct).ConfigureAwait(false);
    }

    /// <summary>DELETE renglón: borrado lógico (SP-2), queda en el historial; devuelve el saneamiento completo (D-65).</summary>
    public async Task<SaneamientoBackofficeDto> QuitarRenglonAsync(Guid empresa, Guid sid, Guid rid, CancellationToken ct)
    {
        await EscribirAsync(empresa, sid, async (s, ahora) =>
        {
            var renglon = await RenglonParaEditarAsync(empresa, sid, rid, ct).ConfigureAwait(false);
            ExigirEditable(s);
            renglon.QuitadoEn = ahora;
            renglon.ActualizadoPor = usuario.Id;
            renglon.ActualizadoEn = ahora;
            EscritorSaneamiento.RegistrarEdicion(db, s, TipoEventoSaneamiento.RenglonQuitado, usuario.Id, ahora, renglon);
            LogEdicion(logger, s.Id, renglon.Id, nameof(TipoEventoSaneamiento.RenglonQuitado));
        }, ct).ConfigureAwait(false);
        return await DetalleAsync(empresa, sid, ct).ConfigureAwait(false);
    }

    /// <summary>DELETE evidencia: las seis columnas quedan nulas (el archivo queda huérfano, ADR-0087); devuelve el saneamiento (D-65).</summary>
    public async Task<SaneamientoBackofficeDto> QuitarEvidenciaAsync(Guid empresa, Guid sid, Guid rid, CancellationToken ct)
    {
        await EscribirAsync(empresa, sid, async (s, ahora) =>
        {
            var renglon = await RenglonParaEditarAsync(empresa, sid, rid, ct).ConfigureAwait(false);
            ExigirEditable(s);
            if (renglon.EvidenciaReferencia is null)
                throw NoEncontrado();
            renglon.EvidenciaReferencia = null;
            renglon.EvidenciaNombre = null;
            renglon.EvidenciaSha256 = null;
            renglon.EvidenciaTamano = null;
            renglon.EvidenciaSubidaPor = null;
            renglon.EvidenciaSubidaEn = null;
            renglon.ActualizadoPor = usuario.Id;
            renglon.ActualizadoEn = ahora;
            EscritorSaneamiento.RegistrarEdicion(db, s, TipoEventoSaneamiento.EvidenciaQuitada, usuario.Id, ahora, renglon);
            LogEdicion(logger, s.Id, renglon.Id, nameof(TipoEventoSaneamiento.EvidenciaQuitada));
        }, ct).ConfigureAwait(false);
        return await DetalleAsync(empresa, sid, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// «Tomar varios» (grupo A, excepción a ADR-0073 de SP-1): <c>{"items": [{"empresa", "id"}]}</c>, 1 a <see cref="MaximoTomar"/> pares
    /// distintos (los repetidos no cuentan); una empresa que no es un uuid o es cero: 400 <see cref="CodigoListaInvalida"/>. Por empresa,
    /// en secuencia y en una sola transacción: lee los ids con <c>DeLaEmpresa</c> (el par que no coincide es <c>NO_ENCONTRADO</c>, igual que
    /// el inexistente), bloquea en orden de <c>diagnostico_id</c>, toma los SOLICITADO con sus renglones y guarda en el ámbito de esa
    /// empresa. Los que no están SOLICITADO salen como <c>TRANSICION_INVALIDA</c>.
    /// </summary>
    public async Task<ResultadoTomarDto> TomarVariosAsync(HttpRequest request, CancellationToken ct)
    {
        var items = await LeerItemsAsync(request, ct).ConfigureAwait(false);
        var autor = ExigirUsuario();
        var omitidos = new Dictionary<(Guid Empresa, Guid Id), string>();
        var tomados = 0;
        try
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                omitidos.Clear();
                tomados = 0;
                await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                var ahora = reloj.GetUtcNow();
                foreach (var grupo in items.GroupBy(i => i.Empresa).OrderBy(g => g.Key))
                {
                    var empresa = grupo.Key;
                    var ids = grupo.Select(i => i.Id).ToList();
                    var encontrados = await db.DeLaEmpresa<SaneamientoEntidad>(empresa).Where(s => ids.Contains(s.Id))
                        .Select(s => new { s.Id, s.DiagnosticoId, s.VehiculoId, s.Estado }).ToListAsync(ct).ConfigureAwait(false);
                    foreach (var id in ids.Except(encontrados.Select(e => e.Id)))
                        omitidos[(empresa, id)] = MotivoNoEncontrado;
                    var aplicadas = new List<TransicionAplicada>();
                    foreach (var e in encontrados.OrderBy(x => x.DiagnosticoId).ThenBy(x => x.Id))
                    {
                        // CR-12: el que ya no se puede tomar sale omitido sin bloquear ni marcar su fila (sin escritura ni sondeo de más).
                        // Si cambió entre esta lectura y el bloqueo, la relectura de abajo lo omite igual.
                        if (TransicionesSaneamiento.Resolver(e.Estado, AccionSaneamiento.Tomar, ActorSaneamiento.Flit) is null)
                        {
                            omitidos[(empresa, e.Id)] = EscritorSaneamiento.CodigoTransicionInvalida;
                            continue;
                        }

                        await BloquearAsync(empresa, e.DiagnosticoId, e.VehiculoId, ct).ConfigureAwait(false);
                        var s = await db.ParaEditarDeLaEmpresa<SaneamientoEntidad>(empresa).SingleAsync(x => x.Id == e.Id, ct).ConfigureAwait(false);
                        if (TransicionesSaneamiento.Resolver(s.Estado, AccionSaneamiento.Tomar, ActorSaneamiento.Flit) is null)
                        {
                            omitidos[(empresa, e.Id)] = EscritorSaneamiento.CodigoTransicionInvalida;
                            continue;
                        }

                        aplicadas.Add(await TransicionarAsync(empresa, s, AccionSaneamiento.Tomar, autor, null, ahora, ct).ConfigureAwait(false));
                    }

                    await AvisosSaneamiento.AgregarAsync(db, aplicadas, ahora, ct).ConfigureAwait(false);
                    await db.GuardarDeLaEmpresaAsync(empresa, Tipos, ct).ConfigureAwait(false);
                    tomados += aplicadas.Count;
                }

                await transaccion.CommitAsync(ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw VersionDesactualizada();
        }

        LogTomarVarios(logger, items.Count, tomados, omitidos.Count);
        return new ResultadoTomarDto(tomados, [.. items.Where(omitidos.ContainsKey).Select(i => new OmitidoTomarDto(i.Id, omitidos[i]))]);
    }

    /// <summary>
    /// Corre <paramref name="accion"/> sobre el saneamiento <paramref name="sid"/> de <paramref name="empresa"/> en una transacción: lee la
    /// cadena (404), bloquea cabecera → vehículo, relee el saneamiento con seguimiento, aplica y guarda en el ámbito de la empresa. La usa
    /// también la subida de la evidencia, después de guardar el archivo (SP-12).
    /// </summary>
    internal async Task EscribirAsync(Guid empresa, Guid sid, Func<SaneamientoEntidad, DateTimeOffset, Task> accion, CancellationToken ct)
    {
        ExigirUsuario();
        try
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                var ids = await db.DeLaEmpresa<SaneamientoEntidad>(empresa).Where(s => s.Id == sid)
                    .Select(s => new { s.DiagnosticoId, s.VehiculoId }).SingleOrDefaultAsync(ct).ConfigureAwait(false)
                    ?? throw NoEncontrado();
                await BloquearAsync(empresa, ids.DiagnosticoId, ids.VehiculoId, ct).ConfigureAwait(false);
                var saneamiento = await db.ParaEditarDeLaEmpresa<SaneamientoEntidad>(empresa).SingleAsync(s => s.Id == sid, ct).ConfigureAwait(false);
                await accion(saneamiento, reloj.GetUtcNow()).ConfigureAwait(false);
                await db.GuardarDeLaEmpresaAsync(empresa, Tipos, ct).ConfigureAwait(false);
                await transaccion.CommitAsync(ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw VersionDesactualizada();
        }
    }

    /// <summary>El renglón no quitado de ese saneamiento y esa empresa, con seguimiento; 404 si algún eslabón no coincide (SP-6).</summary>
    internal async Task<RenglonSaneamiento> RenglonParaEditarAsync(Guid empresa, Guid sid, Guid rid, CancellationToken ct) =>
        await db.ParaEditarDeLaEmpresa<RenglonSaneamiento>(empresa)
            .SingleOrDefaultAsync(r => r.Id == rid && r.SaneamientoId == sid && r.QuitadoEn == null, ct).ConfigureAwait(false)
        ?? throw NoEncontrado();

    /// <summary>409 <see cref="CodigoNoEditable"/> si el saneamiento no está EN_GESTION.</summary>
    internal static void ExigirEditable(SaneamientoEntidad saneamiento)
    {
        if (saneamiento.Estado != EstadoSaneamiento.EnGestion)
            throw ReglaException.Conflicto(CodigoNoEditable, "El saneamiento solo se edita mientras está en gestión.");
    }

    internal static ReglaException NoEncontrado() => ReglaException.NoEncontrado("El saneamiento");

    private async Task<SaneamientoBackofficeDto> TransicionAsync(
        Guid empresa, Guid sid, AccionSaneamiento accion, uint? version, string? motivo, CancellationToken ct)
    {
        var autor = ExigirUsuario();
        await EscribirAsync(empresa, sid, async (s, ahora) =>
        {
            if (version is not null)
                ExigirVersion(s.Xmin, version.Value);
            var aplicada = await TransicionarAsync(empresa, s, accion, autor, motivo, ahora, ct).ConfigureAwait(false);
            await AvisosSaneamiento.AgregarAsync(db, [aplicada], ahora, ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await DetalleAsync(empresa, sid, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// La transición por <see cref="EscritorSaneamiento.TransicionarAsync"/> con las filas de la empresa: al cerrar como SANEADO, los
    /// resultados de los renglones no quitados; en la primera toma, los hallazgos del vehículo (plan §3.3, brief C5).
    /// </summary>
    private async Task<TransicionAplicada> TransicionarAsync(
        Guid empresa, SaneamientoEntidad s, AccionSaneamiento accion, Guid autor, string? motivo, DateTimeOffset ahora, CancellationToken ct)
    {
        IReadOnlyCollection<ResultadoRenglonSaneamiento>? resultados = accion == AccionSaneamiento.CerrarSaneado
            ? await db.DeLaEmpresa<RenglonSaneamiento>(empresa).Where(r => r.SaneamientoId == s.Id && r.QuitadoEn == null)
                .Select(r => r.Resultado).ToListAsync(ct).ConfigureAwait(false)
            : null;
        var hallazgos = accion == AccionSaneamiento.Tomar && s.TomadoEn is null && s.Estado == EstadoSaneamiento.Solicitado
            ? await HallazgosAsync(empresa, s, ahora, ct).ConfigureAwait(false)
            : null;
        var aplicada = await EscritorSaneamiento.TransicionarAsync(
            db, s, accion, ActorSaneamiento.Flit, autor, motivo, ahora, ct, resultados, hallazgos,
            db.ParaEditarDeLaEmpresa<CabeceraDiagnostico>(empresa), db.ParaEditarDeLaEmpresa<VehiculoDiagnostico>(empresa)).ConfigureAwait(false);
        LogTransicion(logger, s.Id, s.DiagnosticoId, s.Estado, aplicada.Evento.Evento);
        return aplicada;
    }

    /// <summary>
    /// Los hallazgos del vehículo con las celdas de la empresa (brief C5): <see cref="LecturasVehiculo"/> arma el pedido en cualquier
    /// modo, con el tipo de trámite del lote o TRASPASO por defecto, y <see cref="HallazgosSaneamiento"/> decide (nunca el evaluador).
    /// </summary>
    private async Task<IReadOnlyList<HallazgoFuente>> HallazgosAsync(Guid empresa, SaneamientoEntidad s, DateTimeOffset ahora, CancellationToken ct)
    {
        var celdas = await db.DeLaEmpresa<CeldaConsulta>(empresa)
            .Where(c => c.DiagnosticoId == s.DiagnosticoId && c.VehiculoId == s.VehiculoId)
            .ToListAsync(ct).ConfigureAwait(false);
        var tipo = await db.DeLaEmpresa<CabeceraDiagnostico>(empresa).Where(c => c.Id == s.DiagnosticoId)
            .Select(c => c.TipoTramiteCodigo).SingleAsync(ct).ConfigureAwait(false);
        var pedido = LecturasVehiculo.Pedido(string.IsNullOrWhiteSpace(tipo) ? ConsultaDeuda.TipoTramitePorDefecto : tipo, ahora, lecturas.Armar(celdas));
        return HallazgosSaneamiento.Generar(LectorHechosDictamen.Leer(pedido));
    }

    /// <summary>Bloquea la cabecera (versión del lote) y marca la fila del vehículo, con las filas de la empresa (orden canónico).</summary>
    private Task<long> BloquearAsync(Guid empresa, Guid diagnosticoId, Guid vehiculoId, CancellationToken ct) =>
        EscritorCeldas.SubirVersionFilaAsync(
            db, diagnosticoId, vehiculoId, ct, db.ParaEditarDeLaEmpresa<CabeceraDiagnostico>(empresa), db.ParaEditarDeLaEmpresa<VehiculoDiagnostico>(empresa));

    private Guid ExigirUsuario() =>
        usuario.Id != Guid.Empty
            ? usuario.Id
            : throw ReglaException.Prohibido("SIN_USUARIO", "El token no identifica al usuario: la gestión no se puede auditar.");

    private static void ExigirVersion(uint vigente, uint pedida)
    {
        if (vigente != pedida)
            throw VersionDesactualizada();
    }

    private static ReglaException VersionDesactualizada() =>
        ReglaException.Conflicto(CodigoVersionDesactualizada, "El saneamiento cambió desde que lo leíste. Vuelve a leerlo.");

    private static async Task<List<(Guid Empresa, Guid Id)>> LeerItemsAsync(HttpRequest request, CancellationToken ct)
    {
        JsonElement raiz;
        try
        {
            raiz = await CuerpoSaneamiento.LeerAsync(request, ct).ConfigureAwait(false);
        }
        catch (ReglaException ex) when (ex.Codigo != CuerpoAcotadoFiltro.CodigoMuyGrande)
        {
            throw ListaInvalida();
        }

        if (raiz.ValueKind != JsonValueKind.Object || !raiz.TryGetProperty("items", out var lista) || lista.ValueKind != JsonValueKind.Array)
            throw ListaInvalida();
        var items = new List<(Guid, Guid)>();
        var vistos = new HashSet<(Guid, Guid)>();
        foreach (var item in lista.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !Uuid(item, "empresa", out var empresa) || empresa == Guid.Empty
                || !Uuid(item, "id", out var id))
                throw ListaInvalida();
            if (vistos.Add((empresa, id)))
                items.Add((empresa, id));
        }

        return items.Count is >= 1 and <= MaximoTomar ? items : throw ListaInvalida();
    }

    private static bool Uuid(JsonElement item, string campo, out Guid valor)
    {
        valor = Guid.Empty;
        return item.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out valor);
    }

    private static ReglaException ListaInvalida() =>
        new(CodigoListaInvalida, $"items debe ser una lista de 1 a {MaximoTomar} pares de empresa e id distintos.", StatusCodes.Status400BadRequest)
        {
            Campo = "items",
        };

    [LoggerMessage(Level = LogLevel.Information, Message = "Saneamiento {SaneamientoId} del lote {DiagnosticoId}: {Evento}, queda {Estado}.")]
    private static partial void LogTransicion(ILogger logger, Guid saneamientoId, Guid diagnosticoId, EstadoSaneamiento estado, TipoEventoSaneamiento evento);

    [LoggerMessage(Level = LogLevel.Information, Message = "Saneamiento {SaneamientoId}: {Evento} (renglón {RenglonId}).")]
    private static partial void LogEdicion(ILogger logger, Guid saneamientoId, Guid? renglonId, string evento);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tomar varios: {Pedidos} pedidos, {Tomados} tomados, {Omitidos} omitidos.")]
    private static partial void LogTomarVarios(ILogger logger, int pedidos, int tomados, int omitidos);
}

/// <summary>
/// Cuerpo JSON de las escrituras del backoffice del saneamiento (SP-13 con E3): versión obligatoria donde aplica (422
/// <c>VERSION_INVALIDA</c>, brief C6), textos con largo en puntos de código y sin caracteres de control ni bidireccionales
/// (<see cref="TextoLibreSaneamiento"/>). Los mensajes son fijos y nunca repiten lo digitado.
/// </summary>
internal static class CuerpoSaneamiento
{
    /// <summary>
    /// El cuerpo como JSON; vacío es un objeto sin claves; JSON mal formado es 400 <c>CUERPO_INVALIDO</c>; más de 64 KiB es 413
    /// <c>CUERPO_MUY_GRANDE</c> sin leer el resto (<see cref="CuerpoAcotadoFiltro"/>, CR-11).
    /// </summary>
    public static async Task<JsonElement> LeerAsync(HttpRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var cuerpo = await CuerpoAcotadoFiltro.LeerAsync(request, ct).ConfigureAwait(false);
        using var lector = new StreamReader(cuerpo);
        var texto = await lector.ReadToEndAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(texto))
            return JsonElement.Parse("{}");
        try
        {
            using var documento = JsonDocument.Parse(texto);
            return documento.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new ReglaException(GestionSaneamientoBackoffice.CodigoCuerpoInvalido, "El cuerpo no es un JSON válido.", StatusCodes.Status400BadRequest);
        }
    }

    /// <summary><c>version</c> (el <c>xmin</c>): entero de 0 a 4294967295; si falta o no lo es, 422 <c>VERSION_INVALIDA</c>.</summary>
    public static uint Version(JsonElement cuerpo) =>
        cuerpo.ValueKind == JsonValueKind.Object && cuerpo.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetUInt32(out var n)
            ? n
            : throw new ReglaException(GestionSaneamientoBackoffice.CodigoVersionInvalida, "version es obligatoria: la que devolvió la última lectura.")
            {
                Campo = "version",
            };

    /// <summary>El valor si es una cadena; null si falta o es de otro tipo (la regla del campo decide).</summary>
    public static string? Cadena(JsonElement cuerpo, string campo) =>
        cuerpo.ValueKind == JsonValueKind.Object && cuerpo.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>
    /// Texto opcional (null o ausente es null) de <paramref name="minimo"/> a <paramref name="maximo"/> puntos de código: 422
    /// <c>TEXTO_INVALIDO</c> con caracteres prohibidos; 422 <c>VALOR_INVALIDO</c> si no es una cadena, por el largo o por espacios en
    /// los extremos. Una cadena vacía con mínimo 0 vale (se guarda null).
    /// </summary>
    public static string? TextoOpcional(JsonElement cuerpo, string campo, int minimo, int maximo)
    {
        if (cuerpo.ValueKind != JsonValueKind.Object || !cuerpo.TryGetProperty(campo, out var v) || v.ValueKind == JsonValueKind.Null)
            return null;
        if (v.ValueKind != JsonValueKind.String)
            throw ValorInvalido(campo, $"{campo} es un texto.");
        var texto = v.GetString()!;
        if (texto.Length == 0 && minimo == 0)
            return texto;
        return TextoLibreSaneamiento.Validar(texto, minimo, maximo) switch
        {
            null => texto,
            ErrorTextoLibre.CaracterInvalido => throw new ReglaException(TextoLibreSaneamiento.TextoInvalido, "El texto trae caracteres no permitidos.") { Campo = campo },
            _ => throw ValorInvalido(campo, $"{campo} admite de {minimo} a {maximo} caracteres, sin espacios al inicio ni al final."),
        };
    }

    public static ReglaException ValorInvalido(string campo, string mensaje) =>
        new(ManejadorReglas.CodigoValorInvalido, mensaje) { Campo = campo };
}
