using System.Globalization;
using System.IO.Compression;
using System.Text;
using Flit.Diagnostico.Api.Infraestructura.Archivos;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Platform.Sdk.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>Un vehículo del lote en el ZIP: su identificación, el estado que sale en el índice y si su PDF va dentro.</summary>
internal sealed record VehiculoDelZip(Guid Id, int Orden, string Identificador, string Estado, bool Incluido);

/// <summary>
/// Lo que contendría el ZIP del lote hoy (HU #13850): los vehículos en el orden de la carga, cuáles pueden emitir su PDF
/// (<see cref="EmisionPdf.Puede"/>, R-7/Q7: terminados y, con el lote INCOMPLETO, también los que tienen celdas en ERROR) y la huella.
/// La huella es la de las entradas del lote (<see cref="HuellaEvidencia"/> con <see cref="EmpaquetadorZip.VersionPlantilla"/>): todas
/// las celdas con su versión del tablero, la cabecera, lo que el contenido del lote muestra sin venir de las celdas
/// (<see cref="ContenidoReporteLote.ParaHuella"/>: empresa, creador, tipo de análisis, identificación y documentos enmascarados de cada
/// vehículo), el estado del lote, la versión de la plantilla del PDF y la lista ordenada de los vehículos incluidos. Cualquier cambio
/// que cambie un PDF del ZIP o el índice cambia la huella; el origen y el usuario manual no entran (R-20).
/// </summary>
internal sealed record PlanZip(string Huella, IReadOnlyList<VehiculoDelZip> Vehiculos)
{
    public IReadOnlyList<VehiculoDelZip> Incluidos => [.. Vehiculos.Where(v => v.Incluido)];

    public int Pendientes => Vehiculos.Count(v => !v.Incluido);

