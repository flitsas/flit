# A-01 · Inventario de lo que depende de `Development`

> **HU #12894** · Feature #12886 · FLIT Suite, área A · 2026-09-25 · Samuel Cardenas
>
> Base: la rama del Feature #12886, que contiene develop más el Feature #12888. **Solo lectura del repositorio**: no se tocó ningún
> ambiente. Lo que depende de la VPS (el `.env` de cada ambiente, los contenedores en marcha) queda como pregunta para
> quien tiene acceso.

## Resumen para decidir

1. **DEV, QA y PDN corren con `ASPNETCORE_ENVIRONMENT=Development`.** `docker-compose.prod.yml` lo fija en sus tres
   servicios .NET (líneas 130, 329 y 467), y ese compose es el mismo para los tres ambientes (`cd.yml` lo copia a la
   VPS en cada despliegue).
2. **Riesgo de seguridad (a confirmar en la VPS): en los tres ambientes, la API y el gateway aceptarían cualquier
   token sin verificar firma, emisor, audiencia ni vencimiento.**
   - `docker-compose.prod.yml` no usa `env_file`: el `.env` de la VPS solo rellena los `${...}` que aparecen en el
     compose.
   - El compose no pasa ninguna variable de llave (`Jwt__PublicKeyPem`, `Jwt__PublicKeyPath`), ni a la API ni al gateway.
   - `appsettings.json` no trae llave, y `appsettings.Development.json` está en `.gitignore`, así que no entra a la
     imagen.
   - Sin llave pública, `ApiSecurityExtensions.cs:64-74` y `Flit.Gateway/Program.cs:73-80` desactivan toda validación.

   Con eso, un token fabricado con `role=SuperAdmin` pasaría las policies de SuperAdmin. **Se confirma con la pregunta 1
   de abajo y es independiente de la suite: conviene revisarlo con el líder cuanto antes.**
3. **El login firma con una llave RSA efímera** que cambia en cada reinicio o despliegue (`JwtKeyMaterialLoader`,
   `DevelopmentAuthSeeder.cs:1997-2024`). Hoy nadie lo nota porque nadie valida la firma.
4. **`Jwt__DevGenerate: "true"`** (compose, línea 154) **no hace nada**: ningún código la lee.
5. **Si DEV se renombrara hoy sin cambios de código:**
   - el login fallaría con un 500, porque fuera de `Development` exige llave privada;
   - dejaría de sembrarse el catálogo RBAC y el de organismos de tránsito;
   - desaparecería Swagger;
   - los correos dejarían de ir a la consola.

   Por eso el cambio de nombre necesita antes los ajustes de la sección «Cambios propuestos».
6. **Lo que ya corrió en PDN por ser `Development`:**
   - migraciones de datos de demostración;
   - el seeder, que creó 7 cuentas demo con contraseña fija en el código.

   Renombrar no lo deshace: hay que revisarlo y limpiarlo aparte (preguntas 6 y 7).

## Inventario

