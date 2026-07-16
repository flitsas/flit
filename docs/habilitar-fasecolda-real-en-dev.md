# Habilitar el avalúo Fasecolda con datos reales en DEV

**Fecha:** 2026-07-15
**Feature:** #10707 — Avalúo comercial (ADR-0029)
**Síntoma:** `GET /api/v1/tramites/instances/{id}/commercial/suggested-value` devuelve `sugerido: null` con `status: "no_data"` y el mensaje *"Sin valor de referencia mock para el VIN"*.

---

## 1. Diagnóstico

**DEV está corriendo Fasecolda en modo `mock`, no `real`.** El mensaje que recibiste solo puede salir de la rama mock del proveedor:

```csharp
// FasecoldaAvaluoProvider.cs:39-45
if (ConsultationProviderModeOptions.IsMock(_modes.FasecoldaMode))
{
    var mock = await mockReader.GetValueAsync(vin!, SourceKey, ct);
    return mock is null
        ? AvaluoResult.NoData(SourceKey, "Sin valor de referencia mock para el VIN")   // ← tu respuesta
        : AvaluoResult.Ok(SourceKey, mock.Value);
}
```

Hay **dos causas encadenadas**, y la segunda es la de fondo:

### Causa 1 — El VIN no está sembrado en la tabla de mocks

En modo mock, el proveedor lee `tramites.avaluo_mock_values`. La migración `20260714180130_FEATURE10707_AvaluoMockSeed` solo siembra **dos VINs**:

| VIN | fasecolda | base_gravable | mercado_libre |
|-----|-----------|---------------|---------------|
| `93Y9SR333RJ563653` | 105.600.000 | 98.000.000 | 112.000.000 |
| `1FTFW1ET5DFC12345` | 119.900.000 | 110.000.000 | 125.000.000 |

Cualquier otro VIN → `null` → `no_data`. Por eso tu trámite no devuelve nada ni siquiera en mock.

### Causa 2 (la de fondo) — El contenedor nunca recibe `FASECOLDA_MODE`

El modo se resuelve así:

```csharp
// InfrastructureExtensions.cs:239
o.FasecoldaMode = Cfg("Consultations:FasecoldaMode", "FASECOLDA_MODE") ?? "mock";
// donde Cfg(key, env) => configuration[key] ?? Environment.GetEnvironmentVariable(env)
```

Y en el VPS:

- `appsettings.json` (el que va en la imagen) **no declara** `Consultations:FasecoldaMode`.
- **`docker-compose.prod.yml` no pasa ninguna variable `FASECOLDA_*`.** Cero. Pasa `VERIFIK_*`, `KYVERUM_*`, `INTEMPO_MODE`, `OCR_*`, `Smtp__*`… pero Fasecolda no existe en ese archivo.

Resultado: ambos lados devuelven `null` → `?? "mock"` → **mock**.

El único sitio del repo donde `FasecoldaMode` vale `"real"` es `appsettings.Development.json`, que está **gitignored** y por tanto no entra en la imagen Docker. Es tu máquina local, no el servidor.

> **Nota de alcance:** el mismo hueco afecta a RUES. `docker-compose.prod.yml` tampoco pasa `VERIFIK_RUES_MODE`, así que **la consulta RUES también corre en mock en DEV**. Ver `docs/reporte-consulta-rues-2026-07-15.md`.

---

## 2. Qué se necesita para probar con datos reales

Son **tres cosas**, y la primera es un cambio de código: hoy no hay forma de activar Fasecolda real en DEV solo tocando el `.env` del VPS.

### 2.1 Cambio de código — exponer las variables en `docker-compose.prod.yml`

Añadir al bloque `environment:` del servicio `core-api` (junto a los `VERIFIK_*`):

```yaml
      # Avalúo Fasecolda (Feature #10707). El código lee env vars CRUDAS
      # (FASECOLDA_*), no `Fasecolda__*`. Default mock ⇒ no rompe si el .env no lo define.
      FASECOLDA_MODE: ${FASECOLDA_MODE:-mock}
      FASECOLDA_API_USERNAME: ${FASECOLDA_API_USERNAME:-}
      FASECOLDA_API_PASSWORD: ${FASECOLDA_API_PASSWORD:-}
      FASECOLDA_BY_VIN_API_BASE_URL: ${FASECOLDA_BY_VIN_API_BASE_URL:-https://fasecoldaback.quantil.co}
      FASECOLDA_BY_VIN_API_PATH: ${FASECOLDA_BY_VIN_API_PATH:-/api/busquedaVin}
      FASECOLDA_API_BASE_URL: ${FASECOLDA_API_BASE_URL:-https://guiadevalores.fasecolda.com/apifasecolda}
      FASECOLDA_AUTH_API_PATH: ${FASECOLDA_AUTH_API_PATH:-/token}
      FASECOLDA_LIST_CODE_API_PATH: ${FASECOLDA_LIST_CODE_API_PATH:-/api/listacodigosid/consultabycodigo}
      FASECOLDA_API_GRANT_TYPE: ${FASECOLDA_API_GRANT_TYPE:-password}
      FASECOLDA_API_SECONDS_TIMEOUT: ${FASECOLDA_API_SECONDS_TIMEOUT:-55}
```

