namespace Flit.Diagnostico.Domain.Configuracion;

/// <summary>Qué lotes ve un usuario de la compañía (D-01, R-21). En la base: <c>PROPIOS</c> o <c>EMPRESA</c>.</summary>
public enum VisibilidadUsuario
{
    /// <summary>Solo los lotes que creó.</summary>
    Propios,

    /// <summary>Todos los lotes de su compañía (valor por defecto).</summary>
    Empresa,
}