| # | Elemento (archivo:línea) | En `Development` | Con otro nombre | Afecta hoy | Si DEV pasa a otro nombre | Decisión propuesta |
|---|---|---|---|---|---|---|
| 1 | `docker-compose.prod.yml:130,329,467` `ASPNETCORE_ENVIRONMENT` fijo | — | — | DEV/QA/PDN = Development | — | `${ASPNETCORE_ENVIRONMENT:-Development}` |
| 2 | `JwtKeyMaterialLoader` (`DevelopmentAuthSeeder.cs:2007`) | Sin llave privada: RSA efímera en memoria | Excepción en el primer login | Los 3 firman con llave efímera | **Login roto (500)** | Implementar `Jwt:DevGenerate` o exigir `Jwt__PrivateKeyPem` |
| 3 | `Jwt__DevGenerate` (compose:154) | Ningún código la lee | igual | — | Nada | Implementarla (ver 2) o quitarla |
| 4 | `ApiSecurityExtensions.cs:64-74` (no depende del ambiente) | Sin llave pública acepta tokens sin firma, vencidos y de cualquier emisor | igual | **Los 3, incluido PDN** | Nada | Pasar la llave pública y exigirla fuera de local (A-03) |
| 5 | Gateway `Program.cs:46-88`: validador permisivo y `JwtRequired = RequireAssertion(_ => true)` | Sin exigencia | igual | Los 3 | Nada | A-03 |
| 6 | `DevelopmentNoJwtProxyConfigFilter` (`Gateway/Program.cs:125`) | Quita la policy de las rutas | Quedan con `JwtRequired`, que siempre pasa | Los 3 | Nada funcional | Gatear por bandera; corregir su comentario, que dice que QA/PDN «conservan» la policy |
| 7 | `DevelopmentAuthSeeder` (`:87`): catálogo RBAC (módulos, permisos, roles de sistema) | Lo siembra en cada arranque | No lo siembra | Los 3 | **Los permisos nuevos no llegan a DEV** | Separar un `RbacCatalogSeeder` que corra siempre |
| 8 | Mismo seeder: 7 cuentas demo con contraseña fija | Las crea y les reasigna el rol en cada arranque | No | **Los 3, incluido PDN** | Dejan de crearse (a favor) | Gatear por `Seed:DemoUsers`; revisar y desactivar en PDN |
| 9 | Mismo seeder: catálogo RUNT de organismos (`27-HU10659`, 298 filas) | Lo actualiza en cada arranque | No | Los 3 | Una base nueva quedaría sin catálogo | Pasarlo al seeder de catálogo o a una migración |
| 10 | 8 migraciones de datos demo que leen `ASPNETCORE_ENVIRONMENT` (3 también aceptan `QA`) | Siembran datos mock | No, salvo `FLIT_DEV_SEED` (que el compose no pasa) | Ya corrieron en los 3 | Las futuras no siembran en DEV | Pasar `FLIT_DEV_SEED` en el compose; limpiar el mock de PDN |
| 11 | `SeedMockCompanies` (sin condición) | 8 empresas `MOCK-*` | igual | Los 3 | Nada | Revisar y limpiar en PDN |
| 12 | Swagger (core-api `Program.cs:239`, core-ict `Program.cs:21`) y rutas `/swagger` del gateway | Expuesto | 404 | **Los 3, público** | DEV lo pierde | Bandera `Swagger:Enabled` (por defecto: solo en Development); apagado en PDN |
| 13 | Correo por consola (`InfrastructureExtensions.cs:376`) | Sin `SMTP_HOST`: consola | SMTP con host vacío: falla | Según el `.env` | Correos fallan si no hay `SMTP_HOST` | Bandera explícita |
| 14 | Página de error de desarrollo (implícita, no hay `UseExceptionHandler`) | Stack trace en los 500 | 500 sin detalle | **Los 3** | A favor | `UseExceptionHandler` + ProblemDetails (decisión aparte para PDN) |
| 15 | Validación del contenedor de DI (`ValidateScopes`/`ValidateOnBuild`, implícita) | Activa | Inactiva | Los 3 | Errores de DI pasan a salir en runtime | Fijarla en código |
| 16 | core-ict `appsettings.Development.json` (versionado, entra a la imagen) | Se carga | No | Los 3 | Solo cambia un nivel de log | Mover lo necesario a `appsettings.json` |
| 17 | `appsettings.QA.json` de Api y Gateway (versionados) | No se cargan | Solo si el nombre es `QA` | Ninguno | — | Decidir si se usan o se borran |
| 18 | URLs de correo en `Flit.Api/appsettings.json` (invitación, recuperación, recursos, marca) | `dev.flitsas.online` | igual | **QA y PDN envían enlaces de DEV** | — | Pasarlas por `.env` en el compose (A-12) |
| 19 | Llave JWT de ICT (`IctJwtKeyMaterial.cs:56-73`, no depende del ambiente) | En `/tmp` del contenedor | igual | Los 3: cambia en cada despliegue | Nada | Montar `IctJwt__PrivateKeyPem` |
| 20 | `migrador` y `migracion-api` | Ya corren como Production (el compose no fija el ambiente) | — | — | Nada | Nada |
| 21 | Modo real o mock de Verifik, Kyverum, Fasecolda, OCR, Quipux y Renting | Configuración explícita, no el nombre | igual | — | Nada | Nada |

**Otros hallazgos fuera del alcance de A-01:**
- `docker-compose.yml:36`, el compose **local**, tiene versionado un valor de `Smtp__DefaultSenderPassword` que no
  parece de ejemplo. Conviene rotarlo y reemplazarlo por una variable.
- `InfrastructureExtensions.InitializeInfrastructureAsync` (`:1300-1308`) no se llama desde ninguna parte.

## Preguntas para quien tiene acceso a la VPS

Solo interesa saber si una variable existe o está vacía, **nunca su valor**.

1. En DEV, QA y PDN, ¿`docker inspect <core-api> <gateway> --format '{{json .Config.Env}}'` muestra `Jwt__PublicKeyPem`,
   `Jwt__PublicKeyPath`, `Jwt__PrivateKeyPem` o `Jwt__PrivateKeyPath`? ¿Y `IctJwt__*` en core-ict?
2. ¿El `docker-compose.prod.yml` de la VPS es idéntico al del repo? ¿Hay un `docker-compose.override.yml` o contenedores
   levantados a mano con variables extra?
