# La suite en local

Cómo levantar el hub, Trámites y la API en tu máquina con el inicio de sesión único de la suite, y qué probar. Es el
modelo del simulador de Jorman (ejemplo 2): el login vive solo en el hub, cada producto tiene su propia dirección y
entra por redirección sin volver a pedir la contraseña.

## Regla de oro: `127.0.0.1`, nunca `localhost`

Abre todo por `http://127.0.0.1:<puerto>`. Las cookies no distinguen puertos: en `localhost` el navegador junta las de
todas las apps que hayas corrido alguna vez (Trámites, V1, otros proyectos) y la cabecera `Cookie` pasa de 16 KB, el
límite de Node. Cuando pasa, **todas** las peticiones fallan (431 o página en blanco). En `127.0.0.1` solo están las
cookies de la suite (~1,3 KB por app).

Si igual necesitas `localhost`, arranca Next con `NODE_OPTIONS=--max-http-header-size=131072`.

## Qué corre y dónde

| App | Dirección | Qué hace |
|---|---|---|
| API (`core-api`) | `http://127.0.0.1:4903` | Servidor de login (OIDC) y la API |
| Hub (`frontend-hub`) | `http://127.0.0.1:4040` | Portada, login, inicio con productos |
| Trámites (`frontend`) | `http://127.0.0.1:3000` | El producto |

## 1. API

La base se migra sola al arrancar (las DDL van dentro de las migraciones). Usa una base con los usuarios de demo.

```bash
cd services/core-api/src/Flit.Api
dotnet build
ConnectionStrings__Core="Host=localhost;Port=5432;Database=<tu_base>;Username=<tu_usuario>" \
ASPNETCORE_URLS=http://127.0.0.1:4903 \
ASPNETCORE_ENVIRONMENT=Development \
Suite__Oidc__Enabled=true \
Suite__Hosts__Overrides__plataforma=http://127.0.0.1:4040 \
Suite__Hosts__Overrides__tramites=http://127.0.0.1:3000 \
dotnet bin/Debug/net10.0/Flit.Api.dll
```

- `Suite__Oidc__Enabled=true` enciende el servidor de login. En `appsettings.Development.json` sigue apagado a
  propósito: el DEV de la VPS también corre en `Development`.
- Los `Overrides` le dicen a la API dónde vive cada app: con ellos arma las direcciones de retorno del login y los
  enlaces del menú de productos (`me/apps`).

## 2. Hub

```bash
cd frontend-hub
PORT=4040 CORE_API_ORIGIN=http://127.0.0.1:4903 BRANDING_INTERNAL_API_URL=http://127.0.0.1:4903 \
FLIT_HUB_URL=http://127.0.0.1:4040 TRAMITES_URL=http://127.0.0.1:3000 \
npx next dev -H 127.0.0.1 -p 4040
```

## 3. Trámites con la sesión de la suite

```bash
cd frontend
NEXT_PUBLIC_API_BASE_URL= CORE_API_ORIGIN=http://127.0.0.1:4903 BRANDING_INTERNAL_API_URL=http://127.0.0.1:4903 \
FLIT_SESSION_MODE=oidc FLIT_HUB_URL=http://127.0.0.1:4040 \
npx next dev -H 127.0.0.1 -p 3000
```

- `NEXT_PUBLIC_API_BASE_URL=` (vacío) hace que Trámites llame a la API por su mismo origen; el `.env.local` de siempre
  la apunta a `localhost`.
- Sin `FLIT_SESSION_MODE=oidc`, Trámites usa su login de siempre (el de todos los ambientes hoy).
- `FLIT_SESSION_SECRET` no hace falta en `next dev`: se usa una clave fija de desarrollo. En un build de producción es
  obligatoria (mínimo 32 caracteres).

## 4. (Opcional) core-identity aparte

