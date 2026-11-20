namespace Flit.Diagnostico.Api.Modulos.Comun;

/// <summary>
/// Toda entidad de negocio de Diagnóstico pertenece a una empresa (HU #13722, ADR-0072). <c>DiagnosticoDb</c> le aplica por
/// convención el filtro global por la empresa del token y, al guardar, toma <see cref="TenantId"/> del token y rechaza
/// cualquier otro. <c>EntidadesConTenantTests</c> falla si una entidad mapeada que no es del SDK no la implementa.
/// </summary>
public interface IEntidadDeEmpresa
{
    /// <summary>Empresa dueña de la fila (columna <c>tenant_id</c>).</summary>
    Guid TenantId { get; set; }
}
