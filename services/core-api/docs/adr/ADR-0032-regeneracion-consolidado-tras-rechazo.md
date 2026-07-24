# ADR-0032 — Regeneración en cascada del expediente derivado (FUR + consolidado) tras rechazo o cambios en borrador

- **Estado**: Aceptado · 2026-07-23
- **Módulo**: Trámites — Ciclo de vida / Generación documental
- **Feature**: #10852 (punto 7)
- **Deciders**: Líder Técnico FLIT
- **Tags**: arquitectura, backend, database, documental, ciclo-vida

## Contexto

El punto 7 exige que la "hoja de vida" del trámite esté **siempre actualizada**: si un registro se rechaza y vuelve a `borrador` y el usuario ajusta datos, el expediente derivado (FUR, certificados generados en caliente y consolidado) debe regenerarse con la información **y las fechas** vigentes. Hoy el **consolidado_maestro** se invalida con el flag `consolidado_maestro_vigente` (HU #10701) en cada transición de estado (`TramiteLifecycleService`), pero el **expediente del wizard** (FUR, `certificado_identidad*`, `compraventa` system, `consolidado`) **no tiene invalidación**: puede quedar **stale**.

Decisión de negocio (Líder Técnico): usar **caché explícita con columna nueva**, coherente con el precedente del maestro, y que la invalidación **regenere en cascada** el FUR y los documentos generados en caliente (para que salgan con fecha actualizada cuando aplique), no solo re-mezclar PDFs existentes.

## Decisión

Agregar la columna `tramites.procedure_instances.consolidado_wizard_vigente boolean NOT NULL DEFAULT false` (espejo de `consolidado_maestro_vigente`), con **semántica de expediente derivado** (Opción β): cuando está en `false`, la siguiente generación del consolidado **regenera primero el FUR y sus documentos en caliente** (con fecha vigente) y luego consolida, y sube el flag a `true`. Se baja a `false` en los mismos puntos que el maestro (cada transición de estado, decisión OT, adjuntar Licencia de Tránsito). **Requiere migración (Fase 2b).**

## Alternativas consideradas

### Opción 1: Invalidar por borrado de derivados en la transición
**Pros:** sin migración; falla segura; menor superficie.
**Cons:** asimétrica con el maestro; en `borrador` no queda consolidado hasta regenerar; menos observable; no garantiza por sí sola la regeneración del FUR (depende de que el usuario la dispare).
**Esfuerzo:** S · **Riesgos:** bajo.

### Opción 2: Flag `consolidado_wizard_vigente` con regeneración en cascada del FUR (β) (RECOMENDADA / ELEGIDA)
**Pros:**
- Caché explícita: no regenera si nada cambió (evita merge de PDFs innecesario).
- Simétrica con el maestro (`consolidado_maestro_vigente`), mismo modelo mental y mismos 4 puntos de invalidación (precedente #10701).
- El PDF previo se conserva en `borrador` (marcado stale) mientras el usuario edita.
- La cascada garantiza que FUR y documentos en caliente salgan con **fecha actualizada** al regenerar.
- Auditable: estado `vigente` consultable + `row_version`/`audit_log`.

**Cons:**
- Requiere migración → Fase 2b obligatoria + gate `db-schema-validator`.
- Falla insegura: un flujo futuro que edite datos en borrador sin bajar el flag serviría stale como vigente.
- Acopla la regeneración del FUR al request del consolidado (cascada) y añade un flag paralelo al del maestro.
- Cada toggle es un `UPDATE` → bump de `row_version` + fila de `audit_log`.

**Esfuerzo:** M · **Riesgos:** medio (tocar lifecycle + cascada; cubrir con tests).

### Opción 3: Regenerar siempre al vuelo (no persistir el consolidado)
**Pros:** nunca hay stale.
**Cons:** costo de CPU por descarga; cambia el modelo de persistencia; rompe descargas directas del adjunto.
**Esfuerzo:** M · **Riesgos:** alto.

## Tradeoff aceptado

Opción 2 (β). Se acepta el costo de una migración y el acoplamiento consolidado→FUR a cambio de: (a) consistencia con el precedente del maestro que pidió el Líder Técnico, (b) caché explícita y auditable, y (c) la garantía de que **todo documento generado en caliente se regenera con fecha vigente** en una sola acción del usuario. Se descarta la Opción 1 (asimétrica y no garantiza la cascada del FUR) y la Opción 3 (performance / cambio de modelo).

**Nota de nomenclatura:** se conserva el nombre `consolidado_wizard_vigente` para mantener la simetría con `consolidado_maestro_vigente`; su semántica (documentada en el `COMMENT` de la columna) cubre **todo el expediente derivado del wizard**, no solo el PDF consolidado.

## Consecuencias

### Lo que se gana
- Expediente del wizard (FUR + certificados en caliente + consolidado) siempre fresco tras rechazo/edición, con fechas vigentes.
- Caché explícita y auditable; simetría con el maestro.

### Lo que se pierde
- Superficie de schema (columna nueva en tabla caliente) + Fase 2b.
- Acoplamiento consolidado→FUR (la generación del consolidado dispara la del FUR cuando el flag está en `false`).
- Dos flags de vigencia a mantener consistentes (maestro + wizard).

### Cambios operacionales
- Migración idempotente por SQL crudo (la tabla es `ExcludeFromMigrations`, patrón de `20260715022424_HU10701_ConsolidadoMaestroVigente`): `ALTER TABLE ... ADD COLUMN IF NOT EXISTS consolidado_wizard_vigente boolean NOT NULL DEFAULT false` + `COMMENT`; `Down` con `DROP COLUMN IF EXISTS`.
- `ProcedureInstance` gana la propiedad `ConsolidadoWizardVigente`; EF config `HasColumnName`; snapshot.
- Invalidación en `TramiteLifecycleService`, `OtClientProcedureRepository` y `LicenciaTransitoCommand` (mismos sitios que el maestro; idealmente vía helper que baje **ambos** flags).
- `ConsolidadoCommand`: rama de caché (`if vigente && adjunto existe → sin regenerar`) + cascada (`if !vigente → regenerar FUR y documentos en caliente → consolidar → vigente=true`). Manejar el edge case "vigente=true pero adjunto ausente" (como el maestro con `&& vigente is not null`).

## ADRs relacionados
- [ADR-0022] — Estados de negocio del ciclo de vida (transiciones borrador/rechazado).
- [ADR-0030] — Compositor del consolidado (lo que se regenera y consolida).
- HU #10701 — precedente del flag `consolidado_maestro_vigente`.

## Notas para agentes
- **Database Agent (Fase 2b, requerida)**: materializar `consolidado_wizard_vigente` con migración idempotente Up/Down (patrón HU #10701), `COMMENT` con la semántica de expediente derivado, y validar con `db-schema-validator` (veredicto `OK_TO_MERGE_DB`). RLS: sin política nueva (columna sobre tabla ya protegida por `tenant_isolation`). Considerar el impacto en `tr_procedure_instances_row_version` y `tr_procedure_instances_audit` (cada toggle audita).
- **Backend Agent**: cablear invalidación en los 4 sitios (helper que baje maestro + wizard). En `ConsolidadoCommand`, implementar caché + cascada FUR→consolidado; reutilizar el handler de FUR de forma idempotente. No borrar adjuntos subidos por el usuario (`Source="user"`).
- **Frontend Agent**: NA (la UI ya ofrece "Re-generar FUR/consolidado"; la cascada la maneja el backend).
- **QA Agent**: rechazar → borrador → editar dato → pedir consolidado: FUR y documentos en caliente salen con fecha actualizada y el consolidado refleja el cambio; verificar que documentos del usuario no se borran; caché (segunda descarga sin cambios no regenera); regresión de la máquina de estados; consistencia maestro vs wizard.
- **Security Agent**: sin cambios de permisos; datos personales en los documentos (Habeas Data) sin alteración de controles.
- **Infra Agent**: una migración adicional en el pipeline (idempotente, reversible).

## Referencias externas
- Migración precedente: `20260715022424_HU10701_ConsolidadoMaestroVigente.cs`.
