using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Backoffice;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Dictamen;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Dictamen;

/// <summary>Lecturas ya armadas. El emisor no descifra para construir este tipo: quien cierra se las pasa, o las arma aparte.</summary>
internal sealed record LecturasParaDictamen(
    LecturaCelda<DetalleRuntReporte>? RuntDetalle,
    LecturaCelda<VehiculoRunt>? RuntNormalizado,
    LecturaCelda<IReadOnlyList<ResultadoSimitDetalle>>? SimitVehiculo,
    LecturaCelda<IReadOnlyList<ResultadoRuntPersonaDetalle>>? RuntPropietario,
    LecturaCelda<IReadOnlyList<ResultadoSimitDetalle>>? SimitPropietario,
    LecturaCelda<IReadOnlyList<ResultadoSimitDetalle>>? SimitLocatario,
    LecturaCelda<IReadOnlyList<ResultadoRuntPersonaDetalle>>? RuntLocatario,
    LecturaCelda<ResultadoImpuestoDetalle>? Impuesto);

/// <summary>Estado de las hermanas al entrar al cierre, y el estado anterior de la celda que se cierra.</summary>
internal readonly record struct FotoFlanco(EstadoCelda EstadoAnterior, IReadOnlyList<EstadoCelda> Hermanas);

/// <summary>Versión de negocio que sale de la función, sin abrir Postgres.</summary>
internal readonly record struct VersionEmitida(int Version, bool Vigente);

/// <summary>La vigente anterior, apagada, y la fila nueva. Sin anterior, <see cref="Anterior"/> es null y la nueva es la 1.</summary>
internal readonly record struct TransicionVersion(VersionEmitida? Anterior, VersionEmitida Nueva);

