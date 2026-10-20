# API de contratos de Renting en Comparendos (HU #13712)

Comparendos pregunta a Renting quién tenía un vehículo en la fecha de la infracción para asignarle el conductor. La
llamada la hace core-consultas (RPC `ConsultarContratoConductor` de `FuentesComparendosService`); Comparendos decide
cuándo.

## Contrato

- APIM de Renting con certificado cliente (mTLS) y llave de suscripción en todas las llamadas. Mismo APIM, certificado
  y login que el canal de correo de Renting de core-notificaciones.
- Login: `POST {RENTING_API_LOGIN_PATH}` con `{ secretName, subject }` → `{ token, refresh }`.
- Consulta: `GET {RENTING_API_CONTRATOS_PATH}?plate={PLACA}&offenseDate={AAAA-MM-DD}T12:00:00.000Z` con Bearer.
- **Renting solo tiene PRODUCCIÓN.** Toda consulta devuelve personas reales.

## Hallazgo con la API real (2026-10-20)

Una consulta autorizada por Samuel (placa de Renting, solo lectura; se registraron los nombres de los campos, nunca
los valores). Login 200 con `{ refresh, token }`; la consulta respondió 200 con un arreglo de un contrato:

| Campo | Tipo | Uso en Comparendos |
|---|---|---|
| `plate` | texto | — |
| `rentType` | texto | — |
| `contractType` | texto | Tipo de contrato del conductor |
| `startDate`, `endDate` | texto | — (vigencia del contrato) |
| `endReturnDate` | nulo en la muestra | — |
| `names` | texto | Nombre del conductor |
| `firstSurname` | **vacío** en la muestra | Se agrega al nombre si viene |
| `customerEmail` | texto | Correo para los avisos |

**No trae tipo ni número de documento, ni teléfono.** La documentación que había (y V1) solo usaba nombre, apellido,
correo y teléfono.

Según Samuel, el cliente que devuelve el llamado es **quien manejaba el carro** (es lo que dice el servicio:
contraventor por placa y fecha). Que `firstSurname` venga vacío puede indicar que `names` trae el nombre completo; si
algún día resulta ser la empresa arrendataria y no la persona, los avisos le llegarían a ella: hay que revisarlo con
Renting.

### Decisión

El conductor se asigna con nombre y correo, origen API, y queda **«documento pendiente»**:

- Solo con origen API puede faltar el documento; a mano sigue siendo obligatorio.
- El operador lo completa en el detalle (`PUT /comparendos/{id}/contraventor/documento`). No es un reemplazo: no revoca
  los enlaces del portal ni reinicia los avisos. Queda en el historial y en la línea de tiempo.
- El cambio de contraventor ante el OT exige el documento (condición `DOCUMENTO_CONDUCTOR` de la viabilidad), además de
  la cédula y la licencia.
- Si Renting algún día devuelve el documento completo y válido, se usa.

## Quién la usa: dos interruptores del SuperAdmin

La API solo conoce la flota propia de Renting: no sirve para los vehículos de otro propietario aunque Renting los
administre (Bancolombia), ni para empresas sin relación con Renting (Mobilize).

1. **General, por empresa:** integración del contraventor «API de contratos de Renting» y «Consulta de contraventor»
   encendida. En Manual no sale ninguna consulta.
2. **Por NIT monitoreado:** casilla «Consulta por API», apagada por defecto.

Apagar el general no borra las casillas. Al cambiar cualquiera de los dos, los comparendos sin conductor se recalculan:
pendientes de consulta si aplica, asignación manual si no. Por placa u OCR decide el propietario del comparendo.

## Datos personales y correos de prueba

- Fuera de PDN el correo del conductor se reemplaza por `RENTING_API_CONTRATOS_DEVELOPMENT_EMAIL` (el buzón de pruebas,
  `samuel.cardenas@flitsas.com` en local y DEV) y el teléfono se omite. Solo con
  `RENTING_API_CONTRATOS_REAL_CONTACT_ENABLED=true` (PDN) pasa el contacto real.
- No hay respuesta cruda en el contrato gRPC ni datos personales en logs o mensajes de error.

## Configuración (core-consultas)

Las `RENTING_API_*` del canal de correo (`.env.renting.example`): `BASE_URL`, `KEY_NAME`, `KEY_VALUE`,
`PFX_CERTIFICATE_PATH`, `PASSPHRASE`, `LOGIN_PATH`, `LOGIN_SECRET_NAME`, `LOGIN_SUBJECT`, `SECONDS_TIMEOUT`,
`LOGIN_CACHE_SECONDS_TTL`; más `RENTING_API_CONTRATOS_PATH`, `RENTING_API_CONTRATOS_REAL_CONTACT_ENABLED` y
`RENTING_API_CONTRATOS_DEVELOPMENT_EMAIL`. Sin la configuración completa queda «no conectada»: en DEV Comparendos
sigue con la API simulada; fuera de DEV la consulta queda en error y nunca inventa un conductor.