Solo `FASECOLDA_MODE`, `FASECOLDA_API_USERNAME` y `FASECOLDA_API_PASSWORD` son imprescindibles; el resto ya tiene default correcto en `FasecoldaOptions.cs`. Incluirlos igual hace el deploy determinista y explícito, que es la convención del archivo.

Aprovechar y añadir también, para cerrar el hueco de RUES:

```yaml
      VERIFIK_RUES_MODE: ${VERIFIK_RUES_MODE:-mock}
```

### 2.2 Variables en el `.env` del VPS

En el `.env` que consume `docker-compose.prod.yml` en el servidor:

```bash
FASECOLDA_MODE=real
FASECOLDA_API_USERNAME=<usuario Fasecolda>
FASECOLDA_API_PASSWORD=<password Fasecolda>
```

Las credenciales **no van al repo**. Ver §5.

### 2.3 Datos del trámite

El camino real exige dos campos, y uno de ellos es **bloqueante**:

| Campo | Obligatorio | Efecto si falta |
|-------|-------------|-----------------|
| `vin` | **Sí** | `no_data`: *"El vehículo no tiene VIN para consultar Fasecolda"* |
| `vehicle_year` | **Sí** | `SelectValue` devuelve `null` → *"No hay valor para el año/atributos del vehículo"* |
| `vehicle_engine_displacement` | No | Filtro laxo (solo aplica si ambos lados lo tienen) |
| `vehicle_fuel` | No | Filtro laxo |
| `vehicle_passengers` | No | Filtro laxo |

`vehicle_year` sale de la consulta RUNT del vehículo. **Si el trámite no tiene el año, Fasecolda real devolverá `no_data` aunque las credenciales estén bien.** Verificarlo antes de culpar a la integración.

---

## 3. Cómo funciona el camino real (para saber dónde mirar si falla)

Tres llamadas HTTP, en dos hosts distintos:

```
1) GET  https://fasecoldaback.quantil.co/api/busquedaVin/{VIN}      (sin auth)
        → { codigos: [...] }
        404/400 → "VIN sin coincidencia en Fasecolda"
        lista vacía → "VIN sin códigos en Fasecolda"

2) POST https://guiadevalores.fasecolda.com/apifasecolda/token      (form-urlencoded)
        grant_type=password & username=... & password=...
        → { access_token, expires_in ≈ 86399 }
        falla → "No fue posible autenticar contra Fasecolda"
        (token cacheado en memoria ~24h, se renueva 60s antes de expirar)

3) GET  https://guiadevalores.fasecolda.com/apifasecolda/api/listacodigosid/consultabycodigo/{cod1,cod2,...}
        Authorization: Bearer {token}
        → [ { cilindraje, combustible, capacidadPasajeros, valorModelo: [{ modelo, valor }] } ]
        vacío → "Sin fichas técnicas para el VIN"
```

**Selección del valor** (`SelectValue`): filtra las fichas por atributos del vehículo, toma el **menor** `valor` cuyo `modelo` == `vehicle_year`, y multiplica **×1000** (Fasecolda entrega miles de COP). Si el filtro estricto no deja nada, cae a un fallback que busca el año entre todas las fichas del VIN.

**Egress:** el VPS debe alcanzar `fasecoldaback.quantil.co` y `guiadevalores.fasecolda.com`. Timeout 55s (el `/token` de Fasecolda es lento).

### Mapa de mensajes → causa

| `message` en la respuesta | Modo | Causa |
|---|---|---|
| "Sin valor de referencia mock para el VIN" | **mock** | El VIN no está en `avaluo_mock_values` |
| "El vehículo no tiene VIN para consultar Fasecolda" | ambos | El trámite no tiene `vin` |
| "VIN sin coincidencia en Fasecolda" | real | Fasecolda respondió 400/404 al VIN |
| "VIN sin códigos en Fasecolda" | real | VIN válido, sin códigos asociados |
| "No fue posible autenticar contra Fasecolda" | real | Credenciales malas / `/token` inalcanzable |
| "Sin fichas técnicas para el VIN" | real | `consultabycodigo` devolvió lista vacía |
| "No hay valor para el año/atributos del vehículo" | real | Falta `vehicle_year` o el catálogo no tiene ese año |
| `status: "error"` (sin message) | real | Excepción de red/JSON o no-200 |

Que el `message` diga *"mock"* es el indicador inequívoco de que el modo no se activó.

