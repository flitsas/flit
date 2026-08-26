using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Paridad de pasos wizard traspaso/matrícula (2026-08) — el wizard de TRASPASO deja de tener un
    /// paso propio "comercial" (5): sus datos (valor de venta) se absorben en el paso "documentos" (4)
    /// y el hueco lo ocupa "identidad" (biométrica de ambas partes), quedando la MISMA secuencia de
    /// 6 pasos que matrícula: consulta → vendedor → comprador → documentos → identidad → fur.
    ///
    /// <para>Los borradores de traspaso EN CURSO cuyo <c>current_step</c> persistido (HU #10879,
    /// autosave del avance) apuntaba a la clave <c>comercial</c> quedarían apuntando a un paso que ya
    /// no existe. Migran a <c>documentos</c>, que es donde ahora viven los datos que dejaron a medias
    /// (así el gestor retoma exactamente donde lo dejó, con el valor de venta a la vista).</para>
    ///
    /// <para>La tabla <c>tramites.procedure_instances</c> está <c>ExcludeFromMigrations</c> (DDL
    /// gestionado por SQL crudo, HU #10150), por eso el diff EF queda vacío y no hay
    /// <c>*.Designer.cs</c>: se declaran <c>[DbContext]</c>/<c>[Migration]</c> inline (mismo patrón que
    /// <c>HU10879_CurrentStep</c> / <c>HU10870_SubsanacionEditableTrigger</c>) para que la migración se
    /// descubra por reflexión y corra al arrancar sin depender del ModelSnapshot.</para>
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260813120000_TraspasoIdentidadStepMigration")]
    public partial class TraspasoIdentidadStepMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE tramites.procedure_instances
                   SET current_step = 'documentos'
                 WHERE current_step = 'comercial'
                   AND modalidad_entrada = 'traspaso';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible con precisión: no hay forma de distinguir, entre los que hoy están en
            // 'documentos', cuáles venían de 'comercial' (migrados aquí) de cuáles ya estaban en
            // 'documentos' de forma legítima (nunca llegaron a completar comercial). No-op deliberado:
            // revertir a ciegas movería borradores legítimos a un paso ('comercial') que el wizard ya
            // no reconoce, dejándolos peor que si no se revierte nada.
        }
    }
}
