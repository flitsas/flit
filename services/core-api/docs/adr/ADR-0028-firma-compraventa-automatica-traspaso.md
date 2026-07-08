# ADR-0028 — Firma de compraventa no bloqueante en traspaso (desbloqueo temporal)

- **Estado**: Propuesto · 2026-07-08 (revisado 2026-07-08 — alcance simplificado por PO)
- **Módulo**: Trámites — Traspaso (TR), gate de preparación / radicación
- **Requerimientos**: Pendiente B12 (`PendientesFLIT2.0MI-TR.xlsx`): la firma de compraventa **no debe impedir** finalizar el trámite `traspaso_standard`
- **Relacionado**: HU #10459 (gate traspaso original), C10 (consolidado), HU #10349 (auto-solicitud firma)
- **Decide**: Líder Técnico

## Contexto

En **traspaso**, `SubmitGate` (HU #10459) exige firma electrónica de compraventa **firmada** por
comprador **y** vendedor antes de pasar a `preparado`/radicar (`firma_compraventa_requerida`). En la
práctica el gestor debe solicitar y simular la firma por actor (mock DEV), lo que **bloquea** el
cierre del trámite mientras negocio define la lógica ideal de firmas (ZapSign, baúl, flujo legal).

**Decisión de producto (Samuel Cárdenas, 2026-07-08):** B12 no implementa aún la automatización
completa del flujo de firmas. Solo **elimina el impedimento** para avanzar el traspaso. La lógica
definitiva se ajustará cuando negocio la entregue.

## Decisión

Para instancias **`traspaso_standard`**:

1. **`SubmitGate`:** omitir la validación `FirmaCompraventaAmbas` — **no** devolver
   `firma_compraventa_requerida`. El resto del gate traspaso se mantiene (documentos, biométricas,
   FUR, organismo, impronta si aplica).
2. **Wizard (`WizardStateQuery`):** la firma de compraventa **no** bloquea `canSubmit` ni aporta
   `pendiente_firma` como reason bloqueante. El check preflight `firma_compraventa` puede permanecer
   informativo (`warn`/`green`) sin afectar submit.
3. **UI (`FirmaFurStep`):** la sección de firma pasa a ser **informativa** (estado por parte si
   existe) o se minimiza; **no** se exige interacción del gestor para avanzar. Copy alineado: firma
   pendiente de definición de negocio, no bloqueante.
4. **Endpoints de firma:** se conservan (`POST signatures`, simulate, portal) para no romper DEV ni
   trabajo futuro; simplemente dejan de ser prerequisito de ciclo de vida.
5. **`IdentityValidationCompletedConsumer`:** sin cambio obligatorio en B12 (puede seguir
   auto-solicitando; es opcional y no bloqueante).

**Matrícula inicial:** sin cambios.

**Reversión futura:** cuando negocio entregue la lógica ideal, reactivar el gate y/o sustituir por
flujo ZapSign/baúl con un ADR que superseda este.

## Alternativas consideradas

### Alternativa A — Quitar gate de firma en submit/wizard (RECOMENDADA)

- (+) Cambio mínimo; desbloquea traspaso y facilita C10.
- (+) No invierte en mock auto-chain que negocio podría descartar.
- (+) Endpoints y modelo de firmas intactos para evolución.
- (−) Traspaso puede radicarse sin firmas electrónicas (excepción temporal consciente).
- Esfuerzo: **bajo**. Riesgo: bajo técnico; medio negocio (mitigado: temporal explícito).

### Alternativa B — Encadenamiento async completo (mock auto-complete + FUR auto)

- (+) Automatiza el flujo DEV sin quitar el gate.
- (−) Mayor esfuerzo; negocio aún no definió lógica ideal.
- (−) Descartada por PO en revisión B12.
- Esfuerzo: medio. Riesgo: retrabajo alto.

### Alternativa C — Feature flag por tenant

- (+) Permite pilotos con/sin gate.
- (−) Complejidad innecesaria para un desbloqueo temporal.
- Esfuerzo: medio. Riesgo: bajo.

## Consecuencias por agente

- **Backend:** quitar check en `SubmitGate.EvaluateTraspaso`; ajustar `WizardStateQuery` (paso 6 /
  reasons); actualizar `SubmitGateTraspasoTests` y tests wizard si aplica.
- **Frontend:** `FirmaFurStep` / `wizard-copy` — firma no bloqueante; mensajes informativos.
- **QA:** traspaso sin firmas puede preparar/radicar si resto de gates OK; regresión matrícula.
- **Security:** sin cambio de permisos en endpoints de firma.
- **Infra:** sin migración.

## Requisito vs decisión (trazabilidad)

| Pendiente Excel / PO | Decisión ADR |
|----------------------|--------------|
| B12 — Firma compraventa no debe impedir el traspaso | Gate `firma_compraventa_requerida` omitido en traspaso; wizard/UI no bloquean |
| Lógica ideal de firmas | **Fuera de alcance** — ADR futuro cuando negocio defina |
| C10 (consolidado) | Se beneficia al no depender de firmas simuladas |
