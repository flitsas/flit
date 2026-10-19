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

| Ambiente | `FLITO_SIGNALS_TARGET_URL` | Encendido |
|---|---|---|
| DEV | `https://dev.operaciones.flitsas.online/api/flito/sync/flit2/avisos` | Sí |
| QA | `https://qa.operaciones.flitsas.online/api/flito/sync/flit2/avisos` | Sí, cuando QA tenga RabbitMQ y core-notificaciones (Epic #13316) |
| PDN | `https://operaciones.flitsas.com/api/flito/sync/flit2/avisos` | **No**, hasta acordarlo con FLITO |

### Encender o rotar la clave

1. En el VPS del ambiente, generar la clave sin dejarla en el historial de la terminal:
   `openssl rand -base64 32` (44 caracteres; el mínimo es 32).
2. Escribirla en el `.env` del ambiente junto con el destino:
   `FLITO_SIGNALS_ENABLED=true`, `FLITO_SIGNALS_TARGET_URL=<la de la tabla>` y `FLITO_SIGNALS_SIGNING_KEY=<clave>`.
3. Entregar la clave a FLITO por el canal fuera de banda acordado (el mismo de los secretos de cliente: **nunca**
   correo, chat, ADO, GitHub ni las herramientas de trabajo con IA). FLITO la carga en su receptor del mismo ambiente.
4. Reiniciar `core-api` (`docker compose up -d core-api`). En el log de arranque debe aparecer
   `Avisos a FLITO encendidos hacia <host>`. Si dice «mal configurados», revisar la URL (HTTPS absoluta) y el largo
   de la clave: core-api arranca igual, pero no envía avisos.
5. Para **rotar**, coordinar la hora con FLITO: los avisos firmados con la clave vieja que sigan en reintento fallarán
   la firma en FLITO, y su lectura de respaldo cada hora los recupera.

### Encender PDN

Solo después de avisar a FLITO, que mantiene la recepción apagada en PDN hasta entonces. Mismos pasos, con la clave
propia de PDN.

### Apagar

`FLITO_SIGNALS_ENABLED=false` y reiniciar `core-api`. Los cambios siguen quedando registrados como pendientes y salen
al volver a encender. FLITO no pierde datos mientras tanto: su lectura de respaldo cada hora lee el feed.
