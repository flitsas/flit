using System.Globalization;
using Flit.Diagnostico.Api.Modulos.Carga;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Estimacion;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Express;

/// <summary>
/// Diagnóstico Express de un solo vehículo (HU #13753, F02-H6, F02-C4/C12/C13, F01-C10/C11). Primero revalida la fila con
/// <see cref="ValidadorFilaVehiculo"/> (la misma regla de la carga y de la confirmación): un identificador o documento
/// inválido responde 422 con su campo y no se abre nada. Después, en una sola transacción, crea la cabecera con
/// <see cref="ServicioCabecera"/> (<c>es_express=true</c>, reglas de F01-H4) y confirma el vehículo con
/// <see cref="ServicioConfirmacionVehiculos"/> (documento del locatario cifrado con origen DECLARADO_CLIENTE, estimado en la
/// cabecera y entrega al encolador); si algo falla no queda cabecera huérfana. Sin nombre, se autogenera
/// «Express &lt;identificador&gt; &lt;dd/mm/aaaa&gt;» con la fecha de Colombia, igual que la app (#13752). El estimado de vista
/// previa usa la configuración vigente de la compañía y no persiste nada. Los logs solo llevan el id.
/// </summary>
internal sealed partial class ServicioExpress(
    DiagnosticoDb db,
    UsuarioActual usuario,
    ServicioCabecera cabeceras,
    ServicioConfirmacionVehiculos confirmacion,
    TimeProvider reloj,
    ILogger<ServicioExpress> log)
{
    public const string CampoIdentificador = "identificador";
    public const string CampoTipoDocumento = "tipo_documento_locatario";
    public const string CampoDocumento = "documento_locatario";

    /// <summary>Colombia no tiene horario de verano: UTC-5 todo el año.</summary>
    private static readonly TimeSpan HoraColombia = TimeSpan.FromHours(-5);

    public async Task<(CabeceraDiagnostico Cabecera, ConfirmacionLote Confirmacion)> CrearAsync(ExpressEntradaDto entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        usuario.EmpresaPropia();
        var fila = ValidadorFilaVehiculo.Validar(entrada.Identificador, entrada.TipoDocumentoLocatario, entrada.DocumentoLocatario);
        if (!fila.EsValida)
        {
            var error = fila.Errores[0];
            throw new ReglaException(error.Codigo, error.Mensaje) { Campo = Campo(error.Campo) };
        }

        var nombre = string.IsNullOrWhiteSpace(entrada.Nombre) ? NombreAutogenerado(fila.Normalizado, reloj.GetUtcNow()) : entrada.Nombre;
        var cabeceraEntrada = new CabeceraEntradaDto(nombre, entrada.Modo, entrada.TipoTramiteCodigo, entrada.Alcance);
        VehiculoCapturado[] capturados = [new(entrada.Identificador, entrada.TipoDocumentoLocatario, entrada.DocumentoLocatario)];

        // El reintento de Npgsql exige que la transacción del usuario vaya dentro de la estrategia de ejecución.
        var creado = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var cabecera = await cabeceras.CrearAsync(cabeceraEntrada, ct, esExpress: true).ConfigureAwait(false);
            var confirmado = await confirmacion.GuardarEnTransaccionAbiertaAsync(cabecera.Id, capturados, ct).ConfigureAwait(false);
            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            return (cabecera, confirmado);
        }).ConfigureAwait(false);
        ExpressCreado(log, creado.cabecera.Id);
        return creado;
    }

    /// <summary>Estimado por fuente del único vehículo con la configuración vigente; sin <c>ANTES_DE_LANZAR</c> no hay estimado.</summary>
    public async Task<EstimadoExpressDto> EstimarAsync(ExpressEntradaDto entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        usuario.EmpresaPropia();
        var alcance = ServicioCabecera.LeerAlcance(entrada.Alcance);
        var documento = ValidadorDocumentoPersona.Validar(entrada.TipoDocumentoLocatario, entrada.DocumentoLocatario);
        if (!documento.EsValido)
        {
            throw new ReglaException(documento.Codigo!, documento.Mensaje!)
            {
                Campo = documento.Codigo == ValidadorDocumentoPersona.CodigoTipoInvalido ? CampoTipoDocumento : CampoDocumento,
            };
        }

        var copia = await CopiaConfiguracion.TomarAsync(db, ct).ConfigureAwait(false);
        if (copia.EstimadoConsultas != EstimadoConsultas.AntesDeLanzar)
            return new EstimadoExpressDto(false, null);

        var estimado = EstimadorConsultas.Estimar(alcance.Fuentes, [new VehiculoAEstimar(true, documento.Presente)]);
        return new EstimadoExpressDto(true, [.. estimado.Select(EstimadoFuenteDto.De)]);
    }

    /// <summary>«Express &lt;identificador normalizado&gt; &lt;dd/mm/aaaa&gt;» con el día en la hora de Colombia.</summary>
    internal static string NombreAutogenerado(string identificadorNorm, DateTimeOffset ahora) =>
        $"Express {identificadorNorm} {ahora.ToOffset(HoraColombia).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";

    private static string Campo(CampoFilaVehiculo campo) => campo switch
    {
        CampoFilaVehiculo.TipoDocumento => CampoTipoDocumento,
        CampoFilaVehiculo.Documento => CampoDocumento,
        _ => CampoIdentificador,
    };

    [LoggerMessage(EventId = 13753, Level = LogLevel.Information, Message = "Diagnóstico Express {DiagnosticoId} creado con un vehículo")]
    private static partial void ExpressCreado(ILogger logger, Guid diagnosticoId);
}
