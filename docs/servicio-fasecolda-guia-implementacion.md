# Servicio Fasecolda — Guía de implementación

> Fuente analizada: `D:\FLIT\BackApiConnectionRunt` (Node.js + TypeScript + Express + Inversify + Axios, arquitectura hexagonal).
> Objetivo: replicar el servicio Fasecolda en otro proyecto (p. ej. `services/core-api` .NET de FLIT 2.0).
> Fecha: 2026-07-14.
> **Respuestas validadas contra la API en vivo el 2026-07-14** con VIN de prueba `1FTFW1ET5DFC12345` (Ford F150 2014). Los ejemplos JSON de este documento son respuestas reales, no inventadas.

---

## 1. Qué hace el servicio

Fasecolda expone la **guía de valores de vehículos** de Colombia. El servicio del proyecto de referencia consulta esa guía a partir del **VIN** de un vehículo y devuelve las fichas técnicas + valores comerciales asociados.

Internamente son **3 llamadas a 2 hosts externos distintos**, encadenadas:

| # | Nombre interno | Host | Método | Auth |
|---|----------------|------|--------|------|
| 1 | `busquedaVin` (ListCodeByVin) | `fasecoldaback.quantil.co` | GET | ❌ ninguna |
| 2 | `auth` (token) | `guiadevalores.fasecolda.com` | POST | ❌ (obtiene el token) |
| 3 | `listByCode` (consultabycodigo) | `guiadevalores.fasecolda.com` | GET | ✅ Bearer token del paso 2 |

Y un **cuarto endpoint interno** (`analisis`) que no llama a Fasecolda: reutiliza el flujo anterior y **filtra/selecciona** el mejor resultado en memoria.

> ⚠️ Punto clave sobre "queryParam": Fasecolda **no** recibe los datos del vehículo por query string. El **auth** los recibe por *body form-urlencoded*, y `busquedaVin`/`listByCode` reciben el VIN y los códigos como **path params**. Los query params (`vin`, `cilindraje`, etc.) existen solo en la **API propia** que envuelve a Fasecolda (los controllers `GET /api/v1/fasecolda/...`).

---

## 2. Flujo end-to-end

```
Cliente
  │  GET /api/v1/fasecolda/analisis?vin=...&modelo=...&cilindraje=...
  ▼
[Analysis Service]
  │
  ├─(a)─► [ListCodeByVin Service] getListCodeByVin(vin)
  │          │
  │          ├─(1)─► GET  https://fasecoldaback.quantil.co/api/busquedaVin/{vin}
  │          │        ← { message, codigos: [ "123", "456", ... ] }
  │          │
  │          ├─(2)─► POST https://guiadevalores.fasecolda.com/apifasecolda/token
  │          │        Content-Type: application/x-www-form-urlencoded
  │          │        grant_type=password&username=...&password=...
  │          │        ← { access_token, token_type, expires_in }
  │          │
  │          └─(3)─► GET  https://guiadevalores.fasecolda.com/apifasecolda/api/listacodigosid/consultabycodigo/{codigos}
  │                   Authorization: Bearer {access_token}
  │                   ← [ { ficha técnica + valorModelo[] }, ... ]
  │
  └─(b)─► Filtra en memoria por cilindraje / combustible / puertas / pasajeros / modelo
          Selecciona el de menor valor y multiplica valor × 1000
          ← { data: <mejor match> }  |  { data: "NotFound", statusCode: 404 }
```

**Orden de ejecución del paso (a)** (ver `fasecoldaListCodeByVinApiService.ts`):
1. `busquedaVin(vin)` → obtiene `codigos`.
2. `auth()` → obtiene `access_token`.
3. `listByCode(codigos, access_token)` → obtiene el detalle.

> Nota: en la referencia el token **se pide en cada llamada** (no hay caché). Ver §7 para la mejora recomendada.

---

## 3. Detalle de cada endpoint externo

### 3.1 Búsqueda por VIN (`busquedaVin`)

- **URL:** `GET {FASECOLDA_BY_VIN_API_BASE_URL}{FASECOLDA_BY_VIN_API_PATH}/{vin}`
  → `https://fasecoldaback.quantil.co/api/busquedaVin/{vin}`