---

## 4. Pasos para probar

### Ruta A — Verificar el flujo YA, sin credenciales (mock)

Sirve para confirmar que el endpoint, la política por tenant y la UI funcionan:

1. Crear un trámite con VIN **`93Y9SR333RJ563653`**.
2. `GET .../commercial/suggested-value` → debe devolver `sugerido: 105600000`, `fuentePrincipal: "fasecolda"`.

Si esto responde bien, la integración está sana y lo único pendiente es el modo real.

### Ruta B — Datos reales

1. **PR** con el cambio de `docker-compose.prod.yml` (§2.1) → merge a `develop`.
2. Añadir `FASECOLDA_MODE=real` + usuario/password al `.env` del VPS.
3. Redesplegar (push a `develop` dispara el CD; ojo: el último CD falló por **timeout SSH**, ver §6).
4. `docker compose up -d core-api` y confirmar que la variable llegó:
   ```bash
   docker compose exec core-api printenv | grep FASECOLDA
   ```
5. Probar con un VIN real que tenga `vehicle_year` poblado desde RUNT.
6. Confirmar por el `message`: en real ya **no** debe aparecer la palabra "mock".

---

## 5. Advertencia de seguridad — no metas las credenciales en `appsettings.json`

Ahora mismo hay un cambio local **sin commitear** en `services/core-api/src/Flit.Api/appsettings.json` que añade una sección `Fasecolda` con `Username` y `Password` en claro, y además credenciales SMTP (`Smtp:DefaultSenderEmail` / `Smtp:DefaultSenderPassword`) también en claro.

**Ese archivo SÍ está trackeado por git** (a diferencia de `appsettings.Development.json`, que está en `.gitignore`). Si se commitea, los secretos quedan en el historial. El propio `FasecoldaOptions.cs` lo advierte: *"Credenciales por User Secrets / env — NUNCA en el repo"*.

Hay además una **trampa funcional**, no solo de seguridad. La precedencia es:

```csharp
Cfg(key, env) => configuration[key] ?? Environment.GetEnvironmentVariable(env)
//                ↑ appsettings.json GANA        ↑ solo si lo anterior es null
```

Si esa sección `Fasecolda` llega a la imagen, **los valores del `appsettings.json` pisan a las variables del `.env` del VPS** y no habría forma de cambiar credenciales sin recompilar. Es el orden contrario al que uno esperaría.

**Qué hacer:**
- Local: usar `dotnet user-secrets` o `appsettings.Development.json` (gitignored).
- VPS: `.env` + `docker-compose.prod.yml`.
- Revertir el cambio de `appsettings.json`: `git checkout -- services/core-api/src/Flit.Api/appsettings.json`.
- Rotar las credenciales que hayan estado expuestas.

---

## 6. Riesgos y notas de estado

- **El CD a DEV viene fallando por timeout SSH al VPS** (run `#29351648047`, PR #159). Aunque se mergee el cambio de compose, puede no llegar al servidor. Verificar el deploy antes de dar por fallida la integración.
- **Sin caché ni reintentos.** Solo se cachea el token OAuth2 (~24h, singleton en memoria). Las consultas de VIN y códigos pegan a Fasecolda en cada llamada. Un reinicio del contenedor tira el token.
- **El seed de mocks está activo en DEV/QA**: `IsDevSeedEnabled()` es true con `ASPNETCORE_ENVIRONMENT` en `Development` o `QA` (el compose fija `Development`), o con `FLIT_DEV_SEED=1`. En producción la tabla arranca vacía.
- **Al pasar a real, los mocks dejan de usarse**, pero la tabla sigue sembrada. No hay conflicto: el modo decide la rama antes de leerla.
- El proveedor **nunca lanza excepciones** al handler: los fallos se degradan a `status: "error"` o `no_data`. Un avalúo caído no tumba el paso comercial.

---

## 7. Resumen

| Pregunta | Respuesta |
|---|---|
| ¿Por qué `null`? | DEV corre en **mock** y tu VIN no está sembrado |
| ¿Por qué está en mock? | `docker-compose.prod.yml` **no pasa `FASECOLDA_MODE`** → default `mock` |
| ¿Basta con tocar el `.env` del VPS? | **No.** Primero hay que exponer las vars en el compose (cambio de código → PR) |
| ¿Qué credenciales? | `FASECOLDA_API_USERNAME` + `FASECOLDA_API_PASSWORD` en el `.env` del VPS |
| ¿Qué más necesita el trámite? | `vin` **y** `vehicle_year` (ambos bloqueantes) |
| ¿Cómo confirmo que quedó en real? | El `message` deja de mencionar "mock"; `printenv \| grep FASECOLDA` en el contenedor |
| ¿Puedo probar ya sin credenciales? | Sí: VIN `93Y9SR333RJ563653` → 105.600.000 |
