using System.Text;
using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Domain.Diagnosticos;

/// <summary>Lo que el usuario pide guardar en la cabecera (crear o editar).</summary>
public sealed record SolicitudCabecera(string? Nombre, ModoDiagnostico Modo, string? TipoTramiteCodigo, Alcance Alcance);

/// <summary>Regla incumplida: código estable, campo del cuerpo (snake_case) y mensaje para el usuario.</summary>
public sealed record ErrorCabecera(string Codigo, string Campo, string Mensaje);

/// <summary>
/// Reglas de la cabecera del diagnóstico (HU #13750, F01-H4, F01-C1..C7), sin base ni HTTP; las reutiliza el Express
/// (#13718). En orden: nombre de 1 a <see cref="LargoMaximoNombre"/> caracteres (sin contar los espacios de los extremos);
/// cada fuente en su grupo (<see cref="GruposFuente"/>); RUNT siempre; solo fuentes habilitadas en la configuración de la
/// compañía y con adaptador disponible (<c>IMPUESTO_DEPARTAMENTAL</c> sin C3, decisión de la ronda); Con Análisis IA con
/// tipo de trámite. Solo si todo eso se cumple y el modo es Con Análisis IA consulta el catálogo de tipos (F01-H2): el tipo
/// debe estar en él (lista blanca de traspasos) y el alcance debe traer sus fuentes obligatorias (R-17; vacías en D1). Si el
/// catálogo no está disponible deja subir <see cref="CatalogoTiposTramiteNoDisponibleException"/> (la API responde 503).
/// </summary>
public sealed class ValidadorCabecera(ICatalogoTiposTramite catalogo, Func<Fuente, bool> disponible)
{
    public const int LargoMaximoNombre = 150;

    public const string CampoNombre = "nombre";
    public const string CampoAlcance = "alcance";
    public const string CampoTipoTramite = "tipo_tramite_codigo";

    public const string CodigoNombreInvalido = "NOMBRE_INVALIDO";
    public const string CodigoAlcanceSinRunt = "ALCANCE_SIN_RUNT";
    public const string CodigoFuenteEnGrupoAjeno = "FUENTE_EN_GRUPO_AJENO";
    public const string CodigoFuenteNoHabilitada = "FUENTE_NO_HABILITADA";
    public const string CodigoFuenteNoDisponible = "FUENTE_NO_DISPONIBLE";
    public const string CodigoTipoTramiteRequerido = "TIPO_TRAMITE_REQUERIDO";
    public const string CodigoTipoTramiteNoDisponible = "TIPO_TRAMITE_NO_DISPONIBLE";
    public const string CodigoFuentesObligatoriasFaltantes = "FUENTES_OBLIGATORIAS_FALTANTES";

    /// <summary>Campo del cuerpo de cada grupo del alcance.</summary>
    public static string CampoGrupo(GrupoFuente grupo) => grupo switch
    {
        GrupoFuente.PorVehiculo => "alcance.por_vehiculo",
        GrupoFuente.PorPropietario => "alcance.por_propietario",
        _ => "alcance.por_locatario",
    };

    /// <summary>Errores de la solicitud (vacío si es válida), contra las fuentes habilitadas de la configuración de la compañía.</summary>
    public async Task<IReadOnlyList<ErrorCabecera>> ValidarAsync(SolicitudCabecera solicitud, IReadOnlyCollection<Fuente> habilitadas, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        ArgumentNullException.ThrowIfNull(habilitadas);
        var errores = new List<ErrorCabecera>();
        var nombre = solicitud.Nombre?.Trim() ?? string.Empty;
        if (nombre.Length is 0 or > LargoMaximoNombre)
            errores.Add(new(CodigoNombreInvalido, CampoNombre, $"El nombre es obligatorio y tiene como máximo {LargoMaximoNombre} caracteres."));

        errores.AddRange(GruposAjenos(solicitud.Alcance));
        var fuentes = solicitud.Alcance.Fuentes;
        if (!fuentes.Contains(Fuente.Runt))
            errores.Add(new(CodigoAlcanceSinRunt, CampoAlcance, "El alcance siempre incluye RUNT."));

        var noHabilitadas = fuentes.Where(f => !habilitadas.Contains(f)).ToList();
        if (noHabilitadas.Count > 0)
            errores.Add(new(CodigoFuenteNoHabilitada, CampoAlcance, $"Tu compañía no tiene habilitada la fuente {Lista(noHabilitadas)}."));

        var noDisponibles = fuentes.Where(f => habilitadas.Contains(f) && !disponible(f)).ToList();
        if (noDisponibles.Count > 0)
            errores.Add(new(CodigoFuenteNoDisponible, CampoAlcance, $"La fuente {Lista(noDisponibles)} todavía no se puede consultar."));

        var conAnalisis = solicitud.Modo == ModoDiagnostico.ConAnalisis;
        var tipo = solicitud.TipoTramiteCodigo?.Trim();
        if (conAnalisis && string.IsNullOrEmpty(tipo))
            errores.Add(new(CodigoTipoTramiteRequerido, CampoTipoTramite, "Con Análisis IA exige el tipo de trámite."));

        if (errores.Count > 0 || !conAnalisis)
            return errores;

        var tipos = await catalogo.ListarAsync(ct).ConfigureAwait(false);
        var elegido = tipos.FirstOrDefault(t => string.Equals(t.Codigo, tipo, StringComparison.Ordinal));
        if (elegido is null)
            return [new(CodigoTipoTramiteNoDisponible, CampoTipoTramite, "El tipo de trámite no está entre los que ofrece Diagnóstico.")];

        var faltantes = elegido.FuentesObligatorias.Where(f => !fuentes.Contains(f)).ToList();
        return faltantes.Count > 0
            ? [new(CodigoFuentesObligatoriasFaltantes, CampoAlcance, $"El tipo de trámite {elegido.Nombre} exige la fuente {Lista(faltantes)}.")]
            : [];
    }

    private static IEnumerable<ErrorCabecera> GruposAjenos(Alcance alcance)
    {
        foreach (var (grupo, fuentes) in new[]
                 {
                     (GrupoFuente.PorVehiculo, alcance.PorVehiculo), (GrupoFuente.PorPropietario, alcance.PorPropietario),
                     (GrupoFuente.PorLocatario, alcance.PorLocatario),
                 })
        {
            var ajenas = fuentes.Where(f => GruposFuente.De(f) != grupo).Distinct().ToList();
            if (ajenas.Count > 0)
                yield return new(CodigoFuenteEnGrupoAjeno, CampoGrupo(grupo), $"La fuente {Lista(ajenas)} no pertenece a este grupo del alcance.");
        }
    }

    /// <summary>Códigos de la base (<c>SIMIT_VEHICULO</c>), los mismos que recibe y devuelve la API.</summary>
    private static string Lista(IEnumerable<Fuente> fuentes) => string.Join(", ", fuentes.Select(Codigo));

    private static string Codigo(Fuente fuente)
    {
        var nombre = fuente.ToString();
        var codigo = new StringBuilder(nombre.Length + 4);
        for (var i = 0; i < nombre.Length; i++)
        {
            if (i > 0 && char.IsUpper(nombre[i]))
                codigo.Append('_');
            codigo.Append(char.ToUpperInvariant(nombre[i]));
        }

        return codigo.ToString();
    }
}