- **Auth:** ninguna.
- **Headers:** `responseType: json`.
- **Respuesta real (validada):**
  ```json
  { "message": "Exito", "codigos": ["03021063"] }
  ```
  - `message` = `"Exito"` cuando hay coincidencia.
  - `codigos` es un **array plano de strings** (`string[]`) — ⚠️ el modelo TS de la referencia lo declara mal como `Array<string>[]` (array de arrays). En el destino tipa `string[]`.
  - Un VIN sin coincidencia devuelve **HTTP 400** (no 404 ni lista vacía). Manéjalo como "no encontrado".
- Se toma `response.codigos` para el paso 3. Nota: estos códigos son **`homoloCodigo`** (código de homologación), ver §3.3.

### 3.2 Autenticación (`token`)

- **URL:** `POST {FASECOLDA_API_BASE_URL}{FASECOLDA_AUTH_API_PATH}`
  → `https://guiadevalores.fasecolda.com/apifasecolda/token`
- **Headers:** `Content-Type: application/x-www-form-urlencoded`
- **Body (form-urlencoded):**
  ```
  grant_type = password            (FASECOLDA_API_GRANT_TYPE)
  username   = <usuario Fasecolda> (FASECOLDA_API_USERNAME)
  password   = <clave Fasecolda>   (FASECOLDA_API_PASSWORD)
  ```
- **Respuesta real (OAuth2 password grant, validada):**
  ```json
  { "access_token": "<ACCESS_TOKEN>", "token_type": "bearer", "expires_in": 86399 }
  ```
  - `expires_in` real ≈ **86399 segundos (~24 h)**, no 3600. Aprovecha esta duración para cachear el token (§7).
  - `token_type` = `"bearer"` (minúscula); al construir el header usa `Authorization: Bearer {token}`.

### 3.3 Consulta por códigos (`consultabycodigo`)

- **URL:** `GET {FASECOLDA_API_BASE_URL}{FASECOLDA_LIST_CODE_API_PATH}/{codigos}`
  → `https://guiadevalores.fasecolda.com/apifasecolda/api/listacodigosid/consultabycodigo/{codigos}`
- **Auth:** `Authorization: Bearer {access_token}`
- **Respuesta real (validada, array de objetos ficha técnica):**
  ```json
  [
    {
      "id": 2842,
      "codigo": "03042039",
      "homoloCodigo": "03021063",
      "cilindraje": 3496,
      "capacidadPasajeros": 5,
      "puertas": 4,
      "marca": "FORD",
      "clase": "PICKUP DOBLE CAB",
      "referenciaUno": "F150 [12]",
      "referenciaDos": "XLT",
      "referenciaTres": "AT 3500CC 4X4",
      "combustible": "GASOLINA",
      "tipoCaja": "AUTOMATICA",
      "transmision": "4X4",
      "categoria": "LIVIANO CARGA",
      "tipologia": "PICKUP DC",
      "valorModelo": [
        { "modelo": "2014", "valor": 119900.0, "estado": "USADO", "modeloId": 45, "idEstado": 1 },
        { "modelo": "2013", "valor": 111900.0, "estado": "USADO", "modeloId": 44, "idEstado": 1 },
        { "modelo": "2012", "valor": 104400.0, "estado": "USADO", "modeloId": 43, "idEstado": 1 },
        { "modelo": "2011", "valor":  97400.0, "estado": "USADO", "modeloId": 42, "idEstado": 1 }
      ],
      "codigoFoto": []
    }
  ]
  ```
- **Sobre "el valor adecuado" (clave):**
  - `valorModelo[].valor` es un **número** (ej. `119900.0`) — ⚠️ el modelo TS de la referencia lo declara mal como `string`. En el destino tipa `decimal`/`number`.
  - El valor viene expresado en **miles de pesos colombianos (COP)**. Por eso el servicio de análisis lo **multiplica × 1000** para entregar el valor real: `119900 × 1000 = $119.900.000 COP` (precio de una Ford F150 2014 usada, cifra coherente).
  - **Regla de oro:** el valor crudo de Fasecolda NO es el precio final; hay que multiplicarlo por 1000 (o formatearlo entendiendo que está en miles) antes de mostrarlo o persistirlo como precio en pesos.
  - `homoloCodigo` = el código que devolvió `busquedaVin`. El registro trae además su propio `codigo` (distinto). Si vas a cruzar datos, cruza por `homoloCodigo`.
  - `codigoFoto` puede venir vacío (`[]`).
  - (Ver el modelo completo `ListByCodeModelDto` en §6.)

