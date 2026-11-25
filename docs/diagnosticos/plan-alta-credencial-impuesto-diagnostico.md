# Plan: alta idempotente de la credencial del proveedor de impuesto y webhook en Diagnóstico

> Generado: 2026-11-25 · Estado: decisiones D1–D5 cerradas por el usuario · Rama `feature/AB-14694-producto-impuestos` (va en el PR #1053, D4)
> Contrato del proveedor: [guia-integracion-clientes.md](../integraciones/guia-integracion-clientes.md) §3 (alta) y §6 (webhook)

## 1. Qué cambia

**Hoy:**
- Un SuperAdmin pega a mano la `api_key` del proveedor en el admin de compañías, en `frontend/components/admin/companies/tabs/CredencialImpuestoDiagnostico.tsx`, que hace un `PUT .../products/diagnostico/credentials/impuesto_api_key`.
- La llave se guarda cifrada en `platform.tenant_product_credentials`, una tabla clave/valor con `ITenantProductSecretProtector`.
- La consume **core-consultas**. `FuenteCredencialImpuestoPlataforma` la pide por gRPC a core-identity (`CredencialesProductoService.ObtenerCredencialImpuesto`), y `ProveedorImpuesto` la relee ante un 401 si cambió la versión.
- El resultado se obtiene solo por **polling**. No hay `webhook_secret`.

**Después:**
- La plataforma obtiene la `api_key` y el `webhook_secret` con el alta idempotente, igual que core-impuestos.
- core-consultas manda un `callbackUrl` y recibe el aviso firmado, que cierra la consulta. El polling queda como respaldo.
- Si la llave rota, Impuestos se entera.
- La captura manual queda como respaldo.

## 2. Decisiones del usuario (2026-11-25)

| Id | Decisión |
|---|---|
| D1 | El alta vive en **core-api, `Flit.Modules.Platform`** |
| D2 | **Botón «Obtener del proveedor» + alta automática** por outbox al activar el producto `diagnostico`, con reintentos |
| D3 | **Receptor completo** del webhook |
| D3b | El receptor va en **core-consultas**; el **polling queda como conciliación** si el aviso no llega |
| D4 | **Misma rama y mismo PR #1053** |
| D5 | **HU FULLSTACK nueva**, hija de la Feature #14693, creada y activada con el sí del usuario antes de implementar |

## 3. Diseño

### 3.1 Alta (core-api)

1. **Disparadores:** el botón del admin (SuperAdmin) o la activación de `diagnostico` para un tenant. La activación encola un trabajo en la outbox del SDK, con espera escalonada y tope de intentos, igual que `TrabajadorReintentoAlta`. El estado se reporta en `tenant_product_provisioning(product_code='diagnostico')`.
2. **Datos del tenant:** `ObtenerCredencialImpuestoDiagnosticoHandler` lee el NIT base, el DV, la razón social y el correo. Con el NIT ausente o ambiguo responde un error funcional, con los mismos motivos que Impuestos.
3. **Llamada al proveedor:** `ClienteAltaProveedorImpuesto` usa un `HttpClient` tipado.
   - El token sale de `Platform:ImpuestoProveedor:IntegrationToken`.
   - Los errores 400, 401, 404 y 415 se mapean a códigos propios.
   - Ante un 5xx o un timeout reintenta una vez, sin riesgo porque el alta es idempotente.
   - Los DTOs se copian del contrato, sin dependencia entre servicios.
4. **Una sola transacción:**
   - upsert de `impuesto_api_key` y de `impuesto_webhook_secret`;
   - si el secreto cambió, el anterior pasa a `impuesto_webhook_secret_anterior` y vence a las 24 h;
   - `row_version` sube;
   - auditoría `CREDENCIAL_OBTENIDA` o `CLAVE_ROTADA`, sin valores;
   - si la llave cambió, el evento `platform.credencial_impuesto.actualizada {tenantId, version}` sale por la outbox.
5. **Lista blanca:** se agregan los dos códigos nuevos a `TenantProductCredentialCodes`. La tabla es clave/valor, así que no hace falta DDL; falta verificar que no tenga un CHECK sobre `credential_code`, lo que confirmará db-schema-validator.

### 3.2 Lectura del secreto (core-identity)

- **rpc nuevo** `CredencialesProductoService.ObtenerSecretoWebhookImpuesto({tenantId}) → {actual, anterior?, anterior_vence_en?, version}`, con un scope propio para core-consultas.
- **Contrato gRPC:** se le sube la versión menor.

### 3.3 Webhook (core-consultas)

- **Envío:** al pedir la consulta se manda `callbackUrl = {Consultas:Impuesto:AvisoBaseUrl}/api/v1/impuesto/avisos/{token}`.
  - El `token` es opaco por tenant: un HMAC del tenantId con una llave del servicio, igual que `GeneradorTokenAviso` de Impuestos, así que no necesita BD.
  - Si el tenant no tiene secreto, se omite el `callbackUrl` y sigue el polling. Así se evita el `422 webhook_not_configured`.
- **Receptor:** `POST /api/v1/impuesto/avisos/{token}`, siguiendo el modelo M10 de Impuestos.
  1. Resuelve el tenant desde el token; si no lo reconoce, responde `401`.
  2. Lee el cuerpo crudo.
  3. Verifica `v1=HMAC-SHA256(secreto, "{ts}.{raw}")` en tiempo constante, con el secreto actual y, si falla, con el anterior mientras no venza. El timestamp tolera ±300 s.
  4. Deduplica con `INSERT … ON CONFLICT DO NOTHING` en una tabla nueva de dedupe en el esquema de consultas: `delivery_id` y `received_at`, con purga.
  5. Comprueba que `consulta.id` pertenece a ese tenant y se queda con el **mayor `attempt`**.
  6. Cierra la consulta por **el mismo camino del resultado del polling**, que publica a Diagnóstico por el bus, de modo que el cierre es idempotente.
  7. Responde `200` de inmediato.
- **Límites:** por IP real y global, con los mismos valores de Impuestos.
- **Secreto en memoria:** se cachea por versión y se relee si la firma falla con la versión vieja.
- **Polling de respaldo:** si la consulta ya está cerrada por el aviso, se descarta; si no, conserva el intervalo actual. Una consulta no se cierra dos veces.
- **Exposición:** la ruta pública pasa por YARP o nginx. Hereda el pendiente G-6, porque la IP real detrás de YARP está sin resolver.

### 3.4 Propagación a Impuestos (core-impuestos)

- **Consumidor:** escucha `platform.credencial_impuesto.actualizada`. Si su llave salió de `CopiaDiagnostico` o tiene otro prefijo, la recopia por el rpc existente `ObtenerCredencialImpuestoParaCopia`.
- **Contrato:** `platform.v1` pasa a la versión menor siguiente.

### 3.5 Frontend (`frontend`, admin de compañías)

- **Pantalla:** `CredencialImpuestoDiagnostico.tsx` lleva el botón «Obtener del proveedor» con los 4 estados (vacío, cargando, error, lleno).
  - Muestra el prefijo de la llave, «Secreto de webhook configurado» y el estado de aprovisionamiento.
  - Si la llave rotó, muestra un aviso.
  - La captura manual queda plegada como «Ingresar manualmente».
- **Cliente:** `frontend/lib/api/platform.ts`.

### 3.6 Seguridad (no negociable)

- **Nunca en logs, respuestas ni UI:** la api_key, el webhook_secret, el token del alta, la firma ni el token del aviso.
  - Se usa `[LoggerMessage]` sin parámetros sensibles.
  - El endpoint responde solo metadatos.
- **Endpoint del alta:** solo SuperAdmin, con rate limit por tenant. El cuerpo lo arma el servidor, así que el cliente no manda el NIT.
- **Receptor:** no confía en el cuerpo antes de verificar la firma. Responde 401 sin detalle.

## 4. Módulos y carriles (una oleada, contrato fijado en el encargo)

| Id | Carril | Contenido |
|---|---|---|
| **P1** | be1 | core-api: códigos nuevos, cliente del alta, handler, endpoint `POST /api/v1/platform/admin/tenants/{tenantId}/products/diagnostico/credentials/impuesto/obtener`, `ListTenantProducts` con `ImpuestoWebhookSecretConfigurado`, auditoría y evento por la outbox |
| **P2** | be1 | core-api: disparo automático al activar `diagnostico`, con reintentos y estado en `tenant_product_provisioning` |
| **P3** | be1 | core-identity: rpc `ObtenerSecretoWebhookImpuesto` + scope + contrato |
| **P4** | be2 | core-consultas: `callbackUrl` + token, receptor, verificación, dedupe (migración), cierre idempotente compartido con el polling, límites, lectura del secreto por gRPC con caché |
| **P5** | be2 (después de P4) | core-impuestos: consumidor de `platform.credencial_impuesto.actualizada` |
| **P6** | fe1 | frontend: tab de credencial + test |
| **P7** | orquestador | `appsettings` y variables (`Platform:ImpuestoProveedor:*`, `Consultas:Impuesto:AvisoBaseUrl`, llave del token), script local apuntando al stub del proveedor (el alta y el envío de avisos ya existen en `scripts/dev/stub-impuestos`), guía de despliegue, guía del entorno local |

Concurrencia: 2 carriles .NET (be1, be2) y 1 FE (fe1).

## 5. Tests (lista cerrada)

- **core-api:**
  - `ObtenerCredencialImpuestoDiagnosticoHandlerTests` (nuevo): 201 y 200, `rotated`, secreto anterior, NIT ausente o ambiguo, 401/404/5xx, atomicidad y logs sin valores;
  - `ClienteAltaProveedorImpuestoTests` (nuevo);
  - tests del disparo automático (nuevo);
  - `ListTenantProductsHandlerTests`, `SetTenantProductCredentialHandlerTests`, `UpdateTenantProductCredentialFromServiceHandlerTests`.
- **core-identity:** tests del rpc nuevo y del scope.
- **core-consultas:**
  - nuevos: receptor, firma, ventana del secreto anterior, dedupe, mayor attempt, tenant ajeno, cierre aviso/polling sin duplicar y límites;
  - los existentes de `ProveedorImpuesto` y `FuenteCredencialImpuestoPlataforma`.
- **core-impuestos:** el consumidor nuevo, `EvaluadorCredencialesExistentes` y `AltaIdempotenteTests`.
- **frontend:** `CredencialImpuestoDiagnostico.test.tsx` y `ProductosTab.test.tsx`.
- **E2E local**, contra el stub, con el entorno verificado antes de la primera corrida:
  1. se obtiene la credencial desde el admin;
  2. se ve el prefijo;
  3. una consulta de impuesto en Diagnóstico se cierra por aviso;
  4. otra se cierra por polling cuando el stub no avisa.
- **Regresión completa:** queda en el CI del PR #1053.

Revisión: code review, seguridad y db-schema-validator **en paralelo** al cerrar los carriles, con una sola tanda de correcciones y una re-revisión del delta.

## 6. Estimado

- **Tamaño:** entre 1.500 y 2.000 líneas, que se suman al PR #1053 por la decisión D4.
- **Tiempo:** entre 4 y 5 horas hasta el push, con 2 carriles .NET y 1 FE en paralelo.

## 7. Riesgos

- **Rotación cruzada:** el alta es por NIT, así que los dos productos comparten la llave. Si una sola alta la rota (`api_key_rotated`, que pasa una vez por empresa), el otro producto recibe 401 hasta que P5 la propague. core-consultas relee la llave ante un 401.
- **Tenants sin NIT válido:** no pueden hacer el alta. Siguen con la captura manual y con polling.
- **Despliegue:** antes de activar el botón y el `callbackUrl` hay que configurar en flit-gitops el token del alta, la URL pública del aviso y la llave del token. Sin token, el endpoint responde `503 PROVEEDOR_NO_CONFIGURADO`. Sin `AvisoBaseUrl`, no se manda `callbackUrl` y sigue el polling.
- **G-6:** la IP real detrás de YARP es la misma limitación que ya tiene Impuestos.
