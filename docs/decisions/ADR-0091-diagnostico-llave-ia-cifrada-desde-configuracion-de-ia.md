# ADR-0091: Diagnóstico toma proveedor, modelo y llave del redactor desde «Configuración de IA» de core-api, con la llave cifrada para Diagnóstico

**Fecha**: 2026-11-18
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT. La aceptación es exclusiva del Líder Técnico humano, en su propio PR (regla FLIT 15).
**Épica / Feature / HU**: épica #12678 «[5] Motor de IA y dictamen»; Feature y HUs en registro diferido (`.claude/state/pending-work-items/redactor-ia-diagnostico.md`, D9).
**Tags**: arquitectura, backend, seguridad, criptografía, grpc, llm, diagnostico, modulo-admin-ia, ley-1581
**Supersedes**: ninguno.
**Enmienda**: ADR-0070 de core-api, «Multi-proveedor de IA por herramienta» (`services/core-api/docs/adr/ADR-0070-multi-proveedor-ia-por-herramienta.md`, Propuesto). Hasta ahora las llaves no salían de core-api. Desde este ADR, core-api puede entregar la llave de **una herramienta propia de un servicio consumidor** a ese servicio, cifrada para él. Las herramientas `ocr` y `dr_flit` nunca se entregan por este canal; pero como `diagnostico_dictamen` reutiliza la `ANTHROPIC_API_KEY` general (P2=b), el valor de esa llave sí sale cifrado hacia Diagnóstico (ver «Riesgos aceptados»).
**Contrato**: `contracts/proto/flit/platform/v1/configuracion_ia.proto` (servicio y mensajes), el vector de prueba `contracts/proto/flit/platform/v1/testdata/configuracion_ia_vector.json` (AAD y sobre byte a byte) y la tabla «Códigos de error del rpc» de este ADR.

## Contexto

