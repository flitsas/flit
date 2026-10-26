using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;
using Flit.Platform.Grpc.V1;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Bus;

/// <summary>
/// Arma las consultas de una celda para el bus (ADR-0075, T6): el MISMO <c>*Request</c> proto que hoy envía el ejecutor de la
/// celda con su <c>Fuente*Real</c> (<c>ConstructoresSolicitudTests</c> lo compara con lo que capturan los clientes gRPC), sacado de
/// los <c>EjecutorCelda*</c> sin cambiarlos todavía. Solo lleva identificación: placa o VIN, tipo y número de documento o NIT,
/// los datos técnicos del avalúo o el departamento del impuesto; nunca nombres ni direcciones (U1 = b). Lee con el contexto de
/// la empresa de la celda y CON seguimiento: al liberar las celdas en el cierre de la RUNT, el vehículo y sus personas todavía
/// no están guardados y el contexto los tiene con lo que dejó la RUNT. Lista vacía si la celda no tiene qué consultar (sin
/// placa, sin personas, departamento no soportado): entonces no se publica nada y el ejecutor de hoy decide como siempre.
/// </summary>
internal interface IConstructorSolicitud
{
    Fuente Fuente { get; }

    /// <summary>Las consultas de la celda, en el orden en que hoy las hace su ejecutor.</summary>
    Task<IReadOnlyList<SolicitudConsulta>> ConstruirAsync(DiagnosticoDb db, CeldaConsulta celda, CancellationToken ct);
}

/// <summary>Lecturas comunes de los constructores.</summary>
internal static class DatosDeLaCelda
{
    /// <summary>El vehículo de la celda; si el contexto ya lo sigue, con sus cambios aún sin guardar.</summary>
    public static Task<VehiculoDiagnostico?> VehiculoAsync(DiagnosticoDb db, CeldaConsulta celda, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(celda);
        return db.Set<VehiculoDiagnostico>().SingleOrDefaultAsync(v => v.TenantId == celda.TenantId && v.Id == celda.VehiculoId, ct);
    }

    public static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}

/// <summary>RUNT del vehículo: <c>ConsultarPorVin</c> con un VIN y <c>ConsultarPorPlaca</c> en otro caso, como <see cref="FuenteRuntReal"/>.</summary>
internal sealed class ConstructorSolicitudRunt : IConstructorSolicitud
{
    public Fuente Fuente => Fuente.Runt;

    public async Task<IReadOnlyList<SolicitudConsulta>> ConstruirAsync(DiagnosticoDb db, CeldaConsulta celda, CancellationToken ct)
    {
        if (await DatosDeLaCelda.VehiculoAsync(db, celda, ct).ConfigureAwait(false) is not { } vehiculo)
            return [];
        var identificador = new IdentificadorVehiculo(vehiculo.TipoIdentificador, vehiculo.IdentificadorNorm);
        var valor = identificador.Valor.Trim();
        return identificador.EsVin
            ? [new SolicitudConsulta(FuentesBus.Runt, RpcConsultas.PorVin, new ConsultarPorVinRequest { Vin = valor })]
            : [new SolicitudConsulta(FuentesBus.Runt, RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = valor })];
    }
}

/// <summary>SIMIT por la placa definitiva del RUNT (<see cref="EjecutorCeldaSimitVehiculo"/>).</summary>
internal sealed class ConstructorSolicitudSimitVehiculo : IConstructorSolicitud
{
    public Fuente Fuente => Fuente.SimitVehiculo;

    public async Task<IReadOnlyList<SolicitudConsulta>> ConstruirAsync(DiagnosticoDb db, CeldaConsulta celda, CancellationToken ct)
    {
        var placa = DatosDeLaCelda.Limpio((await DatosDeLaCelda.VehiculoAsync(db, celda, ct).ConfigureAwait(false))?.Placa);
        return placa is null
            ? []
            : [new SolicitudConsulta(FuentesBus.SimitVehiculo, RpcConsultas.Multas, FuenteSimitReal.Solicitud(SolicitudSimit.PorPlaca(placa)))];
    }
}

