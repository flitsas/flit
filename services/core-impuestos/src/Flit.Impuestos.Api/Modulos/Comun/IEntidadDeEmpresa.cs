namespace Flit.Impuestos.Api.Modulos.Comun;

/// <summary>
/// Toda entidad de negocio de Impuestos con <c>tenant_id</c> pertenece a una empresa (plan §4.1, forma de ADR-0072).
/// <c>ImpuestosDb</c> le aplica por convención el filtro global por la empresa del token y, al guardar, toma <see cref="TenantId"/>
/// del token y rechaza cualquier otro. <c>EntidadesConTenantTests</c> falla si una entidad mapeada que no es del SDK ni de la
/// lista de tablas globales no la implementa.
/// </summary>
public interface IEntidadDeEmpresa
{
    /// <summary>Empresa dueña de la fila (columna <c>tenant_id</c>).</summary>
    Guid TenantId { get; set; }
}
