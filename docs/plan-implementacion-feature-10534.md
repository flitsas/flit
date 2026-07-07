# Plan de implementación — Feature #10534

**Título ADO:** [Validación de Identidad] – Documento de persona natural y vigencia de representante legal
**Requerimientos cubiertos:** R15, R16 · **Bloque:** F9 (1ª ola) · **Esfuerzo:** 9 SP (5 + 2 + 2)
**Estado ADO:** `New` (Feature y 3 HUs) — sin activar · **Fecha:** 2026-07-07

---

## 1. Objetivo

Para **persona natural**, no exigir la carga manual del documento de identidad: usar el documento generado en la validación biométrica (certificado PDF de Kyverum, ya incorporado con `Source=system`). Y **trazar el rol de representante legal vendedor** sobre la vigencia de 30 días de la validación de identidad, que ya está implementada.

## 2. HUs hijas

| HU | Tipo | Título | SP |
|----|------|--------|----|
| #10542 | BACKEND | Persona natural sin carga manual de documento | 5 |
| #10543 | FRONTEND | Selector persona natural/jurídica y checklist condicionado | 2 |
| #10544 | BACKEND | Rol representante legal vendedor y vigencia 30 días | 2 |

## 3. Dependencias y bloqueantes

- **Links formales en ADO:** ninguno (solo jerarquía Feature→HUs). No está bloqueado por, ni bloquea, otro work item.
- **Independiente** de F7 #10531, F6 #10532 y de los bloques de infraestructura transversal (IT-*).
- **Único gate activo:** las HUs están en `New` y requieren el **gate humano de activación** (`New → Active`) con confirmación explícita antes de codificar.

---

## 4. Diagnóstico base (lo que ya existe vs. lo que falta)