/// <summary>Avalúo con la placa y el VIN del RUNT y sus datos técnicos (<see cref="EjecutorCeldaAvaluo.Solicitud"/>).</summary>
internal sealed class ConstructorSolicitudAvaluo : IConstructorSolicitud
{
    public Fuente Fuente => Fuente.Avaluo;

    public async Task<IReadOnlyList<SolicitudConsulta>> ConstruirAsync(DiagnosticoDb db, CeldaConsulta celda, CancellationToken ct)
    {
        var vehiculo = await DatosDeLaCelda.VehiculoAsync(db, celda, ct).ConfigureAwait(false);
        var placa = DatosDeLaCelda.Limpio(vehiculo?.Placa);
        var vin = DatosDeLaCelda.Limpio(vehiculo?.Vin);
        if (placa is null && vin is null)
            return [];
        var solicitud = EjecutorCeldaAvaluo.Solicitud(placa, vin, vehiculo!.DatosConsolidados);
        return [new SolicitudConsulta(FuentesBus.Avaluo, RpcConsultas.Avaluos, FuenteAvaluoReal.Solicitud(solicitud))];
    }
}

/// <summary>Impuesto departamental por la placa y el departamento, si sigue soportado en la réplica (<see cref="EjecutorCeldaImpuesto"/>).</summary>
internal sealed class ConstructorSolicitudImpuesto : IConstructorSolicitud
{
    public Fuente Fuente => Fuente.ImpuestoDepartamental;

    public async Task<IReadOnlyList<SolicitudConsulta>> ConstruirAsync(DiagnosticoDb db, CeldaConsulta celda, CancellationToken ct)
    {
        var vehiculo = await DatosDeLaCelda.VehiculoAsync(db, celda, ct).ConfigureAwait(false);
        var placa = DatosDeLaCelda.Limpio(vehiculo?.Placa);
        var departamento = DatosDeLaCelda.Limpio(vehiculo?.DepartamentoCodigo);
        if (placa is null || departamento is null || !await DerivacionDepartamento.SoportadoAsync(db, departamento, ct).ConfigureAwait(false))
            return [];
        var request = new ConsultarImpuestoVehicularRequest
        {
            Opciones = new OpcionesConsulta(),
            Placa = new Placa { Valor = placa },
            DepartamentoCodigo = departamento,
        };
        return [new SolicitudConsulta(FuentesBus.Impuesto, RpcConsultas.Impuesto, request)];
    }
}

/// <summary>
/// Base de las celdas de persona (<see cref="EjecutorPorPersonas"/>): una consulta por persona del rol, con su documento descifrado
/// justo aquí (<see cref="IProtectorDocumentoPersona"/>) y su <c>persona_id</c>. Como el ejecutor, no repite a las personas que ya
/// respondieron en la ejecución anterior (payload de la celda): un reproceso solo pide las que quedaron en error. Suma las personas
/// que la RUNT acaba de registrar en el mismo contexto y todavía no se guardaron.
/// </summary>
internal abstract class ConstructorPorPersonas(IProtectorDocumentoPersona protector) : IConstructorSolicitud
{
    public abstract Fuente Fuente { get; }

    /// <summary>La consulta de UNA persona; null si no hay qué consultar con ese documento.</summary>
    protected abstract SolicitudConsulta? Solicitud(DocumentoPersona documento, Guid personaId);

    public async Task<IReadOnlyList<SolicitudConsulta>> ConstruirAsync(DiagnosticoDb db, CeldaConsulta celda, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(celda);
        var respondieron = EjecutorPorPersonas.Leer(celda.Payload)?.Personas
            .Where(p => p.Estado == EjecutorPorPersonas.EstadoOk).Select(p => p.PersonaId).ToHashSet() ?? [];
        var solicitudes = new List<SolicitudConsulta>();
        foreach (var persona in await PersonasAsync(db, celda, ct).ConfigureAwait(false))
        {
            if (respondieron.Contains(persona.Id))
                continue;
            if (Solicitud(new DocumentoPersona(persona.DocTipo, protector.Desproteger(persona.DocCifrado)), persona.Id) is { } solicitud)
                solicitudes.Add(solicitud);
        }

        return solicitudes;
    }

