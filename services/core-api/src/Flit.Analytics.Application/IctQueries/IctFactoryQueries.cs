using Flit.Queries.Domain;

namespace Flit.Analytics.Application.IctQueries;

/// <summary>
/// Consultas que vienen puestas.
///
/// <para>Existen para que la lista NUNCA esté vacía. Un constructor de consultas que se abre en
/// blanco es la forma más segura de que nadie lo use: la gente no sabe qué preguntar hasta que ve
/// una pregunta escrita, y a partir de ahí edita muy bien.</para>
///
/// <para>No se persisten: se sirven junto a las guardadas y se distinguen por
/// <see cref="SavedQueryDto.DeFabrica"/>. Guardar una es duplicarla.</para>
/// </summary>
public static class IctFactoryQueries
{
    // Ids fijos: el enlace compartible de una consulta de fábrica tiene que seguir abriendo la misma
    // consulta la semana que viene. Prefijo propio (distinto de e0000000... de la empresa y
    // f0000000... del organismo) para que un id no pueda resolverse por accidente en el módulo
    // equivocado.
    private static readonly Guid ConNovedadesEstaSemanaId = new("1c700000-0000-4000-8000-000000000001");
    private static readonly Guid EsperandoValidacionId = new("1c700000-0000-4000-8000-000000000002");
    private static readonly Guid AunSinBorradorId = new("1c700000-0000-4000-8000-000000000003");

    private static readonly SavedQueryDto[] All =
    [
        new(
            ConNovedadesEstaSemanaId,
            "Con novedades esta semana",
            "Pre-trámites que la validación de negocio o externa marcó con algo que revisar.",
            DeFabrica: true,
            new QueryDefinition(
                new QueryDateFilter(IctQueryDateField.Registro, QueryRangePreset.Ultimos7),
                [
                    new QueryCondition(
                        IctQueryFieldCatalog.TieneNovedades, QueryOperator.EsAlguno, ["true"]),
                ],
                ["radicado", "placa", "tipo_tramite", "estado", "registrado_en"],
                IctQuerySort.Registrado,
                Descending: true),
            default,
            null),

        new(
            EsperandoValidacionId,
            "Esperando validación",
            "Todavía no pasan la validación de negocio ni la externa, lo más antiguo primero.",
            DeFabrica: true,
            new QueryDefinition(
                new QueryDateFilter(IctQueryDateField.Registro, QueryRangePreset.Ultimos30),
                [
                    new QueryCondition(
                        IctQueryFieldCatalog.Estado,
                        QueryOperator.EsAlguno,
                        ["en_validacion_negocio", "en_validacion_externa"]),
                ],
                ["radicado", "placa", "estado", "registrado_en"],
                IctQuerySort.Registrado,
                Descending: false),
            default,
            null),

        new(
            AunSinBorradorId,
            "Aún sin borrador",
            "Ya pasaron el registro pero todavía no generaron un trámite en FLIT.",
            DeFabrica: true,
            new QueryDefinition(
                new QueryDateFilter(IctQueryDateField.Registro, QueryRangePreset.Ultimos30),
                [
                    new QueryCondition(
                        IctQueryFieldCatalog.TieneBorrador, QueryOperator.EsAlguno, ["false"]),
                ],
                ["radicado", "placa", "estado", "registrado_en"],
                IctQuerySort.Registrado,
                Descending: true),
            default,
            null),
    ];

    public static IReadOnlyList<SavedQueryDto> Queries => All;

    public static bool IsFactory(Guid id) => All.Any(q => q.Id == id);
}