---

## 4. Variables de entorno

Todas las que usa el servicio Fasecolda, con los **valores del proyecto de referencia**. Los tres primeros bloques (URLs/paths/timeout) son configuración pública; los marcados 🔐 son **secretos** y deben ir en un gestor de secretos (AWS Secrets Manager, User Secrets de .NET, GitHub Actions secrets, etc.), **nunca** commiteados.

| Variable | Valor de referencia | Descripción |
|----------|--------------------|-------------|
| `FASECOLDA_BY_VIN_API_BASE_URL` | `https://fasecoldaback.quantil.co` | Host de la búsqueda por VIN |
| `FASECOLDA_BY_VIN_API_PATH` | `/api/busquedaVin` | Path búsqueda por VIN (se le concatena `/{vin}`) |
| `FASECOLDA_API_BASE_URL` | `https://guiadevalores.fasecolda.com/apifasecolda` | Host de auth + consulta por código |
| `FASECOLDA_AUTH_API_PATH` | `/token` | Path del token OAuth2 |
| `FASECOLDA_LIST_CODE_API_PATH` | `/api/listacodigosid/consultabycodigo` | Path consulta por código (se le concatena `/{codigos}`) |
| `FASECOLDA_API_SECONDS_TIMEOUT` | `55` | Timeout (segundos) de las 3 llamadas Fasecolda |
| `FASECOLDA_API_GRANT_TYPE` | `password` | Grant OAuth2 |
| 🔐 `FASECOLDA_API_USERNAME` | `cristian.vasquez@quantil.com.co` | Usuario Fasecolda |
| 🔐 `FASECOLDA_API_PASSWORD` | `eBGT6$tYU` | Clave Fasecolda |

> 🔐 **Advertencia de seguridad:** estas credenciales están hoy en texto plano en el `.env` del proyecto de referencia (junto con credenciales de BD productiva). Al portarlas al nuevo proyecto **no** las escribas en el repo. Considera además rotarlas, ya que han estado versionadas/expuestas. Regla FLIT #5: sin datos sensibles en descripciones/commits.

### Equivalente en `.env` (referencia rápida)

```dotenv
# FASECOLDA
FASECOLDA_BY_VIN_API_BASE_URL=https://fasecoldaback.quantil.co
FASECOLDA_BY_VIN_API_PATH=/api/busquedaVin
FASECOLDA_API_BASE_URL=https://guiadevalores.fasecolda.com/apifasecolda
FASECOLDA_AUTH_API_PATH=/token
FASECOLDA_LIST_CODE_API_PATH=/api/listacodigosid/consultabycodigo
FASECOLDA_API_SECONDS_TIMEOUT=55
FASECOLDA_API_GRANT_TYPE=password
FASECOLDA_API_USERNAME=<secreto>
FASECOLDA_API_PASSWORD=<secreto>
```

---

## 5. Lógica del endpoint `analisis` (filtro y selección)

Fuente: `fasecoldaAnalysisApiService.ts`. Recibe por query params:
`vin` (obligatorio), `tipoCarroceria?`, `cilindraje?`, `tipoCombustible?`, `puertas?`, `pasajerosSentados?`, `modelo?`.

Algoritmo:
1. Obtiene la lista completa vía `getListCodeByVin(vin)`. Si viene vacía → `404 NotFound`.
2. **Filtra** los ítems (comparación con `!=` laxo, string vs number):
   - `cilindraje` → `item.cilindraje`
   - `tipoCombustible` → `item.combustible`
   - `puertas` → `item.puertas`
   - `pasajerosSentados` → `item.capacidadPasajeros`
   - `modelo` → debe existir en `item.valorModelo[].modelo`
   - (`tipoCarroceria` está comentado / no se aplica)
3. Si no queda ninguno → `404 NotFound`.
4. Entre los filtrados, recorre `valorModelo` del `modelo` pedido y **elige el de menor `valor`**.
5. Al ganador le **quita** `codigoFoto`, deja solo el `valorModelo` seleccionado y **multiplica `valor × 1000`** (Fasecolda entrega el valor en **miles de COP**; validado: `119900 → $119.900.000`). Este es el valor final a exponer.
6. Devuelve `{ data: <ganador> }`.

