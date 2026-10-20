# Runbook — Alta de los clientes de integración de Flito

> HU #13088 (AC4) · Feature #13065 · Épica #12737 · ADR-0067 · Contrato: `external-api-tramites-sync.md` §2.
> Se ejecuta una vez por ambiente, al desplegar: `flito-dev` en DEV, `flito-qa` en QA y `flito-pdn` en PDN.

## Antes de empezar

- El ambiente tiene montada la llave del pase externo (`ExternalJwt__PrivateKeyPem`). Sin ella,
  `POST /api/v1/external/auth/token` responde `503 external_auth_unavailable`.
- Quien ejecuta es **superadministrador** de la plataforma y tiene su token de sesión.
- Está acordado con Flito **por dónde se entrega el secreto**: un canal seguro fuera de banda, **nunca**
  correo, chat, ADO, GitHub ni las herramientas de trabajo con IA.

## 1. Alta

```http
POST /api/v1/admin/external-clients
Authorization: Bearer <token de superadministrador>
Content-Type: application/json

{
  "clientId": "flito-dev",
  "displayName": "Flito (DEV)",
  "purpose": "Sincronización de trámites para procesos Flito",
  "scopes": ["external.tramites.read", "external.tramites.pii.read"]
}
```

Respuesta `201` con `client` y `clientSecret`. **El secreto se muestra una sola vez**: no se guarda en
claro en ningún sitio y no se puede volver a consultar. Copiarlo directamente al canal acordado con
Flito y no dejarlo en el historial de la terminal ni en archivos.

- `409 client_id_taken`: el identificador ya existe o existió (no se reutilizan). Si el cliente existe
  pero se perdió el secreto, ir al paso 3.

## 2. Comprobación

1. `GET /api/v1/admin/external-clients` muestra el cliente activo con ambos permisos y sin secreto.
2. Flito pide su pase con el secreto recibido y confirma el `200` (no hace falta que FLIT lo pruebe con
   el secreto: así nadie más lo usa).

## 3. Operación posterior

| Situación | Llamada |
|---|---|
| Rotación planificada (el anterior sigue valiendo 24 h) | `POST /api/v1/admin/external-clients/{id}/regenerate-secret` |
| Secreto filtrado (el anterior deja de valer al instante) | ídem con `{"revocarAnterior": true}` |
| Bloqueado por 5 intentos fallidos (423) | `POST /api/v1/admin/external-clients/{id}/unlock` |
| Cortar el acceso | `PATCH /api/v1/admin/external-clients/{id}` con `{"isActive": false}` |
| Obligar a rotar | `PATCH …/{id}` con `{"mustRotate": true}`; se levanta al regenerar |

Cada cambio queda en `audit.audit_logs` con el superadministrador en `updated_by`.

## 4. Habilitar el envío de adjuntos (HU #13263, contrato v3.2 §7)

Un cliente ya dado de alta recibe el permiso nuevo `external.tramites.attachments.write` **sin rotar el secreto**.

**Desde la UI (recomendado):** Usuarios → Clientes de integración → editar el cliente → marcar «Envío de adjuntos» →
Guardar cambios. El listado muestra la etiqueta «Adjuntos». El formulario conserva los permisos que no administra.

**Por API (alternativa):** `PATCH` reemplaza la lista completa de permisos: enviar los vigentes más el nuevo.

```http
PATCH /api/v1/admin/external-clients/{id}
Authorization: Bearer <token de superadministrador>
Content-Type: application/json

{ "scopes": ["external.tramites.read", "external.tramites.pii.read", "external.tramites.attachments.write"] }
```

El permiso se lee de la base al canjear el pase: Flito lo recibe en su **próximo pase** (los pases vigentes duran
30 minutos y no lo traen). Comprobación: `GET /api/v1/admin/external-clients` muestra los tres permisos.

## 5. Cierre

Marcar como hecha la Task del ambiente, hija de la HU #13088, indicando la fecha y quién hizo el alta,
**sin** el secreto.

