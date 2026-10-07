# Postgres: un usuario por servicio

[ADR-0064](../../docs/decisions/ADR-0064-datos-separados-eventos-y-reportes.md), decisión 1: cada servicio es dueño de
sus datos, en su esquema y con su usuario. Ningún servicio lee el esquema de otro. HU #13330.

## Alcance

| Servicio | Usuario | Esquema | Estado |
|---|---|---|---|
| core-consultas | `flit_consultas` | `consultas` | Con este script, antes de su primer despliegue |
| core-notificaciones | `flit_notificaciones` | `notificaciones` | Con este script, antes de su primer despliegue |
| core-api, core-identity, core-ict | el actual de `CONNECTION_STRING_CORE` | varios | **Sin cambios por ahora**: Trámites todavía lee las tablas de Identidad y los tres comparten el usuario dueño. Se separan cuando se pague esa deuda (ver la Epic #13316, «después») |

La regla estricta aplica desde los servicios nuevos: nacen aislados y no hay que separarlos después.

## Crear o actualizar el usuario de un servicio

Con un usuario administrador, una vez por servicio y por ambiente. Es idempotente: correrlo de nuevo cambia la clave
o el tope de conexiones.

```bash
CLAVE="$(openssl rand -base64 32)"   # va al secreto del ambiente, nunca al repo
psql "$ADMIN_URL" -v servicio=consultas -v conexiones=20 -v clave="$CLAVE" \
     -f deploy/postgres/servicio-con-esquema-propio.sql
```

- `conexiones` es el tope del usuario en Postgres (HU #13331). Tiene que ser mayor o igual al `Maximum Pool Size` de la
  cadena de conexión del servicio.
- El servicio crea sus tablas con sus migraciones; su tabla `__EFMigrationsHistory` vive en su esquema.

## Verificar el aislamiento

Conectado **como el usuario del servicio**. Sale con código 0 solo si puede crear en su esquema y no puede leer ni crear
en otro esquema ni en `public`; si algo falla, sale con código 3 y dice qué.

```bash
psql "postgresql://flit_consultas:$CLAVE@<host>/<base>" -v servicio=consultas -v ajeno=tramites \
     -f deploy/postgres/verificar-aislamiento.sql
```

## Migrar la configuración de consultas a Consultas (HU #13344)

Una vez por ambiente, con un usuario administrador (el de Consultas no puede leer `admin`, a propósito), después de que
core-consultas aplicó sus migraciones y **antes** de encender `CONSULTAS_REMOTO_HABILITADO` en core-api:

```bash
psql "$ADMIN_URL" -v ON_ERROR_STOP=1 -f deploy/postgres/migrar-configuracion-consultas.sql
```

Copia de `admin.tenant_operational_policies` a `consultas.configuracion_empresa` la cadena de proveedores por tipo, el
presupuesto de failover, la fuente de comparendos y los avalúos, con los mismos valores; si alguna empresa no queda
igual, no confirma nada. Es idempotente. Desde que la bandera está encendida, core-api escribe en las dos cada vez que
el SuperAdmin guarda (primero en Consultas: si no responde, no se guarda nada).

