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

## 4. Cierre

Marcar como hecha la Task del ambiente, hija de la HU #13088, indicando la fecha y quién hizo el alta,
**sin** el secreto.