| Capacidad | Estado | Dónde en el código |
|-----------|--------|--------------------|
| Validación biométrica Kyverum + certificado PDF | ✅ Maduro | `ProcedureInstanceBiometricValidation.cs`, `Flit.Tramites.Application/Identity/`, `Flit.Infrastructure/Kyverum/` |
| Vigencia 30 días persistida (`ValidUntil`) + reuso por identidad | ✅ Maduro (HU #10350) | `BiometricRules` (`VigenciaDias=30`, `IdentidadKey`), `EnsureIdentityCommand.cs`, `IdentityApprovalResolver.cs`, `SubmitGate.cs` |
| Auto-adjunto de certificado con `Source="system"` en FUR | ✅ Existe | `FurCommand.cs:134` (Source), `:286-333` (descarga PDF Kyverum), `:265-279` (sello identidad) |
| Checklist con ítem `cedulas` obligatorio | ✅ Existe (siempre manual) | `TramiteTipologiaCatalog.cs:75`, `ChecklistEngine.cs`, `ChecklistQuery.cs` |
| **Tipo de persona (natural/jurídica) en el actor** | ❌ **No existe** (solo en `ConsultationTemplate` y en maqueta estática) | *a crear* en `ProcedureInstanceActor` |
| **Rol representante legal del actor** | ❌ **No existe** (solo enum `ParteRol.RepresentanteLegal` marcado "futuro") | *a crear* |

**Conclusión:** Feature de **bajo riesgo**. La infraestructura de identidad, vigencia y auto-adjunto ya está sólida; el grueso es añadir dos atributos al actor (`PersonType`, rol RL) y condicionar checklist / FUR / UI.

---

## 5. HU #10542 — BACKEND (5 SP): Persona natural sin carga manual de documento

**Criterios de aceptación (ADO):**
- AC1 — Actor persona natural → el ítem de cédula no se exige como carga manual.
- AC2 — Actor PN con validación aprobada → al generar FUR/consolidado, el documento de la validación queda incorporado con `Source=system`.
- AC3 — Actor persona jurídica → el ítem de documento sigue disponible para carga manual.

**Componentes/módulos que toca:**

1. **Dominio — `Flit.Tramites.Domain`**
   - `Entities/ProcedureInstanceActor.cs` → nuevo atributo `PersonType` (`Natural` | `Juridica`) + enum en `Tramites/Enums/`.
   - `Tramites/Catalog/TramiteTipologiaCatalog.cs:75` → el ítem `cedulas` pasa a **condicional** (o se filtra en el motor).
   - `Tramites/Services/ChecklistEngine.cs` → si el actor es PN, **omitir/auto-cubrir** el ítem `cedulas` (AC1); PJ lo mantiene (AC3).

2. **Aplicación — `Flit.Tramites.Application`**
   - `UseCases/ProcedureInstances/ChecklistQuery.cs` (`GetChecklistHandler`) → propagar el filtro por `PersonType`.
   - `UseCases/ProcedureInstances/FurCommand.cs` (`GenerarFurHandler`) → para PN, mapear el documento de la validación de identidad (PDF Kyverum, ya con `Source="system"`) como documento de identidad del trámite (AC2). Mismo gancho en `ConsolidadoCommand.cs`.

3. **Infraestructura — `Flit.Infrastructure`**
   - Migración: `ALTER TABLE tramites.procedure_instance_actors ADD COLUMN person_type varchar(10) NULL;` → DDL en `Persistence/Sql/Ddl/` + migración EF en `Persistence/Migrations/`.
   - Configuration EF del actor → mapear la columna.

4. **API — `Flit.Api`**
   - Endpoints de actor y de wizard-state/checklist → aceptar y devolver `personType`.

**Riesgo a cerrar con negocio:** qué archivo vale como "documento de identidad" para PN — certificado PDF Kyverum vs. imagen capturada. **Propuesta:** el certificado PDF (ya se adjunta con `Source=system`).

---

## 6. HU #10543 — FRONTEND (2 SP): Selector PN/PJ y checklist condicionado

**Criterio de aceptación (ADO):**
- AC1 — Al seleccionar persona natural en el formulario del actor, el paso de documentos no ofrece la carga manual de cédula.

**Componentes/módulos que toca:**

1. `frontend/components/operacion/ActorsForm.tsx` → añadir el **selector Persona Natural / Persona Jurídica**. Referencia visual existente en la maqueta `atom/StepperForm.tsx:242-255` (respetar con `flit-design-guardian`).
2. `frontend/lib/api/types/procedure-runtime.ts` → añadir `personType` a la interfaz `ProcedureActor`.
3. `frontend/lib/api/tramites-client.ts` → enviar `personType` al backend.
4. Paso de documentos del wizard (`TramiteWizard.tsx` / render del checklist) → como el backend ya omite `cedulas` para PN, el front solo renderiza el checklist del backend; verificar que **no ofrezca carga manual de cédula** para PN (AC1).

> `StepperForm.tsx` es maqueta de demo no conectada; el trabajo real es en `ActorsForm.tsx`.

---

## 7. HU #10544 — BACKEND (2 SP): Rol representante legal vendedor + vigencia 30 días

**Criterios de aceptación (ADO):**
- AC1 — RL vendedor con validación aprobada hace 10 días → al iniciar nuevo traspaso en el mismo tenant, se reutiliza la validación vigente sin exigir nueva.
- AC2 — Actor marcado como RL vendedor → el rol queda persistido y visible para trazabilidad.

La vigencia de 30 días **ya aplica a todos los actores**; el gap es **semántico / de trazabilidad** (opción mínima recomendada en ficha R16).

**Componentes/módulos que toca:**

1. **Dominio** — `Entities/ProcedureInstanceActor.cs` → flag `EsRepresentanteLegal` (o rol vía enum `ParteRol.RepresentanteLegal`), persistido para trazabilidad (AC2).
2. **Aplicación** — `EnsureIdentityCommand.cs` / `IdentityApprovalResolver.cs` → **verificar** (no reimplementar) que el reuso por `IdentidadKey` dentro del mismo tenant cubre al RL vendedor con validación <30 días (AC1).
3. **Infraestructura** — migración opcional `ADD COLUMN es_representante_legal boolean DEFAULT false` (fusionable con la migración de #10542).
4. **API** — el actor acepta `esRepresentanteLegal`.

> **No** se toca el cálculo de `BiometricRules` salvo que negocio confirme una vigencia distinta por rol (opción completa, +1 SP). Hoy es uniforme: 30 días.

---

## 8. Transversal (todas las HUs)

- **Migraciones:** idealmente **una sola** que agregue `person_type` + `es_representante_legal` a `procedure_instance_actors`. Generar EF con `Flit.Infrastructure` como startup (Flit.Api corriendo bloquea el build por locks de `bin`).
- **Tests (obligatorio, skill `dev-tester`):**
  - `Flit.Tramites.Tests`: checklist PN omite cédula / PJ la conserva; FUR incorpora el documento PN; reuso RL <30 días.
  - Frontend (Jest/Vitest + RTL): selector PN/PJ en `ActorsForm`, ocultar carga de cédula para PN.
- **Sin dependencias** con F7 #10531, F6 #10532 ni bloques IT-*.

---

## 9. Estrategia de entrega (consistente con la ola)

- Rama única `feature/AB-10534-identidad-pn-vigencia-rl` desde `develop`, **un commit por HU** (`HU10542:`, `HU10543:`, `HU10544:`).
- Orden: **#10542 (BE) → #10543 (FE) → #10544 (BE)**.
- **1 PR** a `develop` (≤ 800 líneas), con Custom.Commits de las 3 HUs y Discussion de evidencia en el Feature #10534.
- Tras merge en DEV confirmado → HUs a `Resolved` + entrega a QA (gate humano de merge + reviewer humano real).

---

## 10. Gates FLIT pendientes

| Gate | Momento | Acción |
|------|---------|--------|
| **Activar HU** (`New → Active`) | Antes de codificar | Confirmación humana explícita ("sí"). No omitible. |
| **Merge de PR** | Antes de mergear a `develop` | Confirmación explícita + reviewer humano real + Code Review/Security/Build succeeded. |
| **Cerrar HU** (`Resolved`) | Tras merge en DEV confirmado | Solo con evidencia objetiva de merge. |
| **Cerrar Feature** (`Closed`) | Al terminar todas las HUs | Exclusivo del Product Owner humano. |

---

## 11. Decisiones abiertas para tu revisión

1. **Documento de identidad para PN:** ¿se usa el certificado PDF de Kyverum (recomendado) o la imagen de cédula capturada en la validación?
2. **Vigencia por rol:** ¿la vigencia de 30 días para el RL vendedor es la misma que para todos (opción mínima, 2 SP) o negocio exige una distinta (opción completa, +1 SP)?
3. **Persistencia del rol RL:** ¿flag booleano `es_representante_legal` o uso del enum `ParteRol.RepresentanteLegal` ya existente?