El servicio de identidad (`services/core-identity`, Epic #13217, [identidad-frontera.md](identidad-frontera.md)). Se
arranca **después** de la API, que es la que migra la base. Usa la misma configuración base que la API; su perfil de
`launchSettings.json` ya trae OIDC encendido y los `Overrides` de `127.0.0.1`.

```bash
cd services/core-identity/src/Flit.Identity.Api
dotnet build
ConnectionStrings__Core="<la misma de la API>" \
ASPNETCORE_URLS=http://127.0.0.1:4905 ASPNETCORE_ENVIRONMENT=Development \
Suite__Oidc__Enabled=true \
Suite__Hosts__Overrides__plataforma=http://127.0.0.1:4040 \
Suite__Hosts__Overrides__tramites=http://127.0.0.1:3000 \
dotnet bin/Debug/net10.0/Flit.Identity.Api.dll
```

- El **hub** habla solo con identidad: `CORE_API_ORIGIN` y `BRANDING_INTERNAL_API_URL` a `http://127.0.0.1:4905`.
- **Trámites** sigue con `CORE_API_ORIGIN=http://127.0.0.1:4903`: su login pasa por el hub, que ya va a identidad.
- `curl http://127.0.0.1:4905/health/ready` responde `ready`; una ruta de negocio (por ejemplo
  `/api/v1/public/banners/active`) responde 404 en 4905 y 200 en 4903.
- **La prueba que importa:** apaga la API (4903) y entra por `127.0.0.1:3000`. El login funciona y Trámites abre con la
  sesión; solo fallan sus datos. Al volver a levantar la API, recarga: los datos aparecen sin volver a iniciar sesión.

En el servidor esto no se arma a mano: es el servicio `core-identity` del compose (perfil `identity`, su propia imagen)
y la bandera `FLIT_IDENTITY_CLUSTER_ENABLED` del gateway.

## Qué probar

Usuarios de las semillas (`DevelopmentAuthSeeder.cs`): `demo@flit.local` (SuperAdmin), `admin@empresa.local`
(AdminCompany, solo Trámites) y `otadmin@flit.local` (Admin OT). Las contraseñas están en el seeder.

| # | Caso | Qué debe pasar |
|---|---|---|
| 1 | Abrir `127.0.0.1:4040` sin sesión | Portada breve con «Iniciar sesión» |
| 2 | Iniciar sesión en el hub con un usuario de un solo producto | Entra directo a Trámites |
| 3 | Menú ▦ → Inicio (o `127.0.0.1:4040/?inicio=1`) | Inicio del hub con sus productos y accesos de administración, sin volver a hacer clic en «Iniciar sesión» (entra en silencio con la sesión del hub) |
| 4 | Abrir `127.0.0.1:3000` en otra pestaña, sin haber iniciado sesión en ningún lado | Va al login del hub y, al entrar, vuelve a Trámites |
| 5 | Con sesión en el hub, abrir `127.0.0.1:3000` | Entra sin pedir contraseña (inicio de sesión único) |
| 6 | Apagar Trámites para la empresa (SuperAdmin, configuración de la compañía) y abrir `127.0.0.1:3000` | «Tu empresa no tiene Trámites» con «Ir a mis productos» |
| 7 | AdminCompany en Trámites | Ve «Administración» y «Usuarios» como con el login de siempre |
| 8 | Cerrar sesión en Trámites (o en el hub) | Se cierra en toda la suite al instante: el hub vuelve a la portada y Trámites pide login en su siguiente página |
| 9 | Iniciar sesión en el hub con el SuperAdmin (tiene todos los productos) | Inicio del hub con una tarjeta por producto; la de Trámites entra sin pedir contraseña |

## Problemas conocidos

- **Página en blanco o 431:** estás en `localhost`. Ver la regla de oro.
- **«Abriendo tu sesión…» que no termina:** el hub y la API no están arriba, o la API no tiene los `Overrides` y
  rechaza la dirección de retorno.
- **Cambiaste `FLIT_SESSION_SECRET` entre arranques:** las cookies anteriores ya no se pueden leer y el hub muestra la
  portada; vuelve a iniciar sesión.
