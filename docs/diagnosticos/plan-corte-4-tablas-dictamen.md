# Plan del corte 4 — tablas del dictamen y siembra del catálogo

Fecha: 2026-11-03. Rama `feature/AB-12881-diagnostico-d0-d2`. Épica #12678, sin historia nueva. Este corte no se implementa hasta confirmar el plan.

El catálogo v1 ya está en `CatalogoReglasDictamen` y el lector ya arma los hechos. Este corte los deja en Postgres. No evalúa un vehículo, no llama al redactor y no publica endpoints.

Antes de `migrations add`, el corte 3 tiene que estar commiteado. Si no, la migración mezcla el lector con las tablas.

## Orden

1. **ADR-0077**, estado Propuesto, en `docs/decisions/ADR-0077-diagnostico-dictamen-por-reglas.md`. Lo redacta arquitectura. No pasa a Aceptado: eso es del Líder Técnico.
2. Un párrafo de excepción en **ADR-0072** para `reglas_dictamen`. Ese ADR ya prevé el catálogo global y pide la justificación al materializarlo. Sigue en Propuesto.
3. Recién con esos textos escritos, la migración y el mapeo EF. Una sola migración, generada con `dotnet ef migrations add`. No se edita una migración ya aplicada.
4. `db-schema-validator` sobre esa migración antes de dar el corte por cerrado.

## ADR-0077 — qué decide

El veredicto lo calcula `EvaluadorDictamen`. El redactor lo explica y no lo cambia. El catálogo es de FLIT, igual para todas las empresas. El dictamen de un vehículo es de la empresa.

La condición de cada regla vive en el evaluador, una por código. La columna `condicion` no es un motor de reglas: guarda el mismo código, para que la fila no quede vacía y no pueda divergir.

Alternativas que el ADR tiene que comparar, en el formato FLIT (pros, contras, esfuerzo, riesgo):

| Opción | Idea |
| --- | --- |
| A, la de este corte | Condición en código. `condicion` es `{"codigo":"<el mismo de la fila>"}` |
| B | Condición declarativa en JSON, evaluada por un intérprete |
| C | Sin tabla de reglas: el catálogo sigue solo en código y la base solo guarda dictámenes |

La decisión de este plan es A. B abre un segundo lenguaje de reglas. C obliga a desplegar código para saber qué versión dictaminó un vehículo ya guardado.

## `diagnostico.reglas_dictamen`

Catálogo global. Sin `tenant_id` y sin filtro de empresa. La entidad entra en la lista cerrada `CatalogosGlobales` de `EntidadesConTenantTests`. Sumar esa línea es parte del corte: sin ella el test de aislamiento falla.

| Columna | Regla |
| --- | --- |
| `id` | uuid, `ValueGeneratedNever`. En la siembra es un UUID v5 estable de `catalogo_version`, `tipo_tramite_codigo` y `codigo`. No es v7: `HasData` no puede cambiar de id entre máquinas. Los dictámenes que nazcan en el corte 5 sí usan v7 |
| `catalogo_version` | int, no nulo. La v1 es 1 |
| `tipo_tramite_codigo` | varchar(64), no nulo |
| `codigo` | varchar(64), no nulo |
| `condicion` | jsonb, no nulo. Siempre `{"codigo":"<codigo>"}`. `jsonb_typeof` es `object`. Un CHECK exige `condicion->>'codigo' = codigo` |
| `efecto` | varchar(16). CHECK `BLOQUEA` o `ADVIERTE` |
| `plantilla_texto` | text, no nulo. El texto de `CatalogoReglasDictamen` |
| `vigente` | boolean, no nulo. Las 42 filas de la v1 nacen en true |
| `xmin` | `IsRowVersion()`. El único update previsto es pasar `vigente` a false cuando exista otra versión |

No lleva `deleted_*`, ni actor, ni `tenant_id`. No se edita a mano. La excepción queda escrita en ADR-0072 junto a `organismos` y `departamentos_impuesto`.

Único: `(catalogo_version, tipo_tramite_codigo, codigo)`.

## Siembra

42 filas, las de `CatalogoReglasDictamen.Filas`: 14 reglas por `TRASPASO_STANDARD`, `TRASPASO_UNILATERAL` y `TRASPASO_TRANSFERENCIA_DE_DOMINIO`.

La siembra no copia los textos a mano. Un método lee `Filas` y produce las filas de `HasData`. Así la plantilla y el efecto no pueden separarse del catálogo en memoria.

`SOAT_NO_VIGENTE` y `RTM_NO_VIGENTE` quedan en `BLOQUEA`. El resto, incluida `PRENDA`, `IMPUESTO` y `FUENTE_PENDIENTE`, en `ADVIERTE`.

## `diagnostico.dictamenes`

Clase (a) de ADR-0072. Con `tenant_id`, `IEntidadDeEmpresa` y el filtro global. No hace falta excepción de catálogo. La tabla nace vacía: cada fila la escribe el corte 5, cuando un vehículo termina.