/// <summary>
/// Escribe <c>diagnostico.dictamenes</c> cuando un vehículo Con Análisis IA de la familia TRASPASO pasa a terminado
/// (corte 5). El cierre deja la plantilla y la fecha de toma (corte 6): no llama al modelo. El flanco, el pedido, los
/// alertamientos y la versión son funciones. La fila va en la transacción de quien cierra, y con ella el evento
/// <c>diagnostico.dictamen.emitido</c> (corte 7).
/// </summary>
internal sealed partial class EmisorDictamen(
    DiagnosticoDb db, IProtectorRespuestaFuente protector, IProveedorRedactor proveedor, IPlatformOutbox outbox, ILogger<EmisorDictamen> log)
{
    /// <summary>El payload solo guarda cuántos locatarios había. El lector pregunta si el número viene con texto.</summary>
    private const string DocumentoSoloParaElLector = "1";

    private static readonly JsonSerializerOptions Json = new();

    /// <summary>
    /// true si el vehículo no estaba terminado con el estado anterior de esta celda y el actual de las hermanas, y sí lo está
    /// al cambiar solo esta celda.
    /// </summary>
    public static bool FlancoDeSubida(EstadoCelda estadoAnterior, EstadoCelda estadoNuevo, IReadOnlyList<EstadoCelda> hermanas)
    {
        ArgumentNullException.ThrowIfNull(hermanas);
        var antes = new EstadoCelda[hermanas.Count + 1];
        var despues = new EstadoCelda[hermanas.Count + 1];
        antes[0] = estadoAnterior;
        despues[0] = estadoNuevo;
        for (var i = 0; i < hermanas.Count; i++)
        {
            antes[i + 1] = hermanas[i];
            despues[i + 1] = hermanas[i];
        }

        return !AvanceVehiculo.Terminado(antes) && AvanceVehiculo.Terminado(despues);
    }

    /// <summary>Sin un cierre que cambie una celda no hay flanco. Un vehículo sin celdas no está terminado.</summary>
    public static bool FlancoDeSubida(IReadOnlyList<EstadoCelda> celdas)
    {
        ArgumentNullException.ThrowIfNull(celdas);
        return false;
    }

    /// <summary>Null si el lote es simple, el tipo viene vacío o el código no está en la familia. No llama al evaluador.</summary>
    public static PedidoLectura? ArmarPedido(ModoDiagnostico modo, string? tipoTramiteCodigo, DateTimeOffset ahora, LecturasParaDictamen lecturas)
    {
        ArgumentNullException.ThrowIfNull(lecturas);
        if (!Admite(modo, tipoTramiteCodigo))
            return null;

        var hoy = DateOnly.FromDateTime(ahora.ToOffset(VigenciaDelDia.OffsetColombia).DateTime);
        return new PedidoLectura(
            tipoTramiteCodigo!,
            hoy,
            lecturas.RuntDetalle,
            lecturas.RuntNormalizado,
            lecturas.SimitVehiculo,
            lecturas.RuntPropietario,
            lecturas.SimitPropietario,
            lecturas.SimitLocatario,
            lecturas.RuntLocatario,
            lecturas.Impuesto);
    }

    /// <summary>Un objeto por regla, en ese orden. En pendiente el texto es la plantilla del catálogo; en completada, el del modelo.</summary>
    public static string AlertamientosJson(string tipoTramiteCodigo, DictamenEvaluado evaluado, string estadoRedaccion, string textoModelo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tipoTramiteCodigo);
        ArgumentNullException.ThrowIfNull(evaluado);
        ArgumentNullException.ThrowIfNull(evaluado.Reglas);
        var alertas = new AlertaJson[evaluado.Reglas.Count];
        for (var i = 0; i < evaluado.Reglas.Count; i++)
        {
            var regla = evaluado.Reglas[i];
            var texto = estadoRedaccion == "COMPLETADA"
                ? textoModelo
                : CatalogoReglasDictamen.Filas.Single(f => f.TipoTramiteCodigo == tipoTramiteCodigo && f.Codigo == regla.Codigo).PlantillaTexto;
            alertas[i] = new AlertaJson(regla.Codigo, texto, regla.Efecto, regla.Fuente);
        }

        return JsonSerializer.Serialize(alertas, Json);
    }

    /// <summary>
    /// El mismo arreglo, en el mismo orden. <c>codigo</c>, <c>efecto</c> y <c>fuente</c> se conservan. El texto vuelve a la
    /// plantilla del catálogo para <paramref name="tipoTramiteCodigo"/> en <paramref name="catalogoVersion"/> (la del dictamen,
    /// para que un dictamen v1 no tome los textos de v2; una versión desconocida usa la vigente); si esa fila no está, se
    /// conserva el texto guardado.
    /// </summary>
    internal static string TextosDePlantilla(string? tipoTramiteCodigo, string alertamientos, int catalogoVersion = CatalogoReglasDictamen.Version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alertamientos);
        using var documento = JsonDocument.Parse(alertamientos);
        if (documento.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Los alertamientos del dictamen no son un arreglo.");

        var filas = CatalogoReglasDictamen.FilasDe(catalogoVersion);
        if (filas.Count == 0)
            filas = CatalogoReglasDictamen.Filas;

        var lista = new List<AlertaJson>();
        foreach (var item in documento.RootElement.EnumerateArray())
        {
            var codigo = item.GetProperty("codigo").GetString() ?? string.Empty;
            var texto = item.GetProperty("texto").GetString() ?? string.Empty;
            var efecto = item.GetProperty("efecto").GetString() ?? string.Empty;
            var fuente = item.GetProperty("fuente").GetString() ?? string.Empty;
            var plantilla = filas.FirstOrDefault(f => f.TipoTramiteCodigo == tipoTramiteCodigo && f.Codigo == codigo);
            if (plantilla is not null)
                texto = plantilla.PlantillaTexto;
            lista.Add(new AlertaJson(codigo, texto, efecto, fuente));
        }

        return JsonSerializer.Serialize(lista, Json);
    }

    /// <summary>La fila que se agrega. <paramref name="version"/> ya salió de <see cref="SiguienteVersion"/>.</summary>
    public static DictamenVehiculo Materializar(
        Guid tenantId, Guid vehiculoId, int version, string tipoTramiteCodigo, DictamenEvaluado evaluado, ResultadoRedaccion redaccion, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(evaluado);
        ArgumentNullException.ThrowIfNull(redaccion);
        return new DictamenVehiculo
        {
            Id = Guid.CreateVersion7(ahora),
            TenantId = tenantId,
            VehiculoId = vehiculoId,
            Version = version,
            Vigente = true,
            Veredicto = evaluado.Veredicto,
            Alertamientos = AlertamientosJson(tipoTramiteCodigo, evaluado, redaccion.EstadoRedaccion, redaccion.Texto),
            CatalogoVersion = CatalogoReglasDictamen.Version,
            RedactadoPor = redaccion.RedactadoPor,
            EstadoRedaccion = redaccion.EstadoRedaccion,
            RedaccionIntentos = redaccion.Intentos,
            RedaccionDisponibleDesde = DisponibleDesde(redaccion, ahora),
            RedaccionLeaseHasta = null,
            AnalisisOverride = false,
            UsuarioOverrideId = null,
            OverrideEn = null,
            CreadoEn = ahora,
        };
    }

    /// <summary>Sin vigente, la versión es 1. Con vigente, esa deja de serlo y la nueva suma uno.</summary>
    public static TransicionVersion SiguienteVersion(int? versionVigente)
    {
        if (versionVigente is null)
            return new TransicionVersion(null, new VersionEmitida(1, true));
        if (versionVigente < 1)
            throw new ArgumentOutOfRangeException(nameof(versionVigente), versionVigente, "La versión vigente empieza en 1.");
        return new TransicionVersion(new VersionEmitida(versionVigente.Value, false), new VersionEmitida(versionVigente.Value + 1, true));
    }

    /// <summary>
    /// Placa, clase, marca, línea, modelo y servicio. Con detalle salen de ahí. Sin detalle, la placa y el servicio salen del
    /// vehículo y el resto queda null. Sin placa, se usa <paramref name="identificadorNorm"/>. No hay documentos ni nombres.
    /// </summary>
    public static DatosBien BienDe(DetalleRuntReporte? detalle, string? placaVehiculo, string? servicioVehiculo, string identificadorNorm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identificadorNorm);
        if (detalle is null)
            return new DatosBien(Texto(placaVehiculo) ?? identificadorNorm, null, null, null, null, Texto(servicioVehiculo));

        return new DatosBien(
            Texto(detalle.Vehiculo.Placa) ?? Texto(placaVehiculo) ?? identificadorNorm,
            Texto(detalle.Vehiculo.Clase),
            Texto(detalle.General.Marca),
            Texto(detalle.General.Linea),
            Texto(detalle.Vehiculo.Modelo),
            Texto(detalle.Vehiculo.TipoServicio));
    }

    /// <summary>El veredicto y las reglas ya evaluadas, el bien y el interruptor tal como se le pasa. El redactor no cambia el veredicto.</summary>
    public static PedidoRedaccion PedidoAlRedactor(DictamenEvaluado evaluado, DatosBien bien, bool redaccionSuspendida)
    {
        ArgumentNullException.ThrowIfNull(evaluado);
        ArgumentNullException.ThrowIfNull(evaluado.Reglas);
        ArgumentNullException.ThrowIfNull(bien);
        var reglas = new ReglaDisparada[evaluado.Reglas.Count];
        for (var i = 0; i < evaluado.Reglas.Count; i++)
        {
            var regla = evaluado.Reglas[i];
            reglas[i] = new ReglaDisparada(regla.Codigo, regla.Fuente, regla.Efecto, regla.MontoPesos);
        }

        return new PedidoRedaccion(evaluado.Veredicto, reglas, bien, redaccionSuspendida);
    }

    /// <summary>
    /// Fecha de toma que deja el cierre. El reloj si hay proveedor y la redacción no está suspendida; null si está suspendida
    /// o el proveedor no está configurado. La cola no llama al modelo en el cierre.
    /// </summary>
    public static DateTimeOffset? DisponibleAlCerrar(bool redaccionSuspendida, bool proveedorConfigurado, DateTimeOffset ahora) =>
        redaccionSuspendida || !proveedorConfigurado ? null : ahora;

    /// <summary>
    /// La fila del cierre: siempre <c>PLANTILLA</c> / <c>PENDIENTE</c>, intentos 0 y sin lease. El texto de cada alertamiento es
    /// la plantilla del catálogo. <see cref="DisponibleAlCerrar"/> decide si la cola puede tomarla.
    /// </summary>
    public static DictamenVehiculo FilaAlCerrar(
        Guid tenantId, Guid vehiculoId, int version, string tipoTramiteCodigo, DictamenEvaluado evaluado,
        bool redaccionSuspendida, bool proveedorConfigurado, DateTimeOffset ahora)
    {
        var fila = Materializar(
            tenantId, vehiculoId, version, tipoTramiteCodigo, evaluado,
            new ResultadoRedaccion(string.Empty, "PLANTILLA", "PENDIENTE", 0, string.Empty, null), ahora);
        fila.RedaccionDisponibleDesde = DisponibleAlCerrar(redaccionSuspendida, proveedorConfigurado, ahora);
        return fila;
    }

    /// <summary>
    /// La fila del override. No vuelve a evaluar y no redacta: el veredicto es el pedido y los textos vuelven a la plantilla.
    /// Sin confirmación, o si el veredicto es el de la vigente, no arma fila y no apaga la anterior.
    /// </summary>
    internal static DictamenVehiculo FilaDeOverride(
        DictamenVehiculo vigente, string veredicto, bool confirmado, Guid usuarioId, string? tipoTramiteCodigo,
        bool redaccionSuspendida, bool proveedorConfigurado, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(vigente);
        if (!confirmado)
            throw new ReglaException(CodigosDictamen.ConfirmadoRequerido, "Confirma el cambio de veredicto para reemplazarlo.");
        if (veredicto is not ("VIABLE" or "BLOQUEADO"))
            throw new ReglaException(CodigosDictamen.VeredictoInvalido, "El veredicto debe ser Viable o Bloqueado.");
        if (veredicto == vigente.Veredicto)
            throw ReglaException.Conflicto(CodigosDictamen.VeredictoSinCambio, "El veredicto pedido es el del dictamen vigente.");

        var transicion = SiguienteVersion(vigente.Version);
        vigente.Vigente = false;
        return new DictamenVehiculo
        {
            Id = Guid.CreateVersion7(ahora),
            TenantId = vigente.TenantId,
            VehiculoId = vigente.VehiculoId,
            Version = transicion.Nueva.Version,
            Vigente = true,
            Veredicto = veredicto,
            Alertamientos = TextosDePlantilla(tipoTramiteCodigo, vigente.Alertamientos, vigente.CatalogoVersion),
            CatalogoVersion = vigente.CatalogoVersion,
            RedactadoPor = "PLANTILLA",
            EstadoRedaccion = "PENDIENTE",
            RedaccionIntentos = 0,
            RedaccionDisponibleDesde = DisponibleAlCerrar(redaccionSuspendida, proveedorConfigurado, ahora),
            RedaccionLeaseHasta = null,
            AnalisisOverride = true,
            UsuarioOverrideId = usuarioId,
            OverrideEn = ahora,
            AjustadoDesdeVersion = vigente.Version,
            CreadoEn = ahora,
        };
    }

    /// <summary>
    /// La fila del ajuste FLIT con alertamientos (HU #14563 B7, ADR-0086 D-2): el estado completo pedido por soporte. Nace
    /// <c>MANUAL</c> y <c>COMPLETADA</c> (la cola del LLM no la toma), con el autor, el instante, el motivo y la versión de
    /// origen. Cada alertamiento lleva <c>ajuste</c> <c>AGREGADO</c> (no estaba en la vigente con ese código y fuente) o
    /// <c>EDITADO</c> (cambió el texto o el efecto); uno igual no la lleva. Los eliminados no se guardan: se calculan al leer las
    /// versiones. Si nada cambia (veredicto y alertamientos iguales) es 409 <c>VEREDICTO_SIN_CAMBIO</c> y la vigente sigue.
    /// </summary>
    internal static DictamenVehiculo FilaDeAjuste(
        DictamenVehiculo vigente, string veredicto, IReadOnlyList<AlertamientoAjuste> alertamientos, string motivo, Guid usuarioId, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(vigente);
        ArgumentNullException.ThrowIfNull(alertamientos);
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        if (veredicto is not ("VIABLE" or "BLOQUEADO"))
            throw new ReglaException(CodigosDictamen.VeredictoInvalido, "El veredicto debe ser Viable o Bloqueado.");

        var pendientes = AlertasGuardadas(vigente.Alertamientos);
        var anteriores = pendientes.Count;
        var marcadas = new List<AlertaAjustadaJson>(alertamientos.Count);
        foreach (var pedido in alertamientos)
        {
            var fuente = pedido.Fuente ?? string.Empty;
            var previa = pendientes.FindIndex(a => a.Codigo == pedido.Codigo && a.Fuente == fuente);
            string? ajuste = "AGREGADO";
            if (previa >= 0)
            {
                var guardada = pendientes[previa];
                pendientes.RemoveAt(previa);
                ajuste = guardada.Texto == pedido.Texto && guardada.Efecto == pedido.Efecto ? null : "EDITADO";
            }

            marcadas.Add(new AlertaAjustadaJson(pedido.Codigo, pedido.Texto, pedido.Efecto, fuente, ajuste));
        }

        if (veredicto == vigente.Veredicto && pendientes.Count == 0 && marcadas.Count == anteriores && marcadas.TrueForAll(a => a.Ajuste is null))
            throw ReglaException.Conflicto(CodigosDictamen.VeredictoSinCambio, "El veredicto y los alertamientos pedidos son los del dictamen vigente.");

        var transicion = SiguienteVersion(vigente.Version);
        vigente.Vigente = false;
        return new DictamenVehiculo
        {
            Id = Guid.CreateVersion7(ahora),
            TenantId = vigente.TenantId,
            VehiculoId = vigente.VehiculoId,
            Version = transicion.Nueva.Version,
            Vigente = true,
            Veredicto = veredicto,
            Alertamientos = JsonSerializer.Serialize(marcadas, Json),
            CatalogoVersion = vigente.CatalogoVersion,
            RedactadoPor = "MANUAL",
            EstadoRedaccion = "COMPLETADA",
            RedaccionIntentos = 0,
            RedaccionDisponibleDesde = null,
            RedaccionLeaseHasta = null,
            AnalisisOverride = true,
            UsuarioOverrideId = usuarioId,
            OverrideEn = ahora,
            AjusteMotivo = motivo,
            AjustadoDesdeVersion = vigente.Version,
            CreadoEn = ahora,
        };
    }

    /// <summary>Los alertamientos guardados (código, texto, efecto, fuente), en orden; los campos que falten quedan vacíos.</summary>
    internal static List<AlertamientoAjuste> AlertasGuardadas(string alertamientos)
    {
        using var documento = JsonDocument.Parse(string.IsNullOrWhiteSpace(alertamientos) ? "[]" : alertamientos);
        var lista = new List<AlertamientoAjuste>();
        if (documento.RootElement.ValueKind != JsonValueKind.Array)
            return lista;
        foreach (var item in documento.RootElement.EnumerateArray())
        {
            string Campo(string nombre) =>
                item.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() ?? string.Empty : string.Empty;
            lista.Add(new AlertamientoAjuste(Campo("codigo"), Campo("texto"), Campo("efecto"), Campo("fuente")));
        }

        return lista;
    }

    /// <summary>
    /// Null salvo un fallo técnico (<see cref="TipoFalloRedactor.Timeout"/>, <see cref="TipoFalloRedactor.Http5xx"/>,
    /// <see cref="TipoFalloRedactor.RateLimit"/> o <see cref="TipoFalloRedactor.Red"/>). Con espera, el reloj más esa espera;
    /// sin ella, el reloj. Suspensión, proveedor apagado y contradicción quedan null. La usa la cola después de un fallo, no el cierre.
    /// </summary>
    public static DateTimeOffset? DisponibleDesde(ResultadoRedaccion redaccion, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(redaccion);
        if (redaccion.TipoFallo is not (TipoFalloRedactor.Timeout or TipoFalloRedactor.Http5xx or TipoFalloRedactor.RateLimit or TipoFalloRedactor.Red))
            return null;
        return redaccion.ReintentarEn is { } espera ? ahora.Add(espera) : ahora;
    }

    /// <summary>
    /// Foto al entrar al cierre, antes de publicar celdas anidadas. Null si este lote no dictamina: simple, sin tipo o fuera
    /// de la familia.
    /// </summary>
    public async Task<FotoFlanco?> PrepararAsync(CeldaConsulta celda, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(celda);
        var cabecera = await CabeceraAsync(celda, ct).ConfigureAwait(false);
        if (!Admite(cabecera.Modo, cabecera.TipoTramiteCodigo))
            return null;

        // La entidad seguida gana sobre la fila ya guardada: el «antes» es el estado en memoria al entrar.
        var hermanas = await db.ParaEditarDeLaEmpresa<CeldaConsulta>(celda.TenantId)
            .Where(c => c.VehiculoId == celda.VehiculoId && c.Id != celda.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return new FotoFlanco(celda.Estado, hermanas.Select(c => c.Estado).ToArray());
    }

    /// <summary>
    /// Si el flanco sube, evalúa y deja la plantilla en el contexto. No llama al modelo y no confirma: la transacción es de
    /// quien cierra. Encola <c>diagnostico.dictamen.emitido</c> con la fila nueva. Un fallo al armar o al evaluar se propaga
    /// y el cierre no queda a medias.
    /// </summary>
    public async Task EmitirAsync(CeldaConsulta celda, EstadoCelda estadoNuevo, FotoFlanco foto, DateTimeOffset ahora, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(celda);
        if (!FlancoDeSubida(foto.EstadoAnterior, estadoNuevo, foto.Hermanas))
            return;

        var cabecera = await CabeceraAsync(celda, ct).ConfigureAwait(false);
        var pedido = ArmarPedido(cabecera.Modo, cabecera.TipoTramiteCodigo, ahora, await LecturasAsync(celda, ct).ConfigureAwait(false));
        if (pedido is null)
            return;

        var evaluado = EvaluadorDictamen.Evaluar(LectorHechosDictamen.Leer(pedido));
        var suspendida = await db.DeLaEmpresa<ConfiguracionEmpresa>(celda.TenantId)
            .Select(c => (bool?)c.RedaccionIaSuspendida)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? false;

        var vigente = await db.ParaEditarDeLaEmpresa<DictamenVehiculo>(celda.TenantId)
            .SingleOrDefaultAsync(d => d.VehiculoId == celda.VehiculoId && d.Vigente, ct)
            .ConfigureAwait(false);
        var transicion = SiguienteVersion(vigente?.Version);
        if (vigente is not null)
            vigente.Vigente = false;

        var fila = FilaAlCerrar(
            celda.TenantId, celda.VehiculoId, transicion.Nueva.Version, pedido.TipoTramiteCodigo, evaluado, suspendida, proveedor.Configurado, ahora);
        // HU #14563 (ADR-0086 D-1, AC7): la fila toma la versión de la celda que cierra; la cabecera no sube otra vez.
        await EscritorCeldas.MarcarFilaAsync(
            db, celda.VehiculoId, celda.VersionTablero, ct, db.ParaEditarDeLaEmpresa<VehiculoDiagnostico>(celda.TenantId)).ConfigureAwait(false);
        db.Add(fila);
        DictamenEmitido.Encolar(outbox, cabecera, fila, esOverride: false);
        Escrito(log, celda.VehiculoId, fila.Version, fila.Veredicto, fila.EstadoRedaccion);
    }

    private async Task<CabeceraDiagnostico> CabeceraAsync(CeldaConsulta celda, CancellationToken ct) =>
        db.ChangeTracker.Entries<CabeceraDiagnostico>().Select(e => e.Entity).FirstOrDefault(c => c.Id == celda.DiagnosticoId)
        ?? await db.ParaEditarDeLaEmpresa<CabeceraDiagnostico>(celda.TenantId).SingleAsync(c => c.Id == celda.DiagnosticoId, ct).ConfigureAwait(false);

    private async Task<LecturasParaDictamen> LecturasAsync(CeldaConsulta celda, CancellationToken ct)
    {
        var celdas = await db.ParaEditarDeLaEmpresa<CeldaConsulta>(celda.TenantId)
            .Where(c => c.VehiculoId == celda.VehiculoId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        CeldaConsulta? De(Fuente fuente) => celdas.FirstOrDefault(c => c.Fuente == fuente);
        var runt = De(Fuente.Runt);
        return new LecturasParaDictamen(
            Detalle(runt),
            Normalizado(runt),
            SimitVehiculo(De(Fuente.SimitVehiculo)),
            PersonasRunt(De(Fuente.RuntPropietario)),
            PersonasSimit(De(Fuente.SimitPropietario)),
            PersonasSimit(De(Fuente.SimitLocatario)),
            PersonasRunt(De(Fuente.RuntLocatario)),
            Impuesto(De(Fuente.ImpuestoDepartamental)));
    }

    private LecturaCelda<DetalleRuntReporte>? Detalle(CeldaConsulta? celda) =>
        celda is null ? null : new LecturaCelda<DetalleRuntReporte>(celda.Estado, LectorDetalleRunt.Leer(protector, celda.RespuestaFuenteCifrada));

    private static LecturaCelda<VehiculoRunt>? Normalizado(CeldaConsulta? celda)
    {
        if (celda is null)
            return null;
        VehiculoRunt? vehiculo = null;
        if (celda.Estado == EstadoCelda.Completado && ProyectorDetalleCelda.ResultadoVehiculo(Fuente.Runt, celda.Payload) is ResultadoRuntDetalle detalle)
        {
            var propietarios = (detalle.Propietarios ?? []).Select(p => new PropietarioRunt(
                p.TipoDocumento ?? string.Empty,
                string.Empty,
                CodigosDeEnum.TryLeer<TipoPropiedad>(p.TipoPropiedad, out var tipo) ? tipo : TipoPropiedad.Desconocido,
                [])).ToArray();
            var locatarios = detalle.Locatarios > 0 ? new[] { new LocatarioRunt(string.Empty, DocumentoSoloParaElLector) } : Array.Empty<LocatarioRunt>();
            vehiculo = new VehiculoRunt(detalle.Placa, detalle.Vin, detalle.Servicio, detalle.Clase, detalle.Organismo, propietarios, locatarios, string.Empty, null);
        }

        return new LecturaCelda<VehiculoRunt>(celda.Estado, vehiculo);
    }

    private LecturaCelda<IReadOnlyList<ResultadoSimitDetalle>>? SimitVehiculo(CeldaConsulta? celda)
    {
        if (celda is null)
            return null;
        var resultado = ProyectorDetalleCelda.ResultadoVehiculo(Fuente.SimitVehiculo, celda.Payload) as ResultadoSimitDetalle;
        if (resultado is not null
            && RespuestaFuenteCelda.TryLeerVehiculo(protector, celda.RespuestaFuenteCifrada, out var cruda)
            && cruda is { } elemento
            && ProyectorDetalleCelda.RespuestaProveedor(elemento, out _) is { } cuerpo)
            resultado = SimitVisible.Completar(resultado, cuerpo);
        return new LecturaCelda<IReadOnlyList<ResultadoSimitDetalle>>(celda.Estado, resultado is null ? null : [resultado]);
    }

    private LecturaCelda<IReadOnlyList<ResultadoRuntPersonaDetalle>>? PersonasRunt(CeldaConsulta? celda) =>
        Personas<ResultadoRuntPersonaDetalle>(celda, EjecutorCeldaRuntPersona.ConsultaRuntPersona, static (detalle, crudo) => LicenciasConductor.Completar(detalle, crudo));

    private LecturaCelda<IReadOnlyList<ResultadoSimitDetalle>>? PersonasSimit(CeldaConsulta? celda) =>
        Personas<ResultadoSimitDetalle>(celda, EjecutorCeldaSimitPersona.ConsultaSimit, static (detalle, crudo) => SimitVisible.Completar(detalle, crudo));

    private LecturaCelda<IReadOnlyList<T>>? Personas<T>(CeldaConsulta? celda, string consulta, Func<T, JsonElement, T> completar)
        where T : class
    {
        if (celda is null)
            return null;
        var payload = EjecutorPorPersonas.Leer(celda.Payload);
        if (payload is null)
            return new LecturaCelda<IReadOnlyList<T>>(celda.Estado, null);
        if (!RespuestaFuenteCelda.TryLeerPersonas(protector, celda.RespuestaFuenteCifrada, out var respuestas))
            respuestas = new Dictionary<int, JsonElement>();

        var lista = new List<T>();
        foreach (var persona in payload.Personas)
        {
            if (persona.Consulta != consulta || persona.Estado != EjecutorPorPersonas.EstadoOk || persona.Resultado is not { } json)
                continue;
            T? detalle;
            try
            {
                detalle = json.Deserialize<T>(Json);
            }
            catch (JsonException)
            {
                detalle = null;
            }

            if (detalle is null)
                continue;
            if (respuestas.TryGetValue(persona.Orden, out var cruda) && ProyectorDetalleCelda.RespuestaProveedor(cruda, out _) is { } cuerpo)
                detalle = completar(detalle, cuerpo);
            lista.Add(detalle);
        }

        return new LecturaCelda<IReadOnlyList<T>>(celda.Estado, lista);
    }

    private static LecturaCelda<ResultadoImpuestoDetalle>? Impuesto(CeldaConsulta? celda) =>
        celda is null
            ? null
            : new LecturaCelda<ResultadoImpuestoDetalle>(
                celda.Estado, ProyectorDetalleCelda.ResultadoVehiculo(Fuente.ImpuestoDepartamental, celda.Payload) as ResultadoImpuestoDetalle);

    private static bool Admite(ModoDiagnostico modo, string? tipoTramiteCodigo) =>
        modo == ModoDiagnostico.ConAnalisis
        && !string.IsNullOrWhiteSpace(tipoTramiteCodigo)
        && CatalogoReglasDictamen.TiposTramite.Contains(tipoTramiteCodigo, StringComparer.Ordinal);

    private static string? Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    [LoggerMessage(EventId = 126785, Level = LogLevel.Information,
        Message = "El vehículo {VehiculoId} escribió el dictamen versión {Version} ({Veredicto}, redacción {EstadoRedaccion})")]
    private static partial void Escrito(ILogger logger, Guid vehiculoId, int version, string veredicto, string estadoRedaccion);

    private sealed record AlertaAjustadaJson(
        [property: JsonPropertyName("codigo")] string Codigo,
        [property: JsonPropertyName("texto")] string Texto,
        [property: JsonPropertyName("efecto")] string Efecto,
        [property: JsonPropertyName("fuente")] string Fuente,
        [property: JsonPropertyName("ajuste"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Ajuste);

    private sealed record AlertaJson(
        [property: JsonPropertyName("codigo")] string Codigo,
        [property: JsonPropertyName("texto")] string Texto,
        [property: JsonPropertyName("efecto")] string Efecto,
        [property: JsonPropertyName("fuente")] string Fuente);
}
