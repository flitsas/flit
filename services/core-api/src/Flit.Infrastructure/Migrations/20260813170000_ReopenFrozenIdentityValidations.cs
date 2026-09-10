using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Bug #11503 — remediación de datos: reabre a <c>en_proceso</c> las validaciones de identidad que el
/// cliente Kyverum congeló en <c>rechazado</c> terminal tras el PRIMER intento fallido, cuando el
/// proveedor todavía permitía reintentos.
/// </summary>
/// <remarks>
/// El arreglo de código (<c>KyverumVerifyClient</c>, misma rama) evita filas NUEVAS dañadas, pero no
/// libera las ya congeladas: <c>IdentityValidationResultApplier</c> deja inmutable toda fila terminal,
/// así que aunque el ciudadano apruebe después nunca se actualiza — y en trámites eso bloquea la
/// radicación (<c>borrador → preparado</c>), que exige una validación aprobada vigente.
///
/// <para>La migración NO emite un veredicto: solo devuelve la fila al estado donde el sistema puede
/// volver a decidir. El worker de reconciliación consultará a Kyverum y, con el cliente corregido,
/// aplicará el resultado real. Reinicia <c>reconcile_poll_count</c> porque el reclamo del worker
/// (<c>IdentityValidationReconcileProcessor</c>, líneas 207-219) acota por
/// <c>reconcile_poll_count &lt; 3</c>: sin ese reinicio las filas reabiertas quedarían en
/// <c>en_proceso</c> sin que nadie volviera a mirarlas.</para>
///
/// <para>El SQL embebido documenta los cinco filtros de seguridad. Los dos que nacieron de la
/// evidencia del esquema: (a) se excluyen las filas ya vencidas, porque el paso de vencidas del worker
/// las terminaliza en <c>expirado</c> SIN consultar al proveedor —reabrirlas no recupera ninguna
/// aprobación y sí ocupa el cupo de "en vuelo"—; y (b) se excluyen las que colisionarían con el índice
/// único parcial <c>uq_biometric_validations_inflight_doc_norm</c> (HU #11266), el escenario esperado
/// del bug: al ciudadano lo rechazaron mal y volvió a empezar. Sin (b) el UPDATE lanzaría 23505 y
/// tumbaría el despliegue entero.</para>
///
/// <para><b>Down no-op deliberado.</b> Una remediación de datos no tiene reverso semántico honesto. El
/// único "deshacer" literal sería volver a poner en <c>rechazado</c> lo que se reabrió, y eso sería
/// reintroducir el defecto a mano: en el momento del rollback esas filas ya no son las mismas —el
/// worker pudo haberlas aprobado con un veredicto REAL del proveedor—, así que congelarlas destruiría
/// información legítima y volvería a bloquear radicaciones. Tampoco se puede distinguir después cuáles
/// tocó esta migración: la firma del defecto desaparece justo al aplicarla. Dejar el <c>Down</c> vacío
/// es la opción correcta y no rompe la reversibilidad del esquema, porque esta migración no altera
/// ninguna estructura: no hay DDL que revertir. La traza de qué filas cambiaron queda en
/// <c>public.audit_log</c> vía el trigger de auditoría de la tabla.</para>
/// </remarks>
[DbContext(typeof(FlitDbContext))]
[Migration("20260813170000_ReopenFrozenIdentityValidations")]
public partial class ReopenFrozenIdentityValidations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("73-HU11503-reabrir-validaciones-congeladas.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intencionalmente vacío — ver <remarks>: no hay DDL que revertir y "volver a congelar" filas
        // sería reintroducir el Bug #11503 sobre datos que ya pudieron resolverse legítimamente.
    }
}
