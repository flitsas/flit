using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Diagnosticos;

/// <summary>
/// Crea y edita la cabecera del diagnóstico de la compañía del token (HU #13750, F01-H4). Crear guarda la cabecera en
/// CREADO con la copia inmutable de la configuración vigente (<see cref="CopiaConfiguracion"/>) y la fila inicial del
/// historial (anterior nulo, disparador AUTO) en la misma transacción. Editar cambia nombre, modo, tipo y alcance solo en
/// CREADO (si no, 409 <see cref="CodigoNoEditable"/>), validando contra la copia guardada y no contra la configuración de
/// hoy: el lote se arma sobre una configuración fija. Las reglas son las de <see cref="ValidadorCabecera"/> (422 con el
/// campo); el catálogo caído responde 503 como F01-H2.
/// </summary>
public sealed class ServicioCabecera(
    DiagnosticoDb db, UsuarioActual usuario, ICatalogoTiposTramite catalogo, DisponibilidadFuentes disponibilidad, TimeProvider reloj)
{
    public const string CodigoNoEditable = "DIAGNOSTICO_NO_EDITABLE";
    public const string CampoModo = "modo";

    private readonly ValidadorCabecera _validador = new(catalogo, disponibilidad.Disponible);

    /// <summary>
    /// Crea la cabecera en CREADO. Con <paramref name="esExpress"/> la marca como captura Express (HU #13753): el Express la
    /// crea dentro de su propia transacción, junto con su único vehículo.
    /// </summary>
    public async Task<CabeceraDiagnostico> CrearAsync(CabeceraEntradaDto entrada, CancellationToken ct, bool esExpress = false)
    {
        usuario.EmpresaPropia();
        var autor = Autor();
        var solicitud = Leer(entrada);
        var copia = await CopiaConfiguracion.TomarAsync(db, ct).ConfigureAwait(false);
        await ValidarAsync(solicitud, copia.FuentesHabilitadas, ct).ConfigureAwait(false);

        var ahora = reloj.GetUtcNow();
        var cabecera = new CabeceraDiagnostico
        {
            Nombre = solicitud.Nombre!.Trim(),
            Modo = solicitud.Modo,
            TipoTramiteCodigo = Tipo(solicitud),
            EsExpress = esExpress,
            Alcance = [.. solicitud.Alcance.Fuentes],
            Configuracion = copia.ComoJson(),
            Estado = EstadoDiagnostico.Creado,
            UsuarioCreadorId = autor,
            CreadoEn = ahora,
        };
        db.Add(cabecera);
        db.Add(new HistorialEstadoDiagnostico
        {
            DiagnosticoId = cabecera.Id,
            EstadoAnterior = null,
            EstadoNuevo = EstadoDiagnostico.Creado,
            Disparador = DisparadorEstado.Auto,
            UsuarioId = autor,
            En = ahora,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return cabecera;
    }

    /// <summary>
    /// Edita la cabecera en CREADO. Modo, tipo y alcance son inmutables para el seguimiento de cambios de EF (HU #13738): la
    /// edición los escribe con un UPDATE condicionado a <c>estado = CREADO</c> y a la compañía del token, así que una cabecera
    /// que pasó a EN_PROCESO entre la lectura y la escritura tampoco cambia (409).
    /// </summary>
    public async Task<CabeceraDiagnostico> EditarAsync(Guid id, CabeceraEntradaDto entrada, CancellationToken ct)
    {
        var empresa = usuario.EmpresaPropia();
        var solicitud = Leer(entrada);
        var actual = await Buscar(id).AsNoTracking().SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("El diagnóstico");
        if (actual.Estado != EstadoDiagnostico.Creado)
            throw NoEditable();

        await ValidarAsync(solicitud, HabilitadasDeLaCopia(actual.Configuracion), ct).ConfigureAwait(false);
        var nombre = solicitud.Nombre!.Trim();
        var tipo = Tipo(solicitud);
        var alcance = solicitud.Alcance.Fuentes.ToList();
        var filas = await Buscar(id)
            .Where(c => c.TenantId == empresa && c.Estado == EstadoDiagnostico.Creado)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Nombre, nombre)
                .SetProperty(c => c.Modo, solicitud.Modo)
                .SetProperty(c => c.TipoTramiteCodigo, tipo)
                .SetProperty(c => c.Alcance, alcance), ct)
            .ConfigureAwait(false);
        if (filas == 0)
            throw NoEditable();

        return await Buscar(id).AsNoTracking().SingleAsync(ct).ConfigureAwait(false);
    }

    private IQueryable<CabeceraDiagnostico> Buscar(Guid id) => db.Set<CabeceraDiagnostico>().Where(c => c.Id == id);

    private async Task ValidarAsync(SolicitudCabecera solicitud, IReadOnlyCollection<Fuente> habilitadas, CancellationToken ct)
    {
        IReadOnlyList<ErrorCabecera> errores;
        try
        {
            errores = await _validador.ValidarAsync(solicitud, habilitadas, ct).ConfigureAwait(false);
        }
        catch (CatalogoTiposTramiteNoDisponibleException ex)
        {
            throw ReglaException.CatalogoNoDisponible(ex);
        }

        if (errores.Count > 0)
            throw new ReglaException(errores[0].Codigo, errores[0].Mensaje) { Campo = errores[0].Campo };
    }

    private Guid Autor() =>
        usuario.Id != Guid.Empty ? usuario.Id : throw ReglaException.Prohibido("SIN_USUARIO", "El token no identifica al usuario: la cabecera no se puede registrar.");

    /// <summary>Modo Simple no guarda tipo de trámite (ck_tipo_con_analisis solo lo exige en Con Análisis IA).</summary>
    private static string? Tipo(SolicitudCabecera solicitud) =>
        solicitud.Modo == ModoDiagnostico.ConAnalisis ? solicitud.TipoTramiteCodigo?.Trim() : null;

    private static ReglaException NoEditable() =>
        ReglaException.Conflicto(CodigoNoEditable, "El diagnóstico ya no está en CREADO: su cabecera no se puede editar.");

    /// <summary>Convierte los códigos del cuerpo; un código desconocido es 422 <c>VALOR_FUERA_DE_CONJUNTO</c> con su campo.</summary>
    private static SolicitudCabecera Leer(CabeceraEntradaDto entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (!CodigosDeEnum.TryLeer<ModoDiagnostico>(entrada.Modo, out var modo))
            throw FueraDeConjunto(CampoModo);

        return new SolicitudCabecera(entrada.Nombre, modo, entrada.TipoTramiteCodigo, LeerAlcance(entrada.Alcance));
    }

    /// <summary>Alcance del cuerpo en sus tres grupos (también lo lee el estimado del Express, HU #13753); un código desconocido es 422.</summary>
    internal static Alcance LeerAlcance(AlcanceDto? alcance) => new(
        Fuentes(alcance?.PorVehiculo, GrupoFuente.PorVehiculo),
        Fuentes(alcance?.PorPropietario, GrupoFuente.PorPropietario),
        Fuentes(alcance?.PorLocatario, GrupoFuente.PorLocatario));

    private static Fuente[] Fuentes(IReadOnlyList<string>? codigos, GrupoFuente grupo) =>
        [.. (codigos ?? []).Select(c => CodigosDeEnum.TryLeer<Fuente>(c, out var f) ? f : throw FueraDeConjunto(ValidadorCabecera.CampoGrupo(grupo))).Distinct()];

    private static ReglaException FueraDeConjunto(string campo) =>
        new(ServicioConfiguracion.CodigoFueraDeConjunto, $"El valor de {campo} no es válido.") { Campo = campo };

    /// <summary>Fuentes habilitadas de la copia guardada en la cabecera (<c>fuentes_habilitadas</c> de <see cref="CopiaConfiguracion"/>).</summary>
    private static Fuente[] HabilitadasDeLaCopia(string configuracion)
    {
        using var json = JsonDocument.Parse(configuracion);
        return json.RootElement.TryGetProperty(ServicioConfiguracion.CampoFuentesHabilitadas, out var habilitadas) && habilitadas.ValueKind == JsonValueKind.Array
            ? [.. habilitadas.EnumerateArray().Select(e => CodigosDeEnum.TryLeer<Fuente>(e.GetString(), out var f) ? (Fuente?)f : null).OfType<Fuente>()]
            : [Fuente.Runt];
    }
}
