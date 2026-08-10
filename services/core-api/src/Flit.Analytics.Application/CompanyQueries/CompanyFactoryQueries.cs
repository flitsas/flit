using Flit.Queries.Domain;

namespace Flit.Analytics.Application.CompanyQueries;

/// <summary>
/// Consultas que vienen puestas.
///
/// <para>Existen para que la lista NUNCA esté vacía. Un constructor de consultas que se abre en
/// blanco es la forma más segura de que nadie lo use: la gente no sabe qué preguntar hasta que ve
/// una pregunta escrita, y a partir de ahí edita muy bien. Estas tres no pretenden ser las mejores
/// preguntas de una gestora — pretenden ser el punto de partida para escribir las suyas.</para>
///
/// <para>No se persisten: se sirven junto a las guardadas y se distinguen por
/// <see cref="SavedQueryDto.DeFabrica"/>. Así no se pueden romper, no ocupan la cuota del usuario y
/// cambian cuando cambia el código. Guardar una es duplicarla.</para>
/// </summary>
public static class CompanyFactoryQueries
{
    // Ids fijos: el enlace compartible de una consulta de fábrica tiene que seguir abriendo la misma
    // consulta la semana que viene. Distinto prefijo que las del organismo para que un id no pueda
    // resolverse por accidente en el módulo equivocado.
    private static readonly Guid PendientesDeEntregarId = new("e0000000-0000-4000-8000-000000000001");
    private static readonly Guid DevueltosId = new("e0000000-0000-4000-8000-000000000002");
    private static readonly Guid EntregadosDelMesId = new("e0000000-0000-4000-8000-000000000003");

    private static readonly SavedQueryDto[] All =
    [
        new(
            PendientesDeEntregarId,
            "Pendientes de entregar",
            "Lo que sigue en la casa y todavía no ha salido al organismo, lo más viejo primero.",
            DeFabrica: true,
            new QueryDefinition(
                new QueryDateFilter(CompanyQueryDateField.Creacion, QueryRangePreset.Ultimos90),
                [
                    new QueryCondition(
                        CompanyQueryFieldCatalog.Estado,
                        QueryOperator.EsAlguno,
                        ["borrador", "preparado"]),
                ],
                ["referencia", "placa", "tipo", "estado", "radicado_por", "creado_en"],
                CompanyQuerySort.Creado,
                Descending: false),
            default,
            null),

        new(
            DevueltosId,
            "Devueltos por el organismo",
            "Trámites que el organismo regresó y siguen pendientes de corregir.",
            DeFabrica: true,
            new QueryDefinition(
                new QueryDateFilter(CompanyQueryDateField.Actualizacion, QueryRangePreset.Ultimos90),
                [
                    new QueryCondition(
                        CompanyQueryFieldCatalog.EnSubsanacion, QueryOperator.EsAlguno, ["true"]),
                ],
                ["referencia", "placa", "organismo", "estado", "devoluciones", "actualizado_en"],
                CompanyQuerySort.Actualizado,
                Descending: true),
            default,
            null),

        new(
            EntregadosDelMesId,
            "Entregados este mes",
            "Lo que salió al organismo en el mes en curso, contado por la fecha de envío.",
            DeFabrica: true,
            new QueryDefinition(
                new QueryDateFilter(CompanyQueryDateField.Envio, QueryRangePreset.MesActual),
                [],
                ["referencia", "placa", "organismo", "tipo", "estado", "enviado_en"],
                CompanyQuerySort.Enviado,
                Descending: true),
            default,
            null),
    ];

    public static IReadOnlyList<SavedQueryDto> Queries => All;

    public static bool IsFactory(Guid id) => All.Any(q => q.Id == id);
}