> Detalle a replicar con cuidado: el filtro por `modelo` usa `==` (línea 37) pero la selección usa `===` (línea 49). Si en el nuevo proyecto tipas fuerte (`modelo` string), unifica a comparación estricta por string.

---

## 6. Contratos de datos (para tipar en el destino)

> Los tipos abajo están **corregidos según la respuesta real** (la referencia TS tiene dos errores marcados con ⚠️).

**Request VIN:** `{ vin: string }`

**Respuesta `busquedaVin`:** `{ message: string, codigos: string[] }`
⚠️ La referencia declara `codigos: Array<string>[]`; el real es `string[]` (array plano).

**Respuesta `token`:** `{ access_token: string, token_type: string, expires_in: number }`
(`token_type: "bearer"`, `expires_in ≈ 86399`.)

**Respuesta `consultabycodigo`** (`ListByCodeModelDto`, campos principales):
```
id, consecutivo, codigo, homoloCodigo, cilindraje, peso, potencia,
capacidadPasajeros, capacidadCarga, puertas, marca, clase,
referenciaUno, referenciaDos, referenciaTres, nacionalidad,
servicio, combustible, tipoCaja, transmision, frenos,
categoria, tipologia, marcaId, nacionalidadId, claseId,
valorModelo: [{ modelo: string, valor: number, estado, modeloId, idEstado }],
codigoFoto: [{ id, nombre }]   // puede venir vacío []
```
⚠️ La referencia declara `valor: string`; el real es **numérico** (`119900.0`) y expresado en **miles de COP** (multiplicar × 1000 para el precio real).

---

## 7. Recomendaciones al portar (mejoras sobre la referencia)

1. **Cachear el token.** La referencia pide `token` en cada consulta. Usa `expires_in` para cachear y renovar solo al expirar (o ante 401). Reduce latencia y carga.
2. **No tragar errores.** Los proxies de referencia hacen `catch` y devuelven `[{ code: 404, description }]`, lo que confunde "no encontrado" con "error de red/credenciales". En el destino, propaga excepciones tipadas y distingue 401/timeout/5xx de un 404 real.
3. **Secretos fuera del repo.** Usar el gestor de secretos del stack destino (ver §4).
4. **Timeout configurable por llamada** (la referencia usa el mismo `FASECOLDA_API_SECONDS_TIMEOUT=55` para las tres).
5. **Comparaciones estrictas** al filtrar (ver §5).
6. **Reintentos** idempotentes en las 3 GET/POST ante fallos transitorios.

---

## 8. Esbozo de implementación en .NET (core-api de FLIT 2.0)

Mapeo directo de los 3 proxies + orquestador a un `HttpClient` tipado. Ejemplo mínimo:

```csharp
// appsettings.json  ->  sección "Fasecolda"
// (valores públicos; credenciales por User Secrets / Secrets Manager)
"Fasecolda": {
  "ByVinBaseUrl": "https://fasecoldaback.quantil.co",
  "ByVinPath": "/api/busquedaVin",
  "ApiBaseUrl": "https://guiadevalores.fasecolda.com/apifasecolda",
  "AuthPath": "/token",
  "ListCodePath": "/api/listacodigosid/consultabycodigo",
  "TimeoutSeconds": 55,
  "GrantType": "password"
  // Username / Password -> secretos
}

// 1) Token
var form = new FormUrlEncodedContent(new Dictionary<string,string> {
    ["grant_type"] = opts.GrantType,
    ["username"]   = opts.Username,   // secreto
    ["password"]   = opts.Password    // secreto
});
var auth = await _apiClient.PostAsync(opts.AuthPath, form);   // BaseAddress = ApiBaseUrl
var token = (await auth.Content.ReadFromJsonAsync<AuthDto>()).AccessToken;

// 2) Búsqueda por VIN  (cliente con BaseAddress = ByVinBaseUrl)
var vinRes = await _vinClient.GetFromJsonAsync<VinDto>($"{opts.ByVinPath}/{vin}");

// 3) Consulta por código  (cliente con BaseAddress = ApiBaseUrl + Bearer)
var req = new HttpRequestMessage(HttpMethod.Get,
    $"{opts.ListCodePath}/{string.Join(",", vinRes.Codigos)}");
req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
var detalle = await _apiClient.SendAsync(req);
```

Registrar dos `HttpClient` tipados (uno por host) con `IHttpClientFactory`, y una capa `FasecoldaAnalysisService` que aplique el filtro/selección de §5.

