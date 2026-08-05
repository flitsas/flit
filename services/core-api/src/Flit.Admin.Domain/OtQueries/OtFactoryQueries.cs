namespace Flit.Admin.Domain.OtQueries;

/// <summary>
/// Consultas que vienen puestas.
///
/// <para>Existen para que la lista NUNCA esté vacía. Un constructor de consultas que se abre en
/// blanco es la forma más segura de que nadie lo use: la gente no sabe qué preguntar hasta que ve
/// una pregunta escrita, y a partir de ahí edita muy bien. Estas tres no pretenden ser las mejores
/// preguntas del organismo — pretenden ser el punto de partida para escribir las suyas.</para>
///
/// <para>No se persisten: se sirven junto a las guardadas y se distinguen por
/// <see cref="OtSavedQueryDto.DeFabrica"/>. Así no se pueden romper, no ocupan la cuota del usuario
/// y cambian cuando cambia el código. Guardar una es duplicarla.</para>
/// </summary>
public static class OtFactoryQueries
{
    // Ids fijos: el enlace compartible de una consulta de fábrica tiene que seguir abriendo la misma
    // consulta la semana que viene.
    private static readonly Guid ConPrendaSinLtId = new("f0000000-0000-4000-8000-000000000001");
    private static readonly Guid RechazadosDelMesId = new("f0000000-0000-4000-8000-000000000002");
    private static readonly Guid PrioritariosEnRevisionId = new("f0000000-0000-4000-8000-000000000003");

    private static readonly OtSavedQueryDto[] All =
    [
        new(
            ConPrendaSinLtId,
            "Con prenda y sin licencia de tránsito",
            "Trámites con prenda vigente a los que todavía no se les ha cargado la LT.",
            DeFabrica: true,
            new OtQueryDefinition(
                new OtQueryDateFilter(OtQueryDateField.Radicacion, OtQueryRangePreset.Ultimos90),
                [
                    new OtQueryCondition(OtQueryFieldCatalog.Prenda, OtQueryOperator.EsAlguno, ["true"]),
                    new OtQueryCondition(OtQueryFieldCatalog.LicenciaTransito, OtQueryOperator.EsAlguno, ["false"]),
                ],
                ["referencia", "placa", "empresa", "estado", "acreedor_prenda", "radicado_en"],
                OtQuerySort.Radicado,
                Descending: true),
            default,
            null),

        new(
            RechazadosDelMesId,
            "Rechazados este mes",
            "Lo devuelto en el mes en curso, contado por la fecha en que se rechazó.",
            DeFabrica: true,
            new OtQueryDefinition(
                new OtQueryDateFilter(OtQueryDateField.Decision, OtQueryRangePreset.MesActual),
                [
                    new OtQueryCondition(OtQueryFieldCatalog.Estado, OtQueryOperator.EsAlguno, ["rechazado"]),
                ],
                ["referencia", "placa", "empresa", "decidido_por", "decidido_en", "causales"],
                OtQuerySort.Decidido,
                Descending: true),
            default,
            null),

        new(
            PrioritariosEnRevisionId,
            "Prioritarios sin decidir",
            "Los marcados como prioritarios que siguen en la bandeja del organismo.",
            DeFabrica: true,
            new OtQueryDefinition(
                new OtQueryDateFilter(OtQueryDateField.Radicacion, OtQueryRangePreset.Ultimos90),
                [
                    new OtQueryCondition(OtQueryFieldCatalog.Prioritario, OtQueryOperator.EsAlguno, ["true"]),
                    new OtQueryCondition(
                        OtQueryFieldCatalog.Estado,
                        OtQueryOperator.EsAlguno,
                        ["en_revision", "esperando_placa", "en_subsanacion"]),
                ],
                ["referencia", "placa", "empresa", "estado", "radicado_en", "dias_en_organismo"],
                OtQuerySort.Radicado,
                Descending: false),
            default,
            null),
    ];

    public static IReadOnlyList<OtSavedQueryDto> Queries => All;

    public static bool IsFactory(Guid id) => All.Any(q => q.Id == id);

    public static OtSavedQueryDto? Find(Guid id) => All.FirstOrDefault(q => q.Id == id);
}
