using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Definiciones;

namespace Flit.PersonaNatural.Api.Solicitudes;

/// <summary>Gravamen del vehículo del borrador según el RUNT (la consulta la cachea core-consultas). Lo implementa <see cref="VehiculoDelBorradorService"/>.</summary>
public interface IGravamenDelBorrador
{
    /// <exception cref="ProveedorNoDisponibleException">El RUNT no respondió: no se decide en falso.</exception>
    Task<ResultadoVehiculoBorrador> GravamenAsync(Guid solicitudId, CancellationToken ct);
}

/// <summary>
/// Si el RUNT reporta prenda o gravamen en el vehículo, un tipo con prenda complementaria (traspaso) no avanza sin una decisión
/// explícita (<c>registrar</c>, <c>levantar</c> o <c>sin_prenda</c>) en <c>datos.prenda.decision</c>. Los tipos que ya exigen la
/// prenda, o que la fijan por sí mismos (acción única), no pasan por aquí. Sin gravamen, o sin poder consultarlo, no se exige.
/// </summary>
public static class DecisionDePrendaObligatoria
{
    public const string Codigo = "prenda_decision_requerida";
    public const string Referencia = "prenda.decision";
    public const string Mensaje = "El RUNT reporta prenda o gravamen: elige qué se hará con ella para continuar.";

    public static bool Aplica(DefinicionTipoDto def) =>
        def.Flags.PermitePrendaComplementaria && !def.Flags.RequierePrenda && string.IsNullOrEmpty(def.Flags.AccionPrenda);

    /// <summary>
    /// <c>true</c> si el tipo la admite, el borrador no trae una decisión válida y el RUNT reporta gravamen.
    /// Con <paramref name="gravamen"/> nulo o sin vehículo que consultar devuelve <c>false</c>.
    /// </summary>
    /// <exception cref="ProveedorNoDisponibleException">El RUNT no respondió.</exception>
    public static async Task<bool> FaltaAsync(DefinicionTipoDto def, DatosSolicitud datos, Guid solicitudId, IGravamenDelBorrador? gravamen, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(datos);
        if (gravamen is null || !Aplica(def) || TieneDecision(datos))
            return false;

        var r = await gravamen.GravamenAsync(solicitudId, ct).ConfigureAwait(false);
        return r.Estado == EstadoVehiculoBorrador.Ok && r.Gravamen?.TienePrenda == true;
    }

    private static bool TieneDecision(DatosSolicitud datos) =>
        datos.Prenda?.Decision?.Trim().ToLowerInvariant() is ReglasBorrador.PrendaRegistrar or ReglasBorrador.PrendaLevantar or ReglasBorrador.PrendaSinPrenda;
}