core-diagnostico redacta con un LLM los alertamientos de un dictamen que ya decidieron las reglas (ADR-0077). Usa su propio cliente Anthropic y su propia llave (`Diagnostico:Anthropic:ApiKey`). El proveedor se elige al arrancar. El usuario decidió (D9) que el proveedor, el modelo y la llave salgan del módulo «Configuración de IA» de core-api (Feature #13508, ADR-0070), donde el SuperAdmin ya administra las herramientas de IA. También decidió que core-api **entregue la llave cifrada** y que **Diagnóstico llame él mismo** al proveedor. Se descarta que core-api haga de proxy de la llamada.

Restricciones:
- El canal Diagnóstico→core-api es gRPC **h2c sin TLS** dentro de la red de compose (`http://core-api:8082`).
- Las llaves de core-api viven solo en el entorno de ejecución (ADR-0070).
- Lo que se envía al modelo (placa, marca, línea, modelo, servicio) es dato personal asociable al propietario (Ley 1581). Por eso importa qué proveedores pueden recibirlo.
- Un fallo pasajero de core-api no puede dejar un dictamen sin redacción para siempre: «sin proveedor» saca el dictamen de la cola.

## Decisión

Se crea el rpc `flit.platform.v1.ConfiguracionIaService/ObtenerConfiguracionIa`, servido por core-api. Usa la audiencia `tramites` y el scope `platform.ia.diagnostico`, y solo lo puede llamar el cliente `svc-diagnostico`, para la herramienta nueva `diagnostico_dictamen`. Devuelve:
- la cola de proveedores (las mismas reglas que `AiProviderQueue`: habilitado, capaz y con llave);
- el modelo de cada proveedor;
- `row_version` y un TTL sugerido;
- la llave de cada proveedor en un **sobre híbrido RSA-OAEP-256 + AES-256-GCM**, cifrado con la clave pública de Diagnóstico (`kid`). El AAD une audiencia, herramienta, proveedor, modelo, `row_version`, `kid`, un nonce que envía el cliente y la hora de emisión.

Diagnóstico descifra con su clave privada y guarda la llave solo en memoria, en una caché con TTL. Si no hay configuración, usa la plantilla (fallo cerrado). En v1, `diagnostico_dictamen` solo admite `anthropic`.

## Alternativas consideradas

### Opción 1: Sobre híbrido RSA-OAEP-256 + AES-256-GCM con la clave pública de Diagnóstico (elegida)

**Pros:**
- core-api solo guarda la clave pública. Ni la configuración de core-api, ni una captura del tráfico h2c, ni el token de `svc-diagnostico` bastan para obtener la llave.
- Usa primitivas que ya trae .NET (`RSA`, `AesGcm`). No hay dependencia nueva. Es la composición estándar de JWE, así que es fácil de revisar.
- La rotación la hace un solo lado, con `kid` (actual y siguiente).
- El AAD y el nonce atan cada sobre a una sola solicitud y a un solo contexto. Un sobre reenviado o con algún campo cambiado no se descifra.

**Cons:**
- Hay que gestionar un par de claves por ambiente (DEV≠QA≠PDN).
- RSA no da *forward secrecy*. Se acepta porque la privada solo vive en Diagnóstico.
- El sobre pesa unos 420 B por proveedor. No importa con una llamada cada 5 minutos.

**Esfuerzo:** S
**Riesgos:** un error en el AAD o en el padding. Se mitiga con un test de vector fijo y con tests que manipulan cada campo.

### Opción 2: AES-256-GCM con una clave simétrica compartida por ambiente

**Pros:**
- Es lo más simple: una sola primitiva.
- El sobre es mínimo.
- Es rápido.

**Cons:**
- El mismo secreto vive en dos servicios, y cualquiera de los dos puede fabricar sobres.
- La rotación hay que coordinarla en ambos a la vez.
- Quien obtenga el entorno de core-api y capture el tráfico tiene la llave.

**Esfuerzo:** S
**Riesgos:** si se filtra el secreto compartido, quedan comprometidos los dos lados.

### Opción 3: ECIES sobre P-256 (ECDH efímero + HKDF-SHA256 + AES-256-GCM)

**Pros:**
- Claves y sobre pequeños.
- *Forward secrecy* frente a la clave del emisor.
- También usa primitivas de .NET.

**Cons:**
- Hay más código criptográfico propio (KDF, codificación de puntos, salt e info), y con él más superficie de error.
- X25519, la variante sugerida, no viene en .NET: exigiría NSec o BouncyCastle, una dependencia nueva.

**Esfuerzo:** M
**Riesgos:** una implementación casera de ECIES.

### Descartadas por el usuario (D9)

- core-api ejecuta la llamada al LLM (proxy).
- La llave viaja en claro y se confía solo en TLS o mTLS del canal, que hoy es h2c.

## Tradeoff aceptado

Se acepta gestionar un par RSA por ambiente a cambio de lo siguiente: el único secreto capaz de descifrar la llave vive en un solo proceso, y core-api no guarda ningún secreto nuevo. La opción 2 duplicaría el secreto en dos servicios. La opción 3 ahorra unos bytes que no importan y añade código criptográfico propio, o una dependencia.

Limitar `diagnostico_dictamen` a `anthropic` cuesta resiliencia, porque no hay failover. A cambio, no se transfieren datos personales a proveedores sin región declarada (Kimi, DeepSeek). Tampoco hace falta un cliente OpenAI-compatible en Diagnóstico mientras OpenAI no esté registrado como encargado.

## Consecuencias

### Lo que se gana
- Una sola consola (Configuración de IA) gobierna proveedor y modelo también para Diagnóstico. Activar o desactivar no exige redespliegue: el cambio se ve al vencer el TTL (≤ 5 min).
- La llave de Diagnóstico deja de estar en el contenedor de Diagnóstico. En reposo solo está en el entorno de core-api.
- En los servidores, el modo `plataforma` nunca usa el simulado. Esto cierra SP-5.

### Lo que se pierde
- El redactor depende de core-api. Una caída de core-api se trata como **fallo técnico**: el dictamen se reintenta en la cola y no sale de ella. Si dura más que los reintentos, queda con texto estándar.
- Hay un par de claves más que operar y rotar por ambiente.
- No hay failover entre proveedores para esta herramienta en v1.

### Cambios operacionales
- core-api: la herramienta usa la **misma `ANTHROPIC_API_KEY` general** de OCR y DR. FLIT (decisión del usuario P2=b, 2026-11-18). No existe `DIAGNOSTICO_ANTHROPIC_API_KEY`. Se añaden `Ai__Entrega__Diagnostico__ClavePublica`/`__Kid` (y las `Siguiente`) y el modelo en `Ai:Herramientas:diagnostico_dictamen:Modelos:anthropic`, que por defecto es `claude-haiku-4-5`.
- core-diagnostico: `Diagnostico__Ia__Origen=plataforma`, `Diagnostico__Ia__ClavePrivada` (variable de entorno del `.env` del ambiente, igual que `ANTHROPIC_API_KEY` del OCR; decisión del usuario 2026-11-19), `__Kid`, `__ClavePrivadaAnterior` y `__KidAnterior` (rotación). `ClavePrivadaArchivo`/`ClavePrivadaAnteriorArchivo` se conservan para local y pruebas; si la variable tiene valor, gana. `Diagnostico__Ia__ModelosPermitidos` (SP-14) y `Diagnostico__Ia__MaxLlamadasPorMinuto` (SP-17) quedan con los defectos de la imagen. `Diagnostico__Anthropic__ApiKey` sale del compose y queda solo para `Origen=local`. Sin privada, el servicio arranca y el dictamen sale con la plantilla.

### Riesgos aceptados (decisión del usuario, 2026-11-18)
- **Llave general compartida (SP-17).** Con P2=b, la llave que sale cifrada hacia Diagnóstico es la misma que usan OCR y DR. FLIT para todos los tenants. Si core-diagnostico o su clave privada se comprometen, un atacante puede agotar la cuota y dejar sin servicio a OCR y DR. FLIT, o generar gasto a cuenta de FLIT; revocarla obliga a cortar los tres usos a la vez. Mitigaciones: tope local de llamadas por minuto en Diagnóstico, límite de gasto con alerta en el workspace de Anthropic y runbook de revocación (estas dos últimas, a cargo del Líder Técnico). Una llave propia de Diagnóstico sigue siendo la salida si el riesgo deja de ser aceptable: es un cambio de configuración en core-api.
- **Privada por variable de entorno (SP-16).** Por paridad con la `ANTHROPIC_API_KEY` de core-api (decisión del usuario, 2026-11-19), la privada de Diagnóstico entra por el `.env` y no como secreto de archivo. Queda visible para quien pueda hacer `docker inspect` o leer el `.env` del VPS, el mismo nivel de exposición que ya tiene la llave de Anthropic. Mitigación: nunca se registra ni se imprime; rotación por `kid`. Montarla como secreto de archivo sigue disponible sin cambio de código (`ClavePrivadaArchivo`).
- **Atacante activo en la red interna (SP-14=a).** El canal `http://core-api:8082` va sin TLS y la clave pública de Diagnóstico no es secreta. El sobre protege la confidencialidad de la llave, pero no autentica a core-api: un atacante dentro de la red de contenedores podría entregar un sobre con su propia llave y modelo, o forzar `disponible=false`. Se acepta porque exige acceso a la red interna; Diagnóstico solo acepta modelos de `ModelosPermitidos`. Firmar la respuesta (ES256) o TLS interno quedan como evolución.
- BD: DDL `203-ai-tool-diagnostico-dictamen.sql` y su migración EF. Amplía los tres CHECK de `tool_code` y siembra la fila de la herramienta.
- OpenAPI de core-api: el enum de `toolCode` incluye `diagnostico_dictamen` (sube el patch).
- Identity: el scope `platform.ia.diagnostico` se añade al cliente `svc-diagnostico`.

### Códigos de error del rpc

| gRPC | reason (ErrorInfo) | Cuándo | Diagnóstico hace |
|---|---|---|---|
| UNAUTHENTICATED | — | token ausente o inválido | fallo técnico reintentable (espera 120 s) + log Error |
| PERMISSION_DENIED | `CLIENTE_NO_PERMITIDO` | `sub` distinto de svc-diagnostico o sin scope (la policy lo audita como `POLICY_RECHAZADA`) | sin configurar + log Error |
| PERMISSION_DENIED | `HERRAMIENTA_NO_PERMITIDA` | herramienta fuera de su lista (`ocr`, `dr_flit`…) | sin configurar |
| INVALID_ARGUMENT | `TOOL_CODE_REQUERIDO` · `NONCE_INVALIDO` · `KID_INVALIDO` | validación de la petición | sin configurar |
| FAILED_PRECONDITION | `CLAVE_DESTINATARIO_DESCONOCIDA` | `kid` no registrado en core-api | reintenta una vez con `KidAnterior`; si no, sin configurar |
| UNAVAILABLE / DEADLINE_EXCEEDED / INTERNAL | — | core-api reiniciando, BD caída | fallo técnico reintentable (espera 120 s); nunca sin configurar |

`disponible=false` (sin proveedor con llave, `SIN_PROVEEDOR_CON_LLAVE`) no es error: respuesta OK que Diagnóstico guarda 60 s.

## ADRs relacionados

- [ADR-0070] de core-api (multi-proveedor de IA por herramienta) — este ADR lo enmienda.
- [ADR-0070] de `docs/decisions` (gRPC entre servicios, REST hacia afuera): el canal nuevo es gRPC de servicio.
- [ADR-0089] — el mismo patrón de rpc de core-api atendido solo por `svc-diagnostico` (aud `tramites`, scope dedicado y policy por `sub`).
- [ADR-0077] — el dictamen lo deciden las reglas y el LLM solo redacta. No cambia.
- [ADR-0058] — tabla global `admin` sin tenant.

## Notas para agentes

- **Backend Agent (core-api)**: la lectura de la llave en claro queda `internal` en Infrastructure. Prohibido loguear la llave, el sobre o cualquier hash de la llave. El rpc no escribe transiciones. Solo `svc-diagnostico` puede pedir `diagnostico_dictamen`.
- **Backend Agent (core-diagnostico)**: no tocar `RedactorLlm.cs:24-37` ni `PromptRedactor.Construir` (CF6/SP-3). `BaseUrl` nunca viene de core-api. `UNAVAILABLE` nunca se convierte en «no configurado». Poner a cero los buffers de CEK y de texto en claro.
- **Database Agent**: CHECK con `DROP … IF EXISTS` + `ADD`, semilla idempotente, migración EF espejo.
- **Frontend Agent**: solo etiqueta, orden y tipo de la herramienta. «No aplica» ya existe.
- **QA Agent**: probar ida y vuelta del sobre, manipulación del AAD, nonce y reloj, cliente o herramienta no permitidos, `kid` desconocido, falta de llave (`disponible=false`), core-api caído (reintento) y un smoke DEV sin simulado.
- **Security Agent**: revisar OAEP con SHA-256, IV único, ZeroMemory, ausencia de la llave en logs y respuestas, y gitleaks del diff. El gate de Habeas Data para encender sigue igual.
- **Infra Agent**: un par RSA-3072 por ambiente. La privada va solo en el host de core-diagnostico; la pública y la llave Anthropic, solo en el de core-api. Seguir el procedimiento de rotación en 3 pasos del contrato §2.6.

## Referencias externas

- RFC 7516 (JWE) y RFC 7518 §4.3 (RSA-OAEP-256) y §5.3 (A256GCM).
- Ley 1581 de 2012, art. 26; Decreto 1377 de 2013, art. 24-25 (transmisión internacional a encargados).