    private async Task<List<PersonaVehiculo>> PersonasAsync(DiagnosticoDb db, CeldaConsulta celda, CancellationToken ct)
    {
        var rol = EjecutorPorPersonas.RolDe(Fuente);
        var guardadas = await DocumentosVehiculo.PersonasDelVehiculoAsync(db, celda.TenantId, celda.VehiculoId, ct, rol).ConfigureAwait(false);
        var nuevas = db.ChangeTracker.Entries<PersonaVehiculo>()
            .Where(e => e.State == EntityState.Added && e.Entity.TenantId == celda.TenantId && e.Entity.VehiculoId == celda.VehiculoId && e.Entity.Rol == rol)
            .Select(e => e.Entity)
            .Where(n => guardadas.TrueForAll(g => g.Id != n.Id));
        return [.. guardadas.Concat(nuevas).OrderBy(p => p.Orden)];
    }
}

/// <summary>
/// RUNT_PROPIETARIO y RUNT_LOCATARIO (<see cref="EjecutorCeldaRuntPersona"/>): la persona natural a <c>ConsultarConductor</c> con
/// el tipo en mayúsculas; la persona con NIT a <c>ConsultarRues</c> con el NIT sin dígito de verificación (fuente del bus
/// <see cref="FuentesBus.Rues"/>).
/// </summary>
internal sealed class ConstructorSolicitudRuntPersona : ConstructorPorPersonas
{
    public ConstructorSolicitudRuntPersona(Fuente fuente, IProtectorDocumentoPersona protector)
        : base(protector)
    {
        if (fuente is not (Fuente.RuntPropietario or Fuente.RuntLocatario))
            throw new ArgumentOutOfRangeException(nameof(fuente), fuente, "El constructor RUNT persona atiende RUNT_PROPIETARIO o RUNT_LOCATARIO.");
        Fuente = fuente;
    }

    public override Fuente Fuente { get; }

    protected override SolicitudConsulta? Solicitud(DocumentoPersona documento, Guid personaId)
    {
        if (documento.EsNit)
        {
            var nit = EjecutorCeldaRuntPersona.NitSinDigito(documento.Numero);
            return nit.Length == 0
                ? null
                : new SolicitudConsulta(FuentesBus.Rues, RpcConsultas.Rues, new ConsultarRuesRequest { Opciones = new OpcionesConsulta(), Nit = nit }, personaId);
        }

        var request = new ConsultarConductorRequest
        {
            Opciones = new OpcionesConsulta(),
            Documento = new DocumentoIdentidad { Tipo = documento.Tipo.Trim().ToUpperInvariant(), Numero = documento.Numero },
        };
        return new SolicitudConsulta(FuentesBus.De(Fuente), RpcConsultas.Conductor, request, personaId);
    }
}

/// <summary>SIMIT_PROPIETARIO y SIMIT_LOCATARIO por documento (<see cref="EjecutorCeldaSimitPersona"/>): NIT como persona jurídica.</summary>
internal sealed class ConstructorSolicitudSimitPersona : ConstructorPorPersonas
{
    public ConstructorSolicitudSimitPersona(Fuente fuente, IProtectorDocumentoPersona protector)
        : base(protector)
    {
        if (fuente is not (Fuente.SimitPropietario or Fuente.SimitLocatario))
            throw new ArgumentOutOfRangeException(nameof(fuente), fuente, "El constructor SIMIT por persona atiende SIMIT_PROPIETARIO o SIMIT_LOCATARIO.");
        Fuente = fuente;
    }

    public override Fuente Fuente { get; }

    protected override SolicitudConsulta? Solicitud(DocumentoPersona documento, Guid personaId) => new(
        FuentesBus.De(Fuente), RpcConsultas.Multas,
        FuenteSimitReal.Solicitud(SolicitudSimit.PorDocumento(EjecutorCeldaSimitPersona.Normalizar(documento))), personaId);
}