---

## 9. Checklist de implementación

- [ ] Añadir las 9 variables de §4 a la config del destino (públicas en config, secretos en gestor).
- [ ] Cliente 1: búsqueda por VIN (sin auth).
- [ ] Cliente 2: token OAuth2 password (form-urlencoded) **+ caché por `expires_in`**.
- [ ] Cliente 3: consulta por código con Bearer.
- [ ] Orquestador `getListCodeByVin`: encadena 1→2→3.
- [ ] Servicio `analisis`: filtro por atributos + selección menor valor + `× 1000`.
- [ ] Endpoint(s) HTTP propios (`/analisis`, `/ListCodeByVin`) con query params.
- [ ] Manejo de errores tipado (401 / timeout / 5xx ≠ 404).
- [ ] Rotar credenciales expuestas.
```

---

## 10. Integración en FLIT 2.0 — avalúo comercial (FEATURE-03)

> Añadido 2026-07-14 tras validar el flujo `analisis` en vivo. Este es el uso concreto que consumirá FLIT (paso comercial del traspaso).

### 10.1 Flujo elegido: `analisis` por VIN + filtros

El avalúo Fasecolda usa el **flujo `analisis`** (no solo `busquedaVin`): el VIN es la entrada y los atributos del vehículo **filtran/seleccionan** el `valorModelo` correcto. Todos los parámetros salen de `field_values` de la instancia (hidratados por RUNT en el paso de consulta):

| Parámetro Fasecolda | `field_values` (fieldKey) | Rol |
|---------------------|---------------------------|-----|
| `vin` | `vin` | Entrada → `busquedaVin` → `codigos[]` |
| `cilindraje` | `vehicle_engine_displacement` | Filtro (desambigua códigos) |
| `tipoCombustible` | `vehicle_fuel` | Filtro |
| `puertas` | *(no siempre disponible)* | Filtro opcional |
| `pasajerosSentados` | `vehicle_passengers` | Filtro |
| `modelo` (año) | `vehicle_year` | Selecciona el `valorModelo` → valor ×1000 |

Los filtros son **opcionales/laxos**: solo estrechan cuando el VIN devuelve varios códigos. El `modelo` (año) elige el `valorModelo`; si hay **años duplicados** con distinto valor, se toma el **menor** (regla de la referencia).

### 10.2 Fixture validado (mock + tests)

**VIN `93Y9SR333RJ563653`** → `busquedaVin` = `Exito`, 5 códigos → **Renault Oroch / Duster**. Valores por año del código `08042011` (Oroch INTENS OUTSIDER), en miles de COP → ×1000:

| modelo (año) | valor crudo | valor real (×1000) |
|---|---|---|
| 2027 | 108900 / 115800 | $108.900.000 / $115.800.000 |
| 2026 | 107700 / 115800 | $107.700.000 / $115.800.000 |
| 2025 | 105700 / 114000 | $105.700.000 / $114.000.000 |
| 2024 | 105600 | **$105.600.000** |
| 2023 | 104100 | $104.100.000 |

> Nota de diseño: para ese VIN los registros reales son **1330–1333 cc, 5 pasajeros, 4–5 puertas, años 2023+**. Filtrar por valores que no casan (p. ej. `cilindraje=3956`, `pasajerosSentados=2`, `modelo=2014`) devuelve **404 / sin match** → refuerza el **AC#3** (nunca bloquear el paso; mostrar fuentes disponibles e indicar la que no respondió).

### 10.3 Encaje con el patrón `IConsultationProvider`

- Nuevo `FasecoldaConsultationProvider` con `Key="fasecolda"`, modo mock/real vía `ConsultationProviderModeOptions` (toggle `FasecoldaMode`, igual que `VerifikVehicleMode`).
- La fila `external_data_sources.FASECOLDA` **ya existe** (seed HU10151); falta el `consultation_template` con `external_refs.provider="fasecolda"` (migración data-only estilo HU10201) + datos mock.
- Salida normalizada a `ConsultationResult` / `HydratedField` (o DTO tipado del handler `GetSuggestedCommercialValueHandler`), con el valor ya en pesos (×1000).
- Base gravable y Mercado Libre: providers **mock** detrás del mismo patrón (Fase 1); se activan por `appsettings` sin tocar código.