3. ¿El `.env` de cada ambiente define `SMTP_HOST`?
4. ¿Define `ICT_SERVICE_TOKEN_SECRET`, `FLIT_INTERNAL_API_KEY`, `FLITMIG_MIGRACION_API_KEY` y `FLITMIG_MIGRACION_API_ENABLED`?
5. ¿Qué modos tienen `VERIFIK_*_MODE`, `INTEMPO_MODE`, `FASECOLDA_MODE`, `BIOMETRICS_PROVIDER`, `OCR_PROVIDER`,
   `RENTING_API_ENABLED`, `RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED` y `TRAMITE_VALIDATION_*_MODE` en cada ambiente?
6. En QA y PDN, ¿existen y están activas las cuentas demo `demo@flit.local`, `otadmin@flit.local`,
   `otsabaneta@flit.local`, `otenvigado@flit.local`, `admin@empresa.local`, `radicador@empresa.local` y `dev@flitsas.io`?
7. ¿Existen en PDN los datos mock: la empresa `11111111-1111-1111-1111-111111111111`, las `MOCK-*`, OT-SABANETA,
   OT-ENVIGADO, Ricaurte y los seeds de avalúo, LOG QX y analítica?
8. ¿Qué filas tiene `__EFMigrationsHistory` en cada ambiente?
9. ¿Responden públicamente `https://api.flitsas.online/swagger` (y los de QA y DEV) y `https://ict.flitsas.com/swagger`?
   ¿nginx bloquea `/swagger`?
10. ¿DEV, QA y PDN comparten VPS y servidor de Postgres? ¿Las bases están separadas?
11. ¿`admin.quipux_settings.enabled` está encendido en algún ambiente?
12. ¿Alguien depende de Swagger o de las cuentas demo en DEV o QA (Postman, QA, clientes ICT)?
13. Si se genera un par RSA para el JWT, ¿dónde se guardaría en la VPS: como variable del `.env` o como archivo montado?
14. ¿Los correos de invitación y recuperación de QA y PDN llegan con enlaces a `dev.flitsas.online`?
15. ¿Alguien intentó poner `FLIT_DEV_SEED` en el `.env`? Hoy no llega al contenedor.

## Cambios propuestos (A-02 y A-03)

> **Estado (2026-09-28):** los cambios 1 a 5 están hechos (HU #12895), con otra forma en tres puntos: el nombre del ambiente
> usa su propia variable, `FLIT_DOTNET_ENVIRONMENT`; la llave del JWT se persiste en la base (HU #12896) en vez de
> `Jwt:DevGenerate`, que se retiró; y las migraciones de datos demo conservan su condición y solo reciben `FLIT_DEV_SEED`.
> El cambio 6 está hecho en la API; el del gateway pasa a A-05. Variables en `.env.prod.example`.

Todos son **compatibles hacia atrás**: con el `.env` actual nada cambia. El cambio real es una línea en el `.env` de
DEV, que pone quien tiene acceso a la VPS, y se revierte borrándola.

1. **Compose:** `ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Development}` en los tres servicios.
2. **Llave del JWT:**
   - implementar `Jwt:DevGenerate` (el compose ya la pasa), para que un DEV renombrado siga pudiendo iniciar sesión;
   - pasar `Jwt__PrivateKeyPem: ${JWT_PRIVATE_KEY_PEM:-}` a la API y `Jwt__PublicKeyPem: ${JWT_PUBLIC_KEY_PEM:-}` a la
     API y al gateway. Vacías, todo sigue igual.
3. **Seeder:** separar `RbacCatalogSeeder`, que corre siempre (roles de sistema, módulos, permisos y catálogo de
   organismos), de `DemoDataSeeder`, que corre con `Seed:DemoUsers` (por defecto: solo en Development).
4. **Migraciones de datos demo:** una sola función para decidir si siembran, y `FLIT_DEV_SEED: ${FLIT_DEV_SEED:-}` en
   el compose.
5. **Banderas explícitas:** `Swagger:Enabled`, `Smtp:UseConsoleWhenNoHost` y el filtro del gateway. Por defecto hacen lo
   mismo que hoy.
6. **Validación del token (A-03)**, cuando existan las llaves:
   - la API falla al arrancar sin llave pública fuera de local;
   - el gateway valida firma, emisor, audiencia y vencimiento;
   - `JwtRequired` exige usuario autenticado.

   Se prueba con token manipulado, vencido y de otro emisor.

**Orden sugerido:** el líder responde las preguntas 1 y 2 → los cambios 1 a 5 (nada cambia al desplegarlos) → el líder
pone las llaves y el nombre nuevo solo en el `.env` de DEV → se observa un sprint → el cambio 6 → QA y PDN, con su
aprobación.
