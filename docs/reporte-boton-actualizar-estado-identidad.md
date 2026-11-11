# Reporte: ¿para qué sirve el botón «Actualizar estado» en la validación de identidad?

> Generado: 2026-11-10 · base `origin/develop` 507a043a7 · verificado leyendo código (anclas `archivo:línea` derivan: reverificar antes de citarlas).

## Resumen

En FLIT hay **dos botones con el mismo texto «Actualizar estado» y comportamientos distintos**:

| Dónde | Qué hace realmente | ¿Consulta a Kyverum Verify? |
|---|---|---|
| **Módulo Validaciones → detalle de la persona** (`PersonIdentityDetailDrawer`) | Vuelve a leer de la BD de FLIT el detalle de la persona. Equivale a refrescar la pantalla. | **No** |
| **Wizard del trámite → paso de identidad** (`BiometricStep`, `ReconcileAction`) | Pregunta a Kyverum Verify el estado actual de la verificación y, si cambió, lo aplica en FLIT. | **Sí** |

La consecuencia práctica: en el módulo Validaciones, si el aviso (webhook) de Verify se perdió, pulsar «Actualizar estado» **no desatasca nada**; solo muestra lo que FLIT ya sabe. Para forzar la consulta al proveedor hay que ir al paso de identidad del trámite (o esperar al proceso de fondo descrito abajo).

---

## 1. Botón del módulo Validaciones (detalle de la persona)

- **Código:** `frontend/components/atom/modules/PersonIdentityDetailDrawer.tsx:225-234` → `onClick={() => void load()}`.
- **Qué llama:** `GET /api/v1/tramites/biometric-validations/by-person/detail` (o la variante de red `network/identity-validations/by-person/detail`). El handler `ListPersonBiometricValidationsHandler` solo lee la BD; no tiene cliente de Kyverum (`ListPersonBiometricValidationsQuery.cs:25-113`).
- **Visibilidad:** siempre visible; sin condición de rol ni de estado.
- **Además, el detalle se refresca solo** cada 5 s mientras alguna validación no esté en estado final y la pestaña esté visible (`PersonIdentityDetailDrawer.tsx:54`, `:150-158`). Si un estado cambia, refresca también el listado del módulo (`onStatusChanged`).
- **Hueco detectado:** si la recarga falla cuando ya hay datos en pantalla, el error **no se muestra** (el aviso solo aparece si no hay datos, `:256`) y el refresco automático se detiene. El usuario puede creer que el estado está al día.

**Para qué sirve, en una línea:** refrescar a demanda lo que FLIT ya tiene guardado (útil si el refresco automático se detuvo o la validación ya era final).

## 2. Botón del paso de identidad del trámite (reconciliación con Verify)

- **Código:** `frontend/components/operacion/BiometricStep.tsx:1119-1178` (`ReconcileAction`, aria-label «Actualizar estado de la validación consultando al proveedor»).
- **Qué llama:** `POST /api/v1/tramites/instances/{instanceId}/biometric/{validationId}/reconcile` (`BiometricaEndpoints.cs:447-472`) → `ReconciliarIdentidadHandler` (`ReconciliarIdentidadCommand.cs:57-126`).
- **Cuándo aparece:** validación en `enviado`, `pendiente_envio` o `en_proceso` (con o sin enlace de captura vigente), **15 s después** de abrir el paso, y oculto en modo solo lectura. No filtra por rol. Deshabilitado si no hay trámite (`BiometricStep.tsx:677-706`, `:1133-1144`).

### Qué hace, paso a paso

1. Si la validación no es de Kyverum o no tiene id de Verify → **409**.
2. Si ya está en estado final → no hace nada (salvo un rechazo con intentos disponibles).
3. Llama a Kyverum: `GET /v1/validations/{kyverumVerificationId}` (`KyverumVerifyClient.cs:119-132`).
4. **Reinicia el presupuesto de sondeo del proceso de fondo** (`ReconcilePollCount = 0`) y guarda aunque Verify aún no resuelva (`ReconciliarIdentidadCommand.cs:92-100`).
5. Aplica el estado con la **misma lógica que el webhook y el proceso de fondo** (`IdentityValidationReconciler.ApplyStatusAsync`).
6. Deja auditoría con `stage=reconcile`, también en los fallos.

