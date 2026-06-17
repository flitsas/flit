---
name: flit-rollback-procedure
description: Guía el rollback de despliegues en QA o producción con confirmación paso a paso, verificación de build/commit objetivo y registro en runbook. Usar cuando infra-agent deba revertir un deploy fallido o el usuario solicite rollback controlado. Triggers rollback, revertir deploy, PDN, QA, infra-agent, flit-rollback-procedure.
---

Invocada por **infra-agent**. Siempre supervisado por humano con permisos de infra.

## Checklist

- [ ] Confirmar ambiente y build/commit actual vs objetivo
- [ ] Validar autorización del Líder Técnico (producción)
- [ ] Ejecutar rollback según `@flit-rollback-procedure`
- [ ] Verificar health checks post-rollback
- [ ] Documentar causa y acciones en ADO Discussion o reporte al Líder Técnico

## Pre-flight

1. Leer runbook de rollback del servicio afectado
2. Identificar `build_id` o imagen/tag a restaurar
3. Confirmar ventana de mantenimiento si es producción

## Flujo

1. **Detener tráfico o canary** si el runbook lo indica
2. **Desplegar artefacto anterior** (pipeline, kubectl, o script documentado — usar solo lo del runbook)
3. **Smoke tests** mínimos tras rollback
4. **Notificar** a QA y Tech Lead con timestamp y commit restaurado
5. **Abrir seguimiento** (incidente o bug) si el rollback fue por defecto en PDN

## Prohibido

- Rollback en producción sin autorización explícita
- Saltar verificación post-rollback
- Rollback sin registrar evidencia en reporte