Sigue el plan §5, con las claves compuestas que ADR-0072 (c) exige y el plan dibujaba simples:

| Pieza | Materialización |
| --- | --- |
| PK | `id` uuid v7 de la aplicación, `ValueGeneratedNever` |
| Empresa | `tenant_id uuid NOT NULL` y `uq_dictamenes_tenant_id (tenant_id, id)` |
| Vehículo | FK `(tenant_id, vehiculo_id)` → `vehiculos (tenant_id, id)`, `ON DELETE RESTRICT`. Sin `ON UPDATE` emitido |
| Versión | `ux_dictamen_version (tenant_id, vehiculo_id, version)` |
| Vigente | índice único parcial `(tenant_id, vehiculo_id) WHERE vigente` |
| Veredicto | CHECK `VIABLE` o `BLOQUEADO` |
| `alertamientos` | jsonb, no nulo, arreglo. `jsonb_typeof` es `array`. Sin documentos ni nombres: código, texto, efecto y fuente |
| `catalogo_version` | int, no nulo. La fila recuerda qué catálogo usó |
| Redacción | `redactado_por` `PLANTILLA` o `LLM`. `estado_redaccion` `PENDIENTE` o `COMPLETADA`. CHECK: completada si y solo si la redactó el LLM |
| Cola | `redaccion_intentos` default 0, `redaccion_disponible_desde` y `redaccion_lease_hasta` nulos. El índice parcial que cruza empresas no se crea aquí |
| Override | `analisis_override` default false, `usuario_override_id` y `override_en` nulos. Ningún endpoint los usa todavía |
| Autoría | `creado_en timestamptz` no nulo. Concurrencia con `xmin` |

El índice `ix_redaccion_pendiente` del plan no empieza por `tenant_id`. Eso es una lectura entre empresas y solo se justifica cuando exista el trabajador de la cola (corte 6), igual que la cola del ZIP. Crearlo ahora abriría una excepción de aislamiento sin dueño.

No hay PII en claro. `usuario_override_id` es un uuid.

## Qué queda fuera

- Enganchar `AvanceVehiculo.Terminado`, llamar al lector o al redactor, y guardar la primera fila de `dictamenes`. Eso es el corte 5.
- La cola durable y la invalidación del PDF. Corte 6.
- `GET`, `PUT`, el evento y la pantalla. Corte 7.
- Marcar las fuentes obligatorias en el alta. Corte 8, después de R-17.
- Un intérprete de `condicion`.
- Activar la épica #12678.

## Archivos

| Archivo | Papel |
| --- | --- |
| `docs/decisions/ADR-0077-diagnostico-dictamen-por-reglas.md` | Decisión, en Propuesto |
| `docs/decisions/ADR-0072-diagnostico-aislamiento-por-tenant.md` | Fila y párrafo de `reglas_dictamen` |
| Entidad, configuración EF y `HasData` bajo `services/core-diagnostico` | Mapeo y siembra |
| Una migración nueva en `Persistence/Migrations` | Solo la que emita `dotnet ef migrations add` |
| `EntidadesConTenantTests.CatalogosGlobales` | Suma la entidad del catálogo |
| Test de la siembra | Las 42 filas coinciden con `CatalogoReglasDictamen.Filas` |

## Pruebas

Primero el test de la siembra, en rojo, antes de la configuración EF. Falla porque el proyector de filas todavía no existe. Afirma:

- 42 filas, versión 1, todas vigentes.
- La clave `(version, tipo, codigo)` no se repite.
- Efecto y plantilla son los de `Filas`.
- `condicion` es un objeto cuyo `codigo` es el de la fila.
- `SOAT_NO_VIGENTE` y `RTM_NO_VIGENTE` bloquean. `PRENDA`, `IMPUESTO` y `FUENTE_PENDIENTE` advierten.
- El id de la misma clave es estable en dos llamadas.

Después, la migración. `EntidadesConTenantTests` tiene que seguir en verde: el dictamen lleva filtro de empresa y la regla está en la lista cerrada.

No se levanta un Postgres nuevo para este corte. El validador de schema revisa la migración. La corrida de tests es el proyecto de Diagnóstico, con filtro de la siembra y de `EntidadesConTenantTests`, después de un build de ese proyecto y `dotnet test --no-build`.

El formato, con severidad error, solo sobre los `.cs` del diff.

## Listo cuando

- ADR-0077 está en Propuesto y ADR-0072 nombra la excepción de `reglas_dictamen`.
- La migración la generó EF, `migrations list` la muestra, y `db-schema-validator` no la bloquea.
- La siembra coincide con el catálogo en memoria y `dictamenes` no tiene filas de semilla.
- No hay endpoint nuevo ni llamada desde `AvanceVehiculo`.
- El registro de la corrida queda en este documento, en local, sin Azure DevOps.
