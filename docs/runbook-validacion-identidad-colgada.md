# Runbook — Validación de identidad colgada en `en_proceso`

**Cuándo aplica:** una validación de identidad (Kyverum) se queda en `en_proceso` / la pantalla muestra
"Esperando validación de …" indefinidamente, aunque la persona ya completó la captura (el enlace dice
"ya está aprobada").

**Causa típica:** el webhook `validation.completed` de Kyverum no fue procesado por el backend del ambiente.
El caso confirmado en PDN fue **HTTP 500 en el webhook**: el handler no pudo descifrar el secreto de firma
(`CryptographicException` del keyring de Data Protection). Ver [Causa raíz](#causa-raíz-keyring-de-data-protection).

> Desde 2026-07-02 el webhook está endurecido: **no da 500** y, si no puede verificar la firma, **reconcilia
> consultando a Kyverum** (self-heal). Además hay un endpoint y un worker de reconciliación. Este runbook
> cubre cómo diagnosticar y desatascar manualmente cuando haga falta.

---

## 0. Consulta ÚNICA de diagnóstico (tabla de auditoría)

Toda la historia de una validación queda en `tramites.identity_validation_audit` (envío, llegada del webhook,
si había secreto, si **descifró o no** el cifrado, firma, resultado, reconciliación y errores). Una sola query,
sin cruzar tablas ni ir a los logs del pod:
```sql
select occurred_at, stage, outcome, http_status,
       signature_present, secret_present, decrypt_ok, error_type, provider_status, message
from tramites.identity_validation_audit
where validation_id = '<validation-id>'          -- o kyverum_verification_id = '<kyv-id>'
order by occurred_at;
```
Lectura típica del caso PDN (webhook que no descifra):
- `stage=webhook_received, outcome=received, secret_present=true` → el webhook **llegó**.
- `stage=webhook_not_verifiable, outcome=decrypt_failed, decrypt_ok=false, error_type=CryptographicException`
  → **el cifrado no coincide** (keyring/ApplicationName). **Este es el diagnóstico**, sin mirar el pod.
- `stage=reconcile, outcome=aprobado` → el respaldo por consulta lo resolvió.

> La tabla se llena sola desde el build endurecido. La migración `20260702183303_IdentityValidationAudit`
> se aplica **automáticamente al desplegar** (la app corre las migraciones pendientes al arrancar,
> `Database:AutoMigrate=true` por defecto).

## 1. Diagnóstico rápido (detalle por fuente)

### a) Estado local de la validación (BD del ambiente)
```sql
select id, status, provider_status,
       webhook_secret_encrypted is not null as tiene_secreto,
       kyverum_verification_id, created_at
from tramites.procedure_instance_biometric_validations
where id = '<validation-id>';
```
- `status = en_proceso` y `provider_status = enviado` → nunca llegó/procesó el webhook.

### b) Estado REAL en Kyverum (fuente de verdad)
```bash
curl -s -H "Authorization: Bearer $KYVERUM_API_KEY" \
  "https://verify.kyverum.com/v1/validations/<kyverum_verification_id>" | jq '.status, .result.aprobado'
```
- `status = "aprobado"` (o `rechazado`) → Kyverum ya resolvió; el problema es 100% del webhook → reconciliar.
- `status = "enviado"` → la persona **no ha terminado** la captura; `en_proceso` es correcto, no hay nada roto.

### c) Entrega del webhook (plataforma Kyverum)
En el panel de Kyverum, la validación muestra el estado de la notificación. Si dice
`validation.completed fallido · N intento(s)`:
- **HTTP 500** → keyring (secreto indescifrable). Ver causa raíz.
- **HTTP 401** → firma inválida / secreto ausente.
- **HTTP 404** → routing/ingress no llega al backend, o el ambiente no tiene el endpoint desplegado.
- **No hay intentos** → Kyverum no envió, o `KYVERUM_WEBHOOK_CALLBACK_URL` mal configurada.

> Las URLs de webhook están **diferenciadas por ambiente** (no es misrouting):
> DEV `api.dev.flitsas.online` · QA `api.qa.flitsas.online` · PDN `api.flitsas.online`.

---

## 2. Desatascar (de lo más rápido a lo de fondo)

### Opción A — Endpoint de reconciliación (inmediato, recomendado)
Consulta el estado a Kyverum y lo aplica. Idempotente.
```bash
curl -X POST \
  "https://<host-del-ambiente>/api/v1/tramites/instances/<instance-id>/biometric/<validation-id>/reconcile" \
  -H "Authorization: Bearer <JWT>" \
  -H "X-Tenant-Id: <tenant-id>"
# → 200 { "status": "aprobado", "updated": true }
```
- **Token:** el endpoint es tenant-scoped por el JWT. Para reconciliar una validación de un tenant que no
  es el tuyo, usa un usuario **SuperAdmin** (respeta el `X-Tenant-Id` del header). Un usuario de compañía
  queda forzado a su propio tenant.
- Respuestas: `404` no existe · `409` no es Kyverum · `502/503` proveedor no disponible.

### Opción B — Worker automático (sin intervención)
`IdentityValidationReconcileProcessor` sondea cada ~30s las validaciones `en_proceso` de Kyverum sin tocar
hace >60s y no expiradas, consulta a Kyverum y aplica. Basta con que el servicio esté desplegado y corriendo:
la validación se destraba sola en el siguiente ciclo. No requiere acción manual.

### Opción C — Reintento del webhook (si Kyverum lo permite)
Con el webhook ya endurecido, un reintento desde el panel de Kyverum ahora se procesa (self-heal por consulta)
y responde 200.

---

## 3. Causa raíz: keyring de Data Protection

El secreto HMAC del webhook se guarda **cifrado con Data Protection** (`webhook_secret_encrypted`). Si el
keyring no está **persistido y compartido** entre réplicas/reinicios, la réplica que procesa el webhook no
tiene la llave con que se cifró al crear la validación → `Unprotect` falla → (antes) 500.

**Fix de fondo (PR #50, ya en el código — falta asegurarlo en el ambiente):**
`AddDataProtection().PersistKeysToDbContext<FlitDbContext>().SetApplicationName("flit-core-api")`.

Verificar en el ambiente afectado (p.ej. PDN):
```sql
select count(*) from data_protection_keys;   -- debe ser >= 1
```
- La migración `HU10233_DataProtectionKeys` se aplica **sola al desplegar** (`Database:AutoMigrate=true`
  por defecto). Si la tabla no existe, revisar que el deploy no haya desactivado `Database__AutoMigrate`.
- Que la llave exista NO garantiza que el descifrado funcione: confirmar que **todas las réplicas** usan el
  mismo `ApplicationName` (`flit-core-api`) y comparten esa tabla — un `ApplicationName` distinto (o un pod sin
  la persistencia) hace fallar `Unprotect` aunque la llave esté (es lo que se vio en PDN: key-id coincide, 500 igual).

Con esto, la **vía rápida** (verificación HMAC del cuerpo) vuelve a funcionar y se evita la consulta extra a
Kyverum en cada webhook.

---

## 4. Reconciliación en lote (tras un incidente)

Encontrar candidatas colgadas en el ambiente:
```sql
select id, procedure_instance_id, tenant_id, kyverum_verification_id, created_at
from tramites.procedure_instance_biometric_validations
where provider = 'kyverum'
  and status = 'en_proceso'
  and kyverum_verification_id is not null
  and expires_at > now()
order by created_at;
```
- El **worker** las tomará automáticamente. Para forzar de inmediato, llamar el endpoint de la Opción A por
  cada una (o esperar el ciclo del worker).

---

## 5. Referencias

- **Webhook:** `POST /api/v1/webhooks/kyverum-verify/{validationId}` (público, firma HMAC; robusto: nunca 500,
  self-heal por consulta si no puede verificar la firma).
- **Reconcile on-demand:** `POST /api/v1/tramites/instances/{id}/biometric/{validationId}/reconcile`.
- **Worker:** `IdentityValidationReconcileProcessor` (Infrastructure/Messaging).
- **Consulta a Kyverum:** `GET /v1/validations/{id}` (Bearer `KYVERUM_API_KEY`) — estado; `.../certificado` — PDF.
- **Config por ambiente:** `KYVERUM_WEBHOOK_CALLBACK_URL` (DEBE ser el host público del propio ambiente).
- **Nota de contexto:** el keyring y el fix están descritos en la memoria del proyecto
  (`kyverum-webhook-dataprotection-keyring`).
