using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Estimacion;
using Flit.Diagnostico.Domain.Vehiculos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>
/// Arma la grilla incremental del tablero (HU #13743, F04-C1/C3/C8/C10/C11/C13): con <c>desde=0</c> todo el lote; con
/// <c>desde=N</c> solo las celdas con <c>version_tablero &gt; N</c> (polling cada 3 s, ADR c) y la versión actual del lote.
/// El estado global, las columnas del alcance agrupadas y el avance van siempre. Lee con el contexto de la petición (filtro de
/// la compañía del token) y la visibilidad de <see cref="AccesoLote"/>. Nunca proyecta el origen, el usuario manual, el
/// payload ni los documentos (R-20). Con <c>estimado_consultas=AL_FINALIZAR</c> en la copia de configuración del lote y el lote
/// cerrado (COMPLETADO o INCOMPLETO), agrega el estimado de <see cref="EstimadorConsultas"/> (HU #13747) frente a lo consumido:
/// intentos con <c>desde_cache=false</c> que no son de llenado manual.
/// </summary>
internal sealed class ConsultaGrilla(DiagnosticoDb db, UsuarioActual usuario)
{
    public const string CodigoDesdeInvalido = "DESDE_INVALIDO";

    /// <summary>La grilla del lote desde la versión dada.</summary>
    public async Task<GrillaDto> ObtenerAsync(Guid id, long desde, CancellationToken ct)
    {
        if (desde < 0)
            throw new ReglaException(CodigoDesdeInvalido, "desde es la versión del tablero que ya tiene el cliente: 0 o mayor.", StatusCodes.Status400BadRequest) { Campo = "desde" };
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, id, seguir: false, ct).ConfigureAwait(false);

        var celdas = await db.Set<CeldaConsulta>().AsNoTracking()
            .Where(c => c.DiagnosticoId == id)
            .Select(c => new CeldaLeida(c.Id, c.VehiculoId, c.Fuente, c.Estado, c.MotivoNa, c.TipoError, c.DatoObtenidoEn, c.ReutilizadaDeId != null, c.VersionTablero))
            .ToListAsync(ct).ConfigureAwait(false);
        var porVehiculo = celdas.GroupBy(c => c.VehiculoId).ToDictionary(g => g.Key, g => g.ToList());
        var cambiadas = celdas.Where(c => c.Version > desde).ToList();
        var idsCambiados = cambiadas.Select(c => c.VehiculoId).Distinct().ToList();

        var vehiculos = db.Set<VehiculoDiagnostico>().AsNoTracking().Where(v => v.DiagnosticoId == id);
        var totalVehiculos = await vehiculos.CountAsync(ct).ConfigureAwait(false);
        var filas = await (desde == 0 ? vehiculos : vehiculos.Where(v => idsCambiados.Contains(v.Id)))
            .OrderBy(v => v.Orden)
            .Select(v => new { v.Id, v.Orden, v.IdentificadorNorm, v.TipoIdentificador, v.Placa, v.Vin })
            .ToListAsync(ct).ConfigureAwait(false);

        var ordenFuente = cabecera.Alcance.Distinct().Select((f, i) => (f, i)).ToDictionary(x => x.f, x => x.i);
        var cambiadasPorVehiculo = cambiadas.ToLookup(c => c.VehiculoId);
        var filasDto = filas.Select(v => new FilaVehiculoDto(
            v.Id, v.Orden, v.IdentificadorNorm, CodigosDeEnum.Codigo(v.TipoIdentificador), v.Placa, v.Vin,
            AvanceVehiculo.Terminado(porVehiculo.GetValueOrDefault(v.Id, []).Select(c => c.Estado)),
            [.. cambiadasPorVehiculo[v.Id].OrderBy(c => ordenFuente.GetValueOrDefault(c.Fuente, int.MaxValue)).Select(Dto)])).ToList();
        var terminados = porVehiculo.Values.Count(c => AvanceVehiculo.Terminado(c.Select(x => x.Estado)));

        return new GrillaDto(
            cabecera.Id, cabecera.Nombre, CodigosDeEnum.Codigo(cabecera.Estado), cabecera.VersionTablero, desde,
            AlcanceDto.De(Alcance.Agrupar(cabecera.Alcance)), new AvanceLoteDto(totalVehiculos, terminados), filasDto,
            await EstimadoAsync(cabecera, ct).ConfigureAwait(false));
    }

    private static CeldaGrillaDto Dto(CeldaLeida c) => new(
        c.Id, CodigosDeEnum.Codigo(c.Fuente), CodigosDeEnum.Codigo(c.Estado), c.MotivoNa is { } m ? CodigosDeEnum.Codigo(m) : null,
        c.TipoError, c.DatoObtenidoEn, c.Reutilizada, c.Version);

    /// <summary>Estimado frente a consumido solo con AL_FINALIZAR (copia de la cabecera) y el lote cerrado; null en todo otro caso.</summary>
    private async Task<IReadOnlyList<EstimadoVsConsumidoDto>?> EstimadoAsync(CabeceraDiagnostico cabecera, CancellationToken ct)
    {
        if (cabecera.Estado is not (EstadoDiagnostico.Completado or EstadoDiagnostico.Incompleto) || EstimadoDeLaCopia(cabecera.Configuracion) != EstimadoConsultas.AlFinalizar)
            return null;

        var vehiculos = await db.Set<VehiculoDiagnostico>().AsNoTracking()
            .Where(v => v.DiagnosticoId == cabecera.Id)
            .Select(v => v.LocatarioDocOrigen == OrigenDocumento.DeclaradoCliente || v.LocatarioDeclaradoDocCifrado != null)
            .ToListAsync(ct).ConfigureAwait(false);
        var estimado = EstimadorConsultas.Estimar(cabecera.Alcance, vehiculos.Select(conLocatario => new VehiculoAEstimar(true, conLocatario)));
        var consumidas = (await db.Set<IntentoConsulta>().AsNoTracking()
                .Where(i => i.DiagnosticoId == cabecera.Id && !i.DesdeCache && i.Disparador != DisparadorIntento.Manual)
                .GroupBy(i => i.Fuente)
                .Select(g => new { Fuente = g.Key, Consultas = g.Count() })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(x => x.Fuente, x => x.Consultas);
        return [.. estimado.Select(e => new EstimadoVsConsumidoDto(CodigosDeEnum.Codigo(e.Fuente), e.Consultas, consumidas.GetValueOrDefault(e.Fuente)))];
    }

    /// <summary><c>estimado_consultas</c> de la copia de configuración guardada en la cabecera; sin él, <c>NO_MOSTRAR</c>.</summary>
    private static EstimadoConsultas EstimadoDeLaCopia(string configuracion)
    {
        using var copia = JsonDocument.Parse(configuracion);
        return copia.RootElement.ValueKind == JsonValueKind.Object
            && copia.RootElement.TryGetProperty("estimado_consultas", out var valor)
            && valor.ValueKind == JsonValueKind.String
            && CodigosDeEnum.TryLeer<EstimadoConsultas>(valor.GetString(), out var estimado)
                ? estimado
                : EstimadoConsultas.NoMostrar;
    }

    private sealed record CeldaLeida(
        Guid Id, Guid VehiculoId, Fuente Fuente, EstadoCelda Estado, MotivoNa? MotivoNa, string? TipoError, DateTimeOffset? DatoObtenidoEn,
        bool Reutilizada, long Version);
}