## 6. Avisos en tiempo real a FLITO (HU #13625, Épica #13614)

FLIT 2 avisa a FLITO, con un webhook firmado, cada vez que un trámite radicado cambia. FLITO trae el cambio del feed
v3.1. Es independiente del cliente de integración de los pasos 1 a 4: aquí no hay secreto de cliente, sino una
**clave HMAC por ambiente**, la misma para todos los tenants.

| Ambiente | Destino (`TargetUrl`) | Encendido |
|---|---|---|
| DEV | `https://dev.operaciones.flitsas.online/api/flito/sync/flit2/avisos` | Sí, desde el 2026-10-20 (evidencias en la Épica #13614) |
| QA | `https://qa.operaciones.flitsas.online/api/flito/sync/flit2/avisos` | Sí, cuando QA tenga RabbitMQ y core-notificaciones (Epic #13316) |
| PDN | `https://operaciones.flitsas.com/api/flito/sync/flit2/avisos` | **No**, hasta acordarlo con FLITO |

### Dónde se configura

core-api lee la sección `Integrations:FlitoSignals`. El nombre de cada variable depende de cómo se despliega el
ambiente:

| Valor | Ambiente con GitOps (k3s, repo `flitsas/flit-gitops`) | Ambiente con `docker compose` (`.env` del VPS) |
|---|---|---|
| Encendido | `Integrations__FlitoSignals__Enabled: "true"` en el ConfigMap `core-api-config` | `FLITO_SIGNALS_ENABLED=true` |
| Destino | `Integrations__FlitoSignals__TargetUrl` en el ConfigMap `core-api-config` | `FLITO_SIGNALS_TARGET_URL` |
| Clave | `Integrations__FlitoSignals__SigningKey` en el SealedSecret `core-api-secrets` | `FLITO_SIGNALS_SIGNING_KEY` |

- Desde el 2026-10-17, DEV corre en el k3s de la 177 (namespace `flit2-dev`, carpeta `services/flit2-dev/`). Su `.env`
  del VPS **ya no tiene efecto**: lo que se escriba ahí no llega a core-api. QA siguió el mismo camino (`flit2-qa`).
  Antes de tocar un ambiente, revisar en el job de despliegue del CD (`deploy-*-gitops` o `deploy-to-vps`) cómo se
  despliega.
- Una variable nueva en `docker-compose.prod.yml` **no llega sola** a GitOps: hay que agregarla al ConfigMap o al
  SealedSecret del ambiente.
- Las claves de DEV, QA y PDN ya están selladas en los dos lados (flit-gitops PR 31): `core-api-secrets` en FLIT 2 y
  `FLIT2_AVISOS_SECRETO` en FLITO. **No sellar una clave nueva** salvo que se esté rotando: si los dos lados no tienen
  la misma, todas las firmas fallan con 401.
- Cambiar flit-gitops requiere permiso de escritura en ese repo. Quien no lo tenga le pasa el cambio a quien lo
  administre.

### Encender o rotar la clave

1. **Clave**, solo si es nueva o se está rotando. En el servidor del ambiente, generarla sin dejarla en el historial de
   la terminal: `openssl rand -base64 32` (44 caracteres; el mínimo es 32).
   - GitOps: sellarla con la llave del clúster y llevar el resultado (cifrado; puede ir a git) a `core-api-secrets` →
     `spec.encryptedData`:
     `printf '%s' "$KEY" | kubeseal --raw --namespace <ns> --name core-api-secrets --controller-namespace kube-system --controller-name sealed-secrets-controller`
   - `docker compose`: escribirla en el `.env` como `FLITO_SIGNALS_SIGNING_KEY=<clave>`.
2. **Entregar la clave a FLITO** por el canal fuera de banda acordado (el mismo de los secretos de cliente: **nunca**
   correo, chat, ADO, GitHub ni las herramientas de trabajo con IA). FLITO la carga en su receptor del mismo ambiente
   y lo enciende. Para comprobar que ya está encendido: un POST sin firma al destino pasa de 404 a **401**. Para
   comprobar que los dos lados tienen la misma clave sin mostrarla, cada lado compara los 8 primeros caracteres del
   SHA-256 del valor.
3. **Encendido y destino**: `Enabled` en `true` y `TargetUrl` con el destino de la tabla, en el ConfigMap (GitOps) o
   en el `.env` (`docker compose`).
4. **Probar la red desde el ambiente**, antes de reiniciar. FLIT 2 y FLITO pueden compartir servidor (en la 177
   comparten el k3s), así que la prueba se hace desde dentro:
   `kubectl -n <ns> exec deploy/core-notificaciones -- sh -c "wget -S -O /dev/null --post-data='' <destino> 2>&1 | grep HTTP/"`
   (con `docker compose`: `docker compose exec core-notificaciones …`). Debe responder **401**. Siempre se usa el
   hostname HTTPS público. Un `/etc/hosts` o un Service interno que resuelva a una IP privada no sirve: el filtro
   anti-SSRF de core-notificaciones lo bloquea, y es correcto que lo haga.
5. **Cerrar los avisos pendientes, justo antes del reinicio** (ver «Encender por primera vez» más abajo).
6. **Reiniciar `core-api`**. Un cambio de ConfigMap o de Secret **no reinicia el pod**, y `Enabled` solo se lee al
   arrancar: sin este paso los avisos siguen apagados aunque Argo CD ya haya aplicado el cambio.
   - GitOps: `kubectl -n <ns> rollout restart deployment/core-api` y `kubectl -n <ns> rollout status deployment/core-api`.
   - `docker compose`: `docker compose up -d core-api`. `docker compose restart` no relee el `.env`.
7. **Verificar**:
   - Log de arranque (`kubectl -n <ns> logs deploy/core-api | grep -i "avisos a flito"`): debe decir
     `Avisos a FLITO encendidos hacia <host>` (EventId 13626). `apagados` (13625) significa que el reinicio fue
     anterior al cambio. `mal configurados` (13627) significa que hay que revisar la URL (HTTPS absoluta) y el largo
     de la clave: core-api arranca igual, pero no envía avisos.
   - Primer aviso: cambiar un trámite radicado y, unos 6 a 8 s después, comprobar la entrega:
     `SELECT resultado, codigo_http, ocurrido_en FROM notificaciones.webhooks WHERE origen = 'flito' ORDER BY ocurrido_en DESC LIMIT 5;`
     Debe salir `entregado` con 200.
8. Para **rotar**, coordinar la hora con FLITO: los avisos firmados con la clave vieja que sigan en reintento fallarán
   la firma en FLITO, y su lectura de respaldo los recupera.

### Encender PDN (o encender por primera vez un ambiente con historia)

Solo después de avisar a FLITO, que mantiene la recepción apagada en PDN hasta entonces. Mismos pasos, con la clave
propia de PDN.

Mientras los avisos están apagados, cada trámite que cambia queda con su aviso pendiente. En el primer encendido
saldría un aviso por cada trámite modificado desde el despliegue, que pueden ser miles. No se pierde nada, pero
FLITO ya tiene esos cambios por su lectura periódica. Para evitar la ráfaga, **justo antes** del paso 6 (reiniciar
`core-api`), cerrar los pendientes:

```sql
UPDATE integrations.flito_sync_signals
   SET pendiente_desde = NULL
 WHERE pendiente_desde IS NOT NULL;
```

Los cambios posteriores vuelven a quedar pendientes y salen normalmente. Si entre esta sentencia y el reinicio cambia
algún trámite, ese cambio sale avisado (no se pierde).

### Apagar

`Enabled` en `false` (ConfigMap o `.env`) y reiniciar `core-api` como en el paso 6. Los cambios siguen quedando
registrados como pendientes y salen al volver a encender. FLITO no pierde datos mientras tanto: su lectura de respaldo
lee el feed.
