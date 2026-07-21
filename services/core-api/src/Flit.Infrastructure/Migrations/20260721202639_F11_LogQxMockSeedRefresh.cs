using System;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Feature #10792 (LOG QX) — REFRESH del mock. <c>F11_LogQxMockSeed</c> ya está aplicada en los
    /// ambientes (DEV/QA), y EF NO re-ejecuta una migración registrada en <c>__EFMigrationsHistory</c>
    /// aunque cambie el SQL embebido que carga. Este refresh vuelve a emitir el MISMO
    /// <c>35-F11-log-qx-mock-seed.sql</c> (ya editado) para propagar a esos ambientes los eventos
    /// enriquecidos: <c>registro_enviado</c> con placa/vin/documento/tipo_tramite/tipo_requisito/
    /// codigo_divipo (antes solo booleanos, no se identificaba el trámite) y el sondeo con
    /// <c>estado_tramite = 1</c> ("sin cambios"). El SQL es delete-then-insert idempotente, así que
    /// re-ejecutarlo REFRESCA sin duplicar. Mismo gate DEV/QA/FLIT_DEV_SEED que la migración base.
    /// No modifica el modelo: el snapshot no cambia.
    /// </remarks>
    public partial class F11_LogQxMockSeedRefresh : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dato de prueba, no de negocio: no se siembra en producción real.
            if (!IsDevSeedEnabled())
            {
                return;
            }

            migrationBuilder.Sql(EmbeddedDdl.LoadUp("35-F11-log-qx-mock-seed.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revertir un REFRESH de datos de prueba no es significativo: la limpieza del mock la hace
            // el Down de la migración base F11_LogQxMockSeed. Aquí es no-op a propósito.
        }

        // Gate por entorno: siembra solo en Development/QA o con FLIT_DEV_SEED activo (mismo criterio
        // que F11_LogQxMockSeed / FEATURE10707_AvaluoMockSeed).
        private static bool IsDevSeedEnabled()
        {
            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(env, "QA", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var flag = Environment.GetEnvironmentVariable("FLIT_DEV_SEED");
            return string.Equals(flag, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
