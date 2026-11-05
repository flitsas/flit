using System.Text.RegularExpressions;
using Flit.Diagnostico.Api.Infraestructura.Archivos;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>El archivo que produce un generador (PDF o Excel): bytes, nombre y tipo de contenido.</summary>
public sealed record ArchivoGenerado(byte[] Contenido, string Nombre, string ContentType);

/// <summary>
/// Lo que se entrega al pedir una evidencia: la fila vigente, su huella, los bytes y cuándo se generó. <see cref="Reutilizada"/> es
/// true si no se generó en esta petición (misma huella, o la ganó otro pedido simultáneo).
/// </summary>
public sealed record EvidenciaEntregada(Guid Id, string Huella, byte[] Contenido, DateTimeOffset GeneradoEn, bool Reutilizada);

/// <summary>
/// Registro de evidencias (HU #13846, F07-C8, F07-C11, F07-C12, R-18) sobre la empresa del contexto (filtro global, ADR-0072).
/// <see cref="ObtenerOGenerarAsync"/>: si la vigente de la clave está LISTA con la misma huella y su archivo existe, devuelve sus
/// bytes sin llamar al generador; si no, genera, guarda en el almacén y, en una transacción, pasa la anterior a
/// <c>vigente=false</c> (se conserva) e inserta la nueva vigente con <c>solicitado_por</c> y <c>generado_en</c>. Dos pedidos a la
/// vez: el perdedor choca con <c>ux_evidencias_vigente</c> (23505) o con el <c>xmin</c> de la anterior, relee y entrega la
/// ganadora (patrón de idempotencia del plan §3.1). Solo PDF y Excel: el ZIP lo genera la cola de F07-H5.
/// </summary>
public sealed partial class ServicioEvidencias(DiagnosticoDb db, IAlmacenArchivos almacen, TimeProvider reloj, ILogger<ServicioEvidencias> log)
{
    /// <summary>Índice de una sola vigente por clave (<c>EvidenciaConfig</c>).</summary>
    public const string IndiceVigente = "ux_evidencias_vigente";

    private const int MaximoReintentos = 3;

