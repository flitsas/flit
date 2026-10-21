using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Carga;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Diagnostico.Api.Modulos.Vehiculos;

/// <summary>Lo que dejó una confirmación: la cabecera, la revalidación y las filas guardadas.</summary>
internal sealed record ConfirmacionLote(Guid DiagnosticoId, ResultadoConfirmacion Resultado, IReadOnlyList<VehiculoDiagnostico> Vehiculos, bool MostrarEstimado);

/// <summary>
/// Confirma los vehículos de un lote (HU #13751, F02-H5, F02-C4/C9/C10/C11/C13/C14); la captura Express (#13753) lo reusa
/// con un solo vehículo. Solo con la cabecera en CREADO (409 <see cref="ServicioCabecera.CodigoNoEditable"/>) y sin
/// vehículos confirmados antes (409 <see cref="CodigoYaConfirmados"/>); una cabecera de otra compañía responde 404. El
/// servidor revalida todo (<see cref="ConfirmadorVehiculos"/>): sin válidos o por encima del límite de la copia de
/// configuración responde 422 sin guardar nada. En una transacción: guarda los válidos (documento del locatario cifrado
/// con <see cref="IProtectorDocumentoPersona"/> y origen DECLARADO_CLIENTE, R-3), deja el estimado en la cabecera y
/// entrega la lista limpia a <see cref="IEncoladorConsultas"/>; si el encolador falla no queda nada. No cambia el estado
/// de la cabecera. Los logs solo llevan conteos.
/// </summary>
internal sealed partial class ServicioConfirmacionVehiculos(
    DiagnosticoDb db, UsuarioActual usuario, IProtectorDocumentoPersona protector, IEncoladorConsultas encolador, ILogger<ServicioConfirmacionVehiculos> log)
{
    public const string CodigoSinValidos = AnalizadorCarga.CodigoSinValidos;
    public const string CodigoLimiteSuperado = AnalizadorCarga.CodigoLimiteSuperado;
    public const string CodigoYaConfirmados = "VEHICULOS_YA_CONFIRMADOS";
    public const string CampoVehiculos = "vehiculos";

    public async Task<ConfirmacionLote> ConfirmarAsync(Guid diagnosticoId, IReadOnlyList<VehiculoCapturado> capturados, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(capturados);
        usuario.EmpresaPropia();
        try
        {
            // El reintento de Npgsql exige que la transacción del usuario vaya dentro de la estrategia de ejecución.
            var confirmacion = await db.Database.CreateExecutionStrategy()
                .ExecuteAsync(() => ConfirmarEnTransaccionAsync(diagnosticoId, capturados, ct)).ConfigureAwait(false);
            VehiculosConfirmados(log, diagnosticoId, confirmacion.Vehiculos.Count, confirmacion.Resultado.Invalidos, confirmacion.Resultado.Duplicados);
            return confirmacion;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw YaConfirmados();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw YaConfirmados();
        }
    }

    private async Task<ConfirmacionLote> ConfirmarEnTransaccionAsync(Guid diagnosticoId, IReadOnlyList<VehiculoCapturado> capturados, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var confirmacion = await GuardarEnTransaccionAbiertaAsync(diagnosticoId, capturados, ct).ConfigureAwait(false);
        await transaccion.CommitAsync(ct).ConfigureAwait(false);
        return confirmacion;
    }

    /// <summary>
    /// Revalida, guarda los válidos, deja el estimado en la cabecera y entrega la lista al encolador dentro de la transacción
    /// que ya abrió quien llama (la confirmación del lote o el Express, HU #13753, que crea la cabecera en la misma); no
    /// confirma ni revierte: eso lo decide quien abrió la transacción.
    /// </summary>
    internal async Task<ConfirmacionLote> GuardarEnTransaccionAbiertaAsync(Guid diagnosticoId, IReadOnlyList<VehiculoCapturado> capturados, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(capturados);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("La confirmación de vehículos exige una transacción abierta.");
        var cabecera = await db.Set<CabeceraDiagnostico>().SingleOrDefaultAsync(c => c.Id == diagnosticoId, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("El diagnóstico");
        if (cabecera.Estado != EstadoDiagnostico.Creado)
            throw ReglaException.Conflicto(ServicioCabecera.CodigoNoEditable, "El diagnóstico ya no está en CREADO: no admite confirmar vehículos.");
        if (await db.Set<VehiculoDiagnostico>().AnyAsync(v => v.DiagnosticoId == diagnosticoId, ct).ConfigureAwait(false))
            throw YaConfirmados();

        var parametros = CargaEndpoints.Parametros(cabecera);
        var resultado = ConfirmadorVehiculos.Confirmar(capturados, cabecera.Alcance, parametros.LimiteVehiculosLote);
        if (resultado.SinValidos)
        {
            throw new ReglaException(CodigoSinValidos, "No hay ningún vehículo válido para confirmar: corrija los identificadores o cargue otro archivo.")
            {
                Campo = CampoVehiculos,
            };
        }

        if (resultado.SuperaLimite)
        {
            throw new ReglaException(CodigoLimiteSuperado,
                $"El lote tiene {resultado.Validos.Count} vehículos válidos y el límite por lote es {resultado.LimiteVehiculosLote}: divida el lote o retire vehículos.")
            {
                Campo = CampoVehiculos,
            };
        }

        var vehiculos = resultado.Validos.Select(v => Nuevo(cabecera.Id, v)).ToList();
        db.AddRange(vehiculos);
        cabecera.EstimadoConsultas = JsonSerializer.Serialize(resultado.Estimado.ToDictionary(e => CodigosDeEnum.Codigo(e.Fuente), e => e.Consultas));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await encolador.EncolarAsync(
            cabecera.Id, [.. vehiculos.Select(v => new VehiculoAEncolar(v.Id, v.IdentificadorNorm, v.TipoIdentificador))], ct).ConfigureAwait(false);
        return new ConfirmacionLote(cabecera.Id, resultado, vehiculos, parametros.MostrarEstimado);
    }

    private VehiculoDiagnostico Nuevo(Guid diagnosticoId, VehiculoConfirmado v)
    {
        var conDocumento = v.DocumentoLocatario is not null;
        return new VehiculoDiagnostico
        {
            DiagnosticoId = diagnosticoId,
            IdentificadorOriginal = v.IdentificadorOriginal,
            IdentificadorNorm = v.IdentificadorNorm,
            TipoIdentificador = v.Tipo,
            Placa = v.Tipo == TipoIdentificador.Vin ? null : v.IdentificadorNorm,
            Vin = v.Tipo == TipoIdentificador.Vin ? v.IdentificadorNorm : null,
            LocatarioDocTipo = conDocumento ? v.TipoDocumentoLocatario : null,
            LocatarioDocCifrado = conDocumento ? protector.Proteger(v.DocumentoLocatario!) : null,
            LocatarioDocOrigen = conDocumento ? OrigenDocumento.DeclaradoCliente : null,
            Orden = v.Orden,
        };
    }

    private static ReglaException YaConfirmados() =>
        ReglaException.Conflicto(CodigoYaConfirmados, "El diagnóstico ya tiene sus vehículos confirmados.");

    [LoggerMessage(EventId = 13751, Level = LogLevel.Information,
        Message = "Vehículos confirmados del diagnóstico {DiagnosticoId}: {Confirmados} guardados, {Invalidos} inválidos y {Duplicados} duplicados descartados")]
    private static partial void VehiculosConfirmados(ILogger logger, Guid diagnosticoId, int confirmados, int invalidos, int duplicados);
}