    /// <summary>El plan del lote <paramref name="cabecera"/> con el contexto de su empresa y el nombre de empresa del pedido.</summary>
    public static async Task<PlanZip> LeerAsync(DiagnosticoDb db, LectorContenidoReporte lector, CabeceraDiagnostico cabecera, string? empresa, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(lector);
        ArgumentNullException.ThrowIfNull(cabecera);
        var lote = await lector.LoteAsync(cabecera, empresa, ct).ConfigureAwait(false);
        var estados = (await db.Set<CeldaConsulta>().AsNoTracking()
                .Where(c => c.DiagnosticoId == cabecera.Id && c.TenantId == cabecera.TenantId)
                .Select(c => new { c.VehiculoId, c.Estado })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(c => c.VehiculoId, c => c.Estado);
        var vehiculos = lote.Vehiculos.Select(v =>
        {
            var celdas = estados[v.VehiculoId].ToList();
            return new VehiculoDelZip(v.VehiculoId, v.Orden, v.Identificador, Estado(celdas), EmisionPdf.Puede(cabecera, celdas));
        }).ToList();

        var encabezado = new Dictionary<string, string?>(lote.ParaHuella(), StringComparer.Ordinal)
        {
            ["lote/estado"] = CodigosDeEnum.Codigo(cabecera.Estado),
            ["zip/version_pdf"] = PdfVehiculo.VersionPlantilla.ToString(CultureInfo.InvariantCulture),
            ["zip/incluidos"] = string.Join(',', vehiculos.Where(v => v.Incluido).Select(v => v.Id.ToString("D"))),
        };
        var entrada = await HuellaEvidencia.LeerEntradaAsync(db, ClaveEvidencia.Zip(cabecera.Id), EmpaquetadorZip.VersionPlantilla, encabezado, ct)
            .ConfigureAwait(false);
        return new PlanZip(HuellaEvidencia.Calcular(entrada), vehiculos);
    }

    /// <summary>Con error si alguna celda quedó en ERROR; Terminado si todas están en COMPLETADO o N/A; si no, En proceso.</summary>
    private static string Estado(IReadOnlyCollection<EstadoCelda> celdas) =>
        celdas.Any(e => e == EstadoCelda.Error) ? ExcelLote.ConError
        : AvanceVehiculo.Terminado(celdas) ? ExcelLote.Terminado
        : ExcelLote.EnProceso;
}

/// <summary>
/// Empaquetador del ZIP del lote (HU #13850, decisiones Q2, Q5 y Q7 del 2026-10-22): toma un ZIP de la cola
/// (<see cref="ColaEvidencias"/>, entre compañías) y lo genera con un contexto de datos FIJADO a la empresa de la fila (como
/// <c>ProcesadorConsultas</c> con cada celda): todo lo que lee o escribe después de la toma (cabecera, celdas, personas, PDF del
/// vehículo, almacén) queda acotado por el filtro global a esa empresa, y la cabecera además se exige con su <c>tenant_id</c>.
/// <list type="number">
/// <item>Recalcula el plan con lo actual (<see cref="PlanZip"/>); si nada se puede emitir, ERROR <see cref="ErrorSinVehiculos"/>.</item>
/// <item>Por cada vehículo incluido, el PDF por <see cref="ServicioEvidencias"/>: si su huella coincide con la del PDF vigente se
/// reutilizan esos bytes; si no, se genera (<see cref="PdfVehiculo.Generar"/>) y queda como su evidencia PDF vigente.</item>
/// <item>Escribe el ZIP en streaming a un archivo temporal (nunca entero en memoria): un PDF por vehículo y <c>indice.txt</c> con
/// todos los vehículos (orden, identificador, estado y si van dentro; sin documentos). El temporal se borra siempre.</item>
/// <item>Lo sube al almacén desde el disco y deja la evidencia LISTA y vigente, con <c>generado_en</c>; la anterior vigente pasa a no
/// vigente (se conserva), salvo que la vigente sea de un pedido posterior.</item>
/// </list>
/// Un fallo de generación o del almacén vuelve a PENDIENTE con espera (backoff desde <see cref="OpcionesEvidencias.EsperaReintento"/>,
/// duplicándose) hasta <see cref="OpcionesEvidencias.MaxReintentos"/>; agotados, ERROR con <c>tipo_error</c>. Mientras genera renueva
/// el lease; si lo perdió (otra réplica lo retomó), abandona sin escribir. Los logs llevan ids y tipo de error, nunca datos del lote.
/// </summary>
internal sealed partial class EmpaquetadorZip(
    IServiceScopeFactory scopes, IOptions<OpcionesEvidencias> opciones, TimeProvider reloj, ILogger<EmpaquetadorZip> log)
{
    /// <summary>Versión de la plantilla del ZIP (estructura de entradas e índice); súbela al cambiar su diseño (entra en la huella).</summary>
    public const int VersionPlantilla = 1;

    public const string ContentType = "application/zip";

    public const string NombreIndice = "indice.txt";

    /// <summary>El almacén no guardó el ZIP.</summary>
    public const string ErrorAlmacen = "ALMACEN_NO_DISPONIBLE";

    /// <summary>Falló la lectura del lote o la generación (o el guardado) de un PDF.</summary>
    public const string ErrorGeneracion = "GENERACION_FALLIDA";

    /// <summary>El proceso murió con el ZIP tomado más veces de las permitidas.</summary>
    public const string ErrorInterrumpida = "GENERACION_INTERRUMPIDA";

    /// <summary>Al generarlo ya no había vehículos que pudieran emitir su PDF (p. ej. se reprocesó una celda).</summary>
    public const string ErrorSinVehiculos = "SIN_VEHICULOS_TERMINADOS";

    /// <summary><c>diagnostico-{8 primeros del id}.zip</c>.</summary>
    public static string NombreArchivo(Guid diagnosticoId) => $"diagnostico-{diagnosticoId.ToString()[..8]}.zip";

    /// <summary>Toma el siguiente ZIP de la cola y lo genera; false si la cola no tenía nada tomable.</summary>
    public async Task<bool> ProcesarSiguienteAsync(CancellationToken ct)
    {
        EvidenciaTomada? tomada;
        await using (var scope = scopes.CreateAsyncScope())
        {
            tomada = await scope.ServiceProvider.GetRequiredService<ColaEvidencias>()
                .TomarAsync(opciones.Value.DuracionLease, ct).ConfigureAwait(false);
        }

        if (tomada is null)
            return false;
        await ProcesarAsync(tomada, ct).ConfigureAwait(false);
        return true;
    }

    private async Task ProcesarAsync(EvidenciaTomada tomada, CancellationToken ct)
    {
        var o = opciones.Value;
        await using var scope = scopes.CreateAsyncScope();
        var servicios = scope.ServiceProvider;
        // La empresa del trabajo, fijada ANTES de leer nada: el filtro global y la validación al guardar usan esta empresa.
        await using var db = new DiagnosticoDb(servicios.GetRequiredService<DbContextOptions<DiagnosticoDb>>(), new EmpresaDelTrabajo(tomada.TenantId));
        var almacen = servicios.GetRequiredService<IFabricaAlmacen>().Para(db);
        var lector = ActivatorUtilities.CreateInstance<LectorContenidoReporte>(servicios, db);
        var evidencias = ActivatorUtilities.CreateInstance<ServicioEvidencias>(servicios, db, almacen);
        var trabajo = new Trabajo(tomada.Id, tomada.LeaseHasta);

        var evidencia = await db.Set<Evidencia>().AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == tomada.Id && e.TenantId == tomada.TenantId && e.Tipo == TipoEvidencia.Zip, ct).ConfigureAwait(false);
        if (evidencia is not { Estado: EstadoEvidencia.Generando } || evidencia.LeaseHasta != tomada.LeaseHasta)
        {
            LeasePerdido(log, tomada.Id);
            return;
        }

        if (tomada.Intentos > o.MaxReintentos + 1)
        {
            await FallarAsync(db, trabajo, ErrorInterrumpida, definitivo: true, ct).ConfigureAwait(false);
            return;
        }

        var ruta = Path.Combine(o.CarpetaTemporal(), $"flit-dgn-zip-{tomada.Id:N}-{Guid.NewGuid():N}.zip");
        var tipoError = ErrorGeneracion;
        try
        {
            var cabecera = await db.Set<CabeceraDiagnostico>().AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == tomada.DiagnosticoId && c.TenantId == tomada.TenantId, ct).ConfigureAwait(false)
                ?? throw ReglaException.NoEncontrado("El diagnóstico");
            var plan = await PlanZip.LeerAsync(db, lector, cabecera, evidencia.EmpresaNombre, ct).ConfigureAwait(false);
            if (plan.Huella != evidencia.Huella)
                HuellaCambiada(log, tomada.Id);
            if (plan.Incluidos.Count == 0)
            {
                await FallarAsync(db, trabajo, ErrorSinVehiculos, definitivo: true, ct).ConfigureAwait(false);
                return;
            }

            await EscribirAsync(ruta, plan, cabecera, evidencia, lector, evidencias, db, trabajo, ct).ConfigureAwait(false);
            tipoError = ErrorAlmacen;
            var guardado = await almacen.GuardarArchivoAsync(
                tomada.TenantId, CodigosDeEnum.Codigo(TipoEvidencia.Zip), NombreArchivo(cabecera.Id), ContentType, ruta, ct).ConfigureAwait(false);
            var vigente = await DejarListaAsync(db, trabajo, guardado, plan, ct).ConfigureAwait(false);
            Generado(log, tomada.Id, cabecera.Id, plan.Incluidos.Count, guardado.Tamano, vigente);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // El proceso se detiene: el ZIP queda GENERANDO con su lease y otra réplica lo retoma al vencer.
            throw;
        }
        catch (LeasePerdidoException)
        {
            LeasePerdido(log, tomada.Id);
        }
#pragma warning disable CA1031 // Un ZIP que falla no tumba el trabajador: vuelve a la cola con backoff o queda en ERROR.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Fallo(log, tomada.Id, tipoError, ex.GetType().Name, tomada.Intentos);
            await FallarAsync(db, trabajo, tipoError, definitivo: false, ct).ConfigureAwait(false);
        }
        finally
        {
            BorrarTemporal(ruta);
        }
    }

    /// <summary>Escribe el ZIP en <paramref name="ruta"/> PDF a PDF (cada uno se suelta al escribirlo) y al final el índice.</summary>
    private async Task EscribirAsync(
        string ruta, PlanZip plan, CabeceraDiagnostico cabecera, Evidencia evidencia, LectorContenidoReporte lector, ServicioEvidencias evidencias,
        DiagnosticoDb db, Trabajo trabajo, CancellationToken ct)
    {
        var archivos = new Dictionary<Guid, string>();
        var nombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { NombreIndice };
        await using var archivo = new FileStream(ruta, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using (var zip = new ZipArchive(archivo, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var vehiculo in plan.Incluidos)
            {
                var contenido = await lector.VehiculoAsync(cabecera, vehiculo.Id, evidencia.EmpresaNombre, ct).ConfigureAwait(false);
                var clave = ClaveEvidencia.Pdf(cabecera.Id, vehiculo.Id);
                var entrada = await HuellaEvidencia.LeerEntradaAsync(db, clave, PdfVehiculo.VersionPlantilla, contenido.ParaHuella(), ct).ConfigureAwait(false);
                var nombrePdf = PdfVehiculo.NombreArchivo(contenido.Encabezado);
                var pdf = await evidencias.ObtenerOGenerarAsync(
                    clave, HuellaEvidencia.Calcular(entrada), evidencia.SolicitadoPor,
                    _ => Task.FromResult(new ArchivoGenerado(PdfVehiculo.Generar(contenido, reloj.GetUtcNow()), nombrePdf, PdfVehiculo.ContentType)),
                    ct).ConfigureAwait(false);

                var nombre = Unico(nombres, nombrePdf, vehiculo.Orden);
                await using (var destino = zip.CreateEntry(nombre, CompressionLevel.Optimal).Open())
                    await destino.WriteAsync(pdf.Contenido, ct).ConfigureAwait(false);
                archivos[vehiculo.Id] = nombre;
                await RenovarLeaseSiHaceFaltaAsync(db, trabajo, ct).ConfigureAwait(false);
            }

            await using (var destino = zip.CreateEntry(NombreIndice, CompressionLevel.Optimal).Open())
            {
                var indice = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Indice(cabecera.Id, plan, archivos));
                await destino.WriteAsync(indice, ct).ConfigureAwait(false);
            }
        }

        await archivo.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// El índice del ZIP: todos los vehículos en el orden de la carga con su identificador (placa o VIN), su estado y si su PDF va
    /// dentro. Sin documentos de personas, sin nombres, sin origen ni causas de error.
    /// </summary>
    private string Indice(Guid diagnosticoId, PlanZip plan, Dictionary<Guid, string> archivos)
    {
        var texto = new StringBuilder();
        var generado = reloj.GetUtcNow().ToOffset(VigenciaDelDia.OffsetColombia);
        texto.Append(CultureInfo.InvariantCulture, $"Diagnóstico {diagnosticoId:D}\n");
        texto.Append(CultureInfo.InvariantCulture, $"Generado el {generado:yyyy-MM-dd HH:mm} (hora Colombia)\n");
        texto.Append(CultureInfo.InvariantCulture, $"Vehículos con PDF en este archivo: {archivos.Count} de {plan.Vehiculos.Count}\n\n");
        texto.Append("Orden\tIdentificador\tEstado\tEn el ZIP\tArchivo\n");
        foreach (var vehiculo in plan.Vehiculos)
        {
            var dentro = archivos.TryGetValue(vehiculo.Id, out var nombre);
            texto.Append(CultureInfo.InvariantCulture,
                $"{vehiculo.Orden}\t{vehiculo.Identificador}\t{vehiculo.Estado}\t{(dentro ? "Incluido" : "No incluido")}\t{nombre}\n");
        }

        return texto.ToString();
    }

    /// <summary>
    /// Deja el ZIP LISTA en una transacción: pasa a vigente e invalida la vigente anterior, salvo que la vigente sea de un pedido
    /// posterior (entonces queda LISTA y descargable por su id, sin ser la vigente). Devuelve si quedó vigente.
    /// </summary>
    private async Task<bool> DejarListaAsync(DiagnosticoDb db, Trabajo trabajo, ArchivoGuardado guardado, PlanZip plan, CancellationToken ct) =>
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            for (var intento = 1; ; intento++)
            {
                db.ChangeTracker.Clear();
                try
                {
                    await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                    var propia = await db.Set<Evidencia>().SingleOrDefaultAsync(e => e.Id == trabajo.Id, ct).ConfigureAwait(false);
                    if (propia is not { Estado: EstadoEvidencia.Generando } || propia.LeaseHasta != trabajo.LeaseHasta)
                        throw new LeasePerdidoException();
                    var anterior = await db.Set<Evidencia>()
                        .SingleOrDefaultAsync(e => e.DiagnosticoId == propia.DiagnosticoId && e.VehiculoId == null && e.Tipo == TipoEvidencia.Zip && e.Vigente, ct)
                        .ConfigureAwait(false);
                    var vigente = anterior is null || anterior.CreadoEn <= propia.CreadoEn;
                    if (vigente && anterior is not null)
                    {
                        // Primero se invalida la anterior: el índice de vigentes no admite las dos a la vez.
                        anterior.Vigente = false;
                        await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    }

                    propia.Estado = EstadoEvidencia.Lista;
                    propia.ArchivoRef = guardado.Referencia;
                    propia.TamanoBytes = guardado.Tamano;
                    propia.GeneradoEn = reloj.GetUtcNow();
                    propia.LeaseHasta = null;
                    propia.TipoError = null;
                    propia.Vigente = vigente;
                    propia.VehiculosIncluidos = plan.Incluidos.Count;
                    propia.VehiculosPendientes = plan.Pendientes;
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    await transaccion.CommitAsync(ct).ConfigureAwait(false);
                    return vigente;
                }
                catch (DbUpdateConcurrencyException) when (intento < 3)
                {
                    // Otro ZIP del lote cambió la vigente a la vez: se relee y se decide de nuevo.
                }
                catch (DbUpdateException ex) when (intento < 3 && ex.InnerException is PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: ServicioEvidencias.IndiceVigente,
                })
                {
                    // Otro ZIP del lote quedó vigente primero.
                }
            }
        }).ConfigureAwait(false);

    /// <summary>
    /// Un fallo: vuelve a PENDIENTE con espera (backoff) si quedan reintentos y no es <paramref name="definitivo"/>; si no, ERROR con
    /// <paramref name="tipoError"/>. Si el ZIP ya no es de esta toma (lease perdido), no lo toca.
    /// </summary>
    private async Task FallarAsync(DiagnosticoDb db, Trabajo trabajo, string tipoError, bool definitivo, CancellationToken ct)
    {
        var o = opciones.Value;
        db.ChangeTracker.Clear();
        var propia = await db.Set<Evidencia>().SingleOrDefaultAsync(e => e.Id == trabajo.Id, ct).ConfigureAwait(false);
        if (propia is not { Estado: EstadoEvidencia.Generando } || propia.LeaseHasta != trabajo.LeaseHasta)
        {
            LeasePerdido(log, trabajo.Id);
            return;
        }

        if (!definitivo && propia.Intentos <= o.MaxReintentos)
        {
            var espera = TimeSpan.FromTicks(o.EsperaReintento.Ticks << Math.Clamp(propia.Intentos - 1, 0, 10));
            propia.Estado = EstadoEvidencia.Pendiente;
            propia.LeaseHasta = Microsegundos(reloj.GetUtcNow() + espera);
            Reintento(log, trabajo.Id, tipoError, propia.Intentos, o.MaxReintentos, espera);
        }
        else
        {
            propia.Estado = EstadoEvidencia.Error;
            propia.TipoError = tipoError;
            propia.LeaseHasta = null;
            EnError(log, trabajo.Id, tipoError, propia.Intentos);
        }

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            LeasePerdido(log, trabajo.Id);
        }
    }

    /// <summary>Pasada la mitad del lease, lo extiende otro <see cref="OpcionesEvidencias.DuracionLease"/>; si ya no es suyo, aborta.</summary>
    private async Task RenovarLeaseSiHaceFaltaAsync(DiagnosticoDb db, Trabajo trabajo, CancellationToken ct)
    {
        var duracion = opciones.Value.DuracionLease;
        var ahora = reloj.GetUtcNow();
        if (ahora < trabajo.LeaseHasta - (duracion / 2))
            return;

        db.ChangeTracker.Clear();
        var propia = await db.Set<Evidencia>().SingleOrDefaultAsync(e => e.Id == trabajo.Id, ct).ConfigureAwait(false);
        if (propia is not { Estado: EstadoEvidencia.Generando } || propia.LeaseHasta != trabajo.LeaseHasta)
            throw new LeasePerdidoException();
        var nuevo = Microsegundos(ahora + duracion);
        propia.LeaseHasta = nuevo;
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new LeasePerdidoException();
        }

        trabajo.LeaseHasta = nuevo;
        db.ChangeTracker.Clear();
    }

    /// <summary>Dos vehículos con el mismo nombre de PDF (misma identificación) no se pisan: el segundo lleva su orden.</summary>
    private static string Unico(HashSet<string> nombres, string nombre, int orden)
    {
        if (nombres.Add(nombre))
            return nombre;
        var otro = $"{Path.GetFileNameWithoutExtension(nombre)}-{orden.ToString(CultureInfo.InvariantCulture)}{Path.GetExtension(nombre)}";
        nombres.Add(otro);
        return otro;
    }

    // timestamptz guarda microsegundos: lo que se compara después con lo leído de la base se trunca igual.
    private static DateTimeOffset Microsegundos(DateTimeOffset instante)
    {
        var utc = instante.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }

    private void BorrarTemporal(string ruta)
    {
        try
        {
            File.Delete(ruta);
        }
        catch (IOException ex)
        {
            TemporalNoBorrado(log, ex.GetType().Name);
        }
        catch (UnauthorizedAccessException ex)
        {
            TemporalNoBorrado(log, ex.GetType().Name);
        }
    }

    [LoggerMessage(EventId = 138500, Level = LogLevel.Information,
        Message = "ZIP {EvidenciaId} del diagnóstico {DiagnosticoId} generado: {Pdfs} PDF, {Bytes} bytes, vigente={Vigente}")]
    private static partial void Generado(ILogger logger, Guid evidenciaId, Guid diagnosticoId, int pdfs, long bytes, bool vigente);

    [LoggerMessage(EventId = 138501, Level = LogLevel.Warning, Message = "El ZIP {EvidenciaId} falló ({TipoError}, {Excepcion}) en el intento {Intento}")]
    private static partial void Fallo(ILogger logger, Guid evidenciaId, string tipoError, string excepcion, int intento);

    [LoggerMessage(EventId = 138502, Level = LogLevel.Information,
        Message = "El ZIP {EvidenciaId} vuelve a la cola tras {TipoError}: intento {Intento} de {Maximo} reintentos, espera {Espera}")]
    private static partial void Reintento(ILogger logger, Guid evidenciaId, string tipoError, int intento, int maximo, TimeSpan espera);

    [LoggerMessage(EventId = 138503, Level = LogLevel.Warning, Message = "El ZIP {EvidenciaId} queda en ERROR ({TipoError}) tras {Intentos} intentos")]
    private static partial void EnError(ILogger logger, Guid evidenciaId, string tipoError, int intentos);

    [LoggerMessage(EventId = 138504, Level = LogLevel.Warning, Message = "El ZIP {EvidenciaId} cambió de lease mientras se generaba: se abandona sin escribir")]
    private static partial void LeasePerdido(ILogger logger, Guid evidenciaId);

    [LoggerMessage(EventId = 138505, Level = LogLevel.Information,
        Message = "El lote del ZIP {EvidenciaId} cambió desde el pedido: se genera con lo actual")]
    private static partial void HuellaCambiada(ILogger logger, Guid evidenciaId);

    [LoggerMessage(EventId = 138506, Level = LogLevel.Warning, Message = "No se pudo borrar el temporal de un ZIP ({Excepcion})")]
    private static partial void TemporalNoBorrado(ILogger logger, string excepcion);

    /// <summary>La toma en curso: el id y el lease vigente (cambia al renovarlo).</summary>
    private sealed class Trabajo(Guid id, DateTimeOffset leaseHasta)
    {
        public Guid Id { get; } = id;

        public DateTimeOffset LeaseHasta { get; set; } = leaseHasta;
    }

    /// <summary>El ZIP ya no es de esta toma: otra réplica lo retomó al vencer el lease.</summary>
    private sealed class LeasePerdidoException : Exception
    {
        public LeasePerdidoException()
            : base("El lease del ZIP cambió mientras se generaba.")
        {
        }
    }

    /// <summary>La empresa del ZIP en generación: el contexto filtra y valida con ella, como una petición con su token.</summary>
    private sealed class EmpresaDelTrabajo(Guid empresa) : IPlatformTenantAccessor
    {
        public PlatformTenantContext Current { get; } = new(empresa, IsSuperAdmin: false, IsService: true);
    }
}
