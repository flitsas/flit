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
/// Arma las consultas de una celda para el bus (ADR-0075, T6): el MISMO <c>*Request</c> proto que antes enviaba por gRPC la
/// <c>Fuente*Real</c> de la celda (<c>ConstructoresSolicitudTests</c> lo fija campo por campo). Es la mitad «construir solicitud» de
/// cada <see cref="IEjecutorCelda"/>. Solo lleva identificación: placa o VIN, tipo y número de documento o NIT, los datos técnicos
/// del avalúo o el departamento del impuesto; nunca nombres ni direcciones (U1 = b). Lee con el contexto de la empresa de la celda
/// y CON seguimiento (lo que el contexto tenga sin guardar cuenta). Lista vacía si la celda no tiene qué consultar (sin placa, sin
/// personas, departamento no soportado): entonces no se publica nada y el ejecutor decide el cierre
/// (<see cref="IEjecutorCelda.SinConsultaAsync"/>).
/// </summary>
internal interface IConstructorSolicitud
{
    Fuente Fuente { get; }

    /// <summary>Las consultas de la celda, en el orden en que hoy las hace su ejecutor.</summary>
    Task<IReadOnlyList<SolicitudConsulta>> ConstruirAsync(DiagnosticoDb db, CeldaConsulta celda, CancellationToken ct);
}

/// <summary>
/// Los <c>*Request</c> de SIMIT y del avalúo, iguales a los que enviaban <c>FuenteSimitReal</c> y <c>FuenteAvaluoReal</c>: por placa
/// solo <c>placa</c> (core-consultas elige el proveedor con la configuración de la empresa); por persona <c>documento</c> y
/// <c>tipo_persona</c> JURIDICA para NIT o NATURAL en otro caso; el avalúo con placa y VIN si los hay y los datos técnicos (0 o
/// vacío si faltan).
/// </summary>
internal static class SolicitudesProto
{
    /// <summary>La solicitud de SIMIT: por placa o por documento, nunca los dos.</summary>
    public static ConsultarMultasRequest Multas(SolicitudSimit solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var request = new ConsultarMultasRequest { Opciones = new OpcionesConsulta() };
        if (solicitud.Documento is { } documento)
        {
            request.Documento = new DocumentoIdentidad { Tipo = documento.Tipo.Trim().ToUpperInvariant(), Numero = documento.Numero };
            request.TipoPersona = solicitud.EsJuridica ? TipoPersona.Juridica : TipoPersona.Natural;
        }
        else
        {
            request.Placa = new Placa { Valor = solicitud.Placa };
        }

        return request;
    }

    /// <summary>La solicitud del avalúo: placa y VIN si los hay y los datos técnicos (0 o vacío si faltan).</summary>
    public static ConsultarAvaluosRequest Avaluos(SolicitudAvaluo solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var request = new ConsultarAvaluosRequest
        {
            Opciones = new OpcionesConsulta(),
            Modelo = solicitud.Modelo ?? 0,
            Cilindraje = solicitud.Cilindraje ?? 0,
            Combustible = solicitud.Combustible ?? string.Empty,
            Pasajeros = solicitud.Pasajeros ?? 0,
        };
        if (!string.IsNullOrWhiteSpace(solicitud.Placa))
            request.Placa = new Placa { Valor = solicitud.Placa };
        if (!string.IsNullOrWhiteSpace(solicitud.Vin))
            request.Vin = new Vin { Valor = solicitud.Vin };
        return request;
    }
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

/// <summary>RUNT del vehículo: <c>ConsultarPorVin</c> con un VIN y <c>ConsultarPorPlaca</c> en otro caso (G2=b, HU #13878).</summary>
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

/// <summary>
/// SIMIT por la placa definitiva del RUNT (<see cref="EjecutorCeldaSimitVehiculo"/>). Bug SIMIT por placa (2026-10-27): además de la
/// placa lleva el documento del primer propietario (por <c>orden</c>) que registró el RUNT, descifrado solo en memoria, para el
/// proveedor de multas que exige documento (core-consultas manda solo la placa al que consulta por placa). Sin tipo de persona: el
/// proveedor externo se elige como en la consulta por placa. Sin propietario registrado, solo la placa.
/// </summary>
internal sealed class ConstructorSolicitudSimitVehiculo(IProtectorDocumentoPersona protector) : IConstructorSolicitud
{
    public Fuente Fuente => Fuente.SimitVehiculo;

    public async Task<IReadOnlyList<SolicitudConsulta>> ConstruirAsync(DiagnosticoDb db, CeldaConsulta celda, CancellationToken ct)
    {
        var placa = DatosDeLaCelda.Limpio((await DatosDeLaCelda.VehiculoAsync(db, celda, ct).ConfigureAwait(false))?.Placa);
        if (placa is null)
            return [];

        var request = SolicitudesProto.Multas(SolicitudSimit.PorPlaca(placa));
        var propietario = (await EjecutorPorPersonas.PersonasDelRolAsync(db, celda, RolPersona.Propietario, ct).ConfigureAwait(false)).FirstOrDefault();
        if (propietario is not null)
        {
            var documento = EjecutorCeldaSimitPersona.Normalizar(new DocumentoPersona(propietario.DocTipo, protector.Desproteger(propietario.DocCifrado)));
            if (documento.Numero.Length > 0)
                request.Documento = new DocumentoIdentidad { Tipo = documento.Tipo, Numero = documento.Numero };
        }

        return [new SolicitudConsulta(FuentesBus.SimitVehiculo, RpcConsultas.Multas, request)];
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
        return [new SolicitudConsulta(FuentesBus.Avaluo, RpcConsultas.Avaluos, SolicitudesProto.Avaluos(solicitud))];
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
        foreach (var persona in await EjecutorPorPersonas.PersonasDelRolAsync(db, celda, ct).ConfigureAwait(false))
        {
            if (respondieron.Contains(persona.Id))
                continue;
            if (Solicitud(new DocumentoPersona(persona.DocTipo, protector.Desproteger(persona.DocCifrado)), persona.Id) is { } solicitud)
                solicitudes.Add(solicitud);
        }

        return solicitudes;
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
        SolicitudesProto.Multas(SolicitudSimit.PorDocumento(EjecutorCeldaSimitPersona.Normalizar(documento))), personaId);
}