### Qué produce según la respuesta de Verify

| Respuesta | Efecto en FLIT |
|---|---|
| **aprobado** | `Approve`: estado, fecha de validación y **vigencia** (`valid_until`); sello (`CertificateHash`) si viene `firmaSerie`. Bitácora «Identidad del {parte}: aprobada». Publica `IdentityValidationCompleted` → por outbox **encadena la firma automática** de los trámites pendientes de firma de esa persona (compraventa o FUR con sello). |
| **rechazado** con intentos agotados | Estado rechazado + bitácora. Publica el mismo evento. |
| **rechazado** con intentos disponibles | Solo refresca el detalle del proveedor; la persona puede reintentar. |
| **expirado** | Cambia el estado a expirado **sin evento ni bitácora** (hueco: no deja rastro). |
| Score bajo el umbral | Evento de advertencia en bitácora (RF40), solo con trámite. |
| Sin cambio | Mensaje «El proveedor aún no resuelve la validación. Intenta de nuevo en unos segundos.» |
| Error | 502 error del proveedor · 503 proveedor no disponible · 404 · 500; el front muestra el mensaje o «No se pudo actualizar el estado.» |

Fuentes: `IdentityValidationReconciler.cs:47-108`, `IdentityValidationResultApplier.cs:62-208`, `IdentityValidationCompletedConsumer.cs:24-71`.

**No cambia el estado del trámite directamente ni envía correos por sí mismo** (no se revisó si algún consumidor de la outbox envía correos río abajo).

## 3. Por qué existe: el proceso de fondo y el webhook

El camino normal es que Verify **avise a FLIT por webhook**. Si el aviso no llega (por ejemplo, un callback mal ruteado entre ambientes), existen dos mecanismos de recuperación (`ReconciliarIdentidadCommand.cs:10-15`, `docs/runbook-validacion-identidad-colgada.md`):

1. **Proceso de fondo** `IdentityValidationReconcileProcessor` (`Flit.Infrastructure/Messaging/IdentityValidationReconcileProcessor.cs:25-42`, `:96-139`):
   - arranca a los 20 s y corre cada 30 s, 10 validaciones por ciclo, ignorando las tocadas hace menos de 120 s;
   - **3 sondeos rápidos** por validación; después, un sondeo lento cada **10 min** hasta que vence;
   - cierra como expiradas, sin consultar, las de trámites anulados/revocados y las vencidas.
2. **El botón manual** del paso de identidad: consulta en el acto y **devuelve los 3 sondeos rápidos** al proceso de fondo.

Es decir: el botón manual no tiene efectos propios distintos del webhook o del proceso de fondo; su valor es **adelantar** la consulta y **revivir** el sondeo rápido. Equivalente para mandatarios: «Consultar estado» → `POST .../mandate-signers/{id}/identity-validation/reconcile`.

## 4. Limitaciones encontradas

1. **Mismo nombre, distinto efecto.** El usuario del módulo Validaciones espera que «Actualizar estado» consulte al proveedor, y no lo hace.
2. **Prevalidaciones sin trámite no se pueden reconciliar a mano:** el endpoint exige `instanceId` y que coincida con la validación (`ReconciliarIdentidadCommand.cs:26`). Solo les queda el proceso de fondo.
3. **Error silencioso** en la recarga del detalle (sección 1).
4. **Expirado por consulta sin rastro** en bitácora ni evento.
5. **Stub local de Verify:** el código llama `GET /v1/validations/{id}`. El stub del puerto 8899 implementa esa ruta, pero no está versionado (vive en un scratchpad de sesión), así que no se pudo verificar contra este código.

## 5. Opciones (para decisión del PO; no implementadas)

- **a)** Renombrar el botón del módulo Validaciones a «Recargar» (cambio de texto, sin riesgo).
- **b)** Hacer que el botón del módulo Validaciones consulte a Verify reutilizando la reconciliación; requiere un endpoint por `validationId` sin trámite para cubrir prevalidaciones (cambio de backend + permisos).
- **c)** Mostrar el error de la recarga aunque ya haya datos, y registrar en bitácora el expirado por consulta.