    /// <summary>
    /// La evidencia vigente de <paramref name="clave"/> con <paramref name="huella"/>: la guardada o una nueva hecha con
    /// <paramref name="generar"/>. 404 (<see cref="ReglaException.NoEncontrado"/>) si el lote, o el vehículo dentro de ese lote, no es
    /// de la empresa del contexto; en ese caso no se genera nada.
    /// </summary>
    public async Task<EvidenciaEntregada> ObtenerOGenerarAsync(
        ClaveEvidencia clave, string huella, Guid solicitadoPor, Func<CancellationToken, Task<ArchivoGenerado>> generar, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(clave);
        ArgumentNullException.ThrowIfNull(generar);
        if (clave.Tipo == TipoEvidencia.Zip)
            throw new ArgumentException("El ZIP no se genera en la petición: lo genera la cola en segundo plano (F07-H5).", nameof(clave));
        if (huella is null || !FormatoHuella().IsMatch(huella))
            throw new ArgumentException("La huella es un SHA-256 en hexadecimal minúsculo (64 caracteres).", nameof(huella));
        if (solicitadoPor == Guid.Empty)
            throw new ArgumentException("La evidencia exige el usuario que la pide.", nameof(solicitadoPor));
        await ExigirClaveDeLaEmpresaAsync(clave, ct).ConfigureAwait(false);

        if (await EntregableAsync(clave, huella, ct).ConfigureAwait(false) is { } guardada)
            return guardada;

        var archivo = await generar(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("El generador de la evidencia no devolvió archivo.");
        if (archivo.Contenido is not { Length: > 0 })
            throw new InvalidOperationException("El generador de la evidencia devolvió un archivo vacío.");
        var empresa = db.EmpresaActual ?? throw ReglaException.NoEncontrado("El diagnóstico");
        var guardado = await almacen.GuardarAsync(empresa, CodigosDeEnum.Codigo(clave.Tipo), archivo.Nombre, archivo.ContentType, archivo.Contenido, ct)
            .ConfigureAwait(false);

        for (var intento = 1; ; intento++)
        {
            try
            {
                // El reintento de Npgsql exige que la transacción del usuario vaya dentro de la estrategia de ejecución.
                var entregada = await db.Database.CreateExecutionStrategy()
                    .ExecuteAsync(() => ReemplazarAsync(clave, huella, solicitadoPor, guardado, archivo.Contenido, ct)).ConfigureAwait(false);
                if (!entregada.Reutilizada)
                    EvidenciaGenerada(log, clave.Tipo, clave.DiagnosticoId, entregada.Id, guardado.Tamano);
                return entregada;
            }
            catch (DbUpdateConcurrencyException) when (intento < MaximoReintentos)
            {
                // Otro pedido invalidó la misma vigente: relee y entrega la ganadora si es de esta huella.
            }
            catch (DbUpdateException ex) when (intento < MaximoReintentos && EsVigenteDuplicada(ex))
            {
                // Otro pedido insertó primero la vigente de esta clave.
            }

            db.ChangeTracker.Clear();
            if (await EntregableAsync(clave, huella, ct).ConfigureAwait(false) is { } ganadora)
                return ganadora;
        }
    }

    /// <summary>La evidencia vigente de <paramref name="clave"/> en la empresa del contexto (sin seguimiento); null si no hay.</summary>
    public Task<Evidencia?> ObtenerVigenteAsync(ClaveEvidencia clave, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(clave);
        return Vigentes(clave).AsNoTracking().SingleOrDefaultAsync(ct);
    }

    private IQueryable<Evidencia> Vigentes(ClaveEvidencia clave) =>
        db.Set<Evidencia>().Where(e => e.DiagnosticoId == clave.DiagnosticoId && e.VehiculoId == clave.VehiculoId && e.Tipo == clave.Tipo && e.Vigente);

    private async Task ExigirClaveDeLaEmpresaAsync(ClaveEvidencia clave, CancellationToken ct)
    {
        if (!await db.Set<CabeceraDiagnostico>().AnyAsync(c => c.Id == clave.DiagnosticoId, ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("El diagnóstico");
        if (clave.VehiculoId is { } vehiculo
            && !await db.Set<VehiculoDiagnostico>().AnyAsync(v => v.Id == vehiculo && v.DiagnosticoId == clave.DiagnosticoId, ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("El vehículo");
    }

    /// <summary>La vigente LISTA con la misma huella y su archivo, si existe; null si hay que generar.</summary>
    private async Task<EvidenciaEntregada?> EntregableAsync(ClaveEvidencia clave, string huella, CancellationToken ct)
    {
        var vigente = await ObtenerVigenteAsync(clave, ct).ConfigureAwait(false);
        if (vigente is not { Estado: EstadoEvidencia.Lista, ArchivoRef: { } referencia } || vigente.Huella != huella)
            return null;

        var bytes = await almacen.LeerAsync(referencia, ct).ConfigureAwait(false);
        if (bytes is null)
        {
            ArchivoPerdido(log, clave.Tipo, clave.DiagnosticoId, vigente.Id);
            return null;
        }

        return new EvidenciaEntregada(vigente.Id, vigente.Huella, bytes, vigente.GeneradoEn ?? vigente.CreadoEn, Reutilizada: true);
    }

    private async Task<EvidenciaEntregada> ReemplazarAsync(
        ClaveEvidencia clave, string huella, Guid solicitadoPor, ArchivoGuardado guardado, byte[] contenido, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var anterior = await Vigentes(clave).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (anterior is { Estado: EstadoEvidencia.Lista, ArchivoRef: not null } && anterior.Huella == huella)
        {
            // Otro pedido ya dejó vigente esta misma huella mientras se generaba: se entrega la suya.
            var suya = await almacen.LeerAsync(anterior.ArchivoRef, ct).ConfigureAwait(false);
            if (suya is not null)
                return new EvidenciaEntregada(anterior.Id, anterior.Huella, suya, anterior.GeneradoEn ?? anterior.CreadoEn, Reutilizada: true);
        }

        if (anterior is not null)
        {
            // Primero se invalida la anterior y después se inserta la nueva: el índice de vigentes no admite las dos a la vez.
            anterior.Vigente = false;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var ahora = reloj.GetUtcNow();
        var nueva = new Evidencia
        {
            DiagnosticoId = clave.DiagnosticoId,
            VehiculoId = clave.VehiculoId,
            Tipo = clave.Tipo,
            Huella = huella,
            Estado = EstadoEvidencia.Lista,
            ArchivoRef = guardado.Referencia,
            TamanoBytes = guardado.Tamano,
            SolicitadoPor = solicitadoPor,
            Vigente = true,
            Intentos = 1,
            CreadoEn = ahora,
            GeneradoEn = ahora,
        };
        db.Add(nueva);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaccion.CommitAsync(ct).ConfigureAwait(false);
        return new EvidenciaEntregada(nueva.Id, huella, contenido, ahora, Reutilizada: false);
    }

    private static bool EsVigenteDuplicada(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: IndiceVigente };

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatoHuella();

    [LoggerMessage(EventId = 13846, Level = LogLevel.Information,
        Message = "Evidencia {Tipo} del diagnóstico {DiagnosticoId} generada ({EvidenciaId}, {Bytes} bytes).")]
    private static partial void EvidenciaGenerada(ILogger logger, TipoEvidencia tipo, Guid diagnosticoId, Guid evidenciaId, long bytes);

    [LoggerMessage(EventId = 13847, Level = LogLevel.Warning,
        Message = "La evidencia {Tipo} {EvidenciaId} del diagnóstico {DiagnosticoId} está vigente pero el almacén no tiene su archivo: se regenera.")]
    private static partial void ArchivoPerdido(ILogger logger, TipoEvidencia tipo, Guid diagnosticoId, Guid evidenciaId);
}
