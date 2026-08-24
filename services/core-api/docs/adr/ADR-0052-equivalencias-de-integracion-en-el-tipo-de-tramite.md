# ADR-0052 — Las equivalencias con sistemas externos son propiedad del tipo de trámite

- **Estado**: Propuesto · 2026-08-24
- **Módulo**: Trámites — catálogo de tipos (`tramites.procedure_types`) · Integraciones (ICT, Quipux)
- **Feature/Bug**: sin ADO — continuación de ADR-0050 (configurador de tipos de trámite)
- **Deciders**: Líder Técnico FLIT
- **Tags**: arquitectura, backend, integraciones, esquema

## Contexto

ADR-0050 estableció que `tramites.procedure_types` es la fuente única de la conformación del
expediente: el recorrido, las capacidades y los documentos de un trámite viven en su fila del
catálogo, y el configurador los edita sin desplegar.

Las **equivalencias con los sistemas externos** quedaron fuera de esa unificación, y hoy viven en dos
sitios con dos mecanismos distintos:

| Integración | Dónde vive | Cómo se cambia |
|---|---|---|
| Quipux | `procedure_types.external_refs -> 'quipux'` | Configurador (desde este trabajo) |
| ICT | `ict.procedure_type_mapping` (tabla propia del esquema `ict`) | Solo por DDL |

El resultado práctico: dar de alta un trámite en ICT exige una migración, y el administrador no tiene
dónde ver, en un mismo sitio, con qué códigos se identifica un trámite en cada sistema. La leyenda
que hoy muestra el configurador al crear un tipo —«hay que mapear su código en esos catálogos»— es
una confesión de esa dispersión.

### Lo que se verificó antes de decidir

1. **Los dos servicios comparten base de datos en producción.** `docker-compose.prod.yml` inyecta el
   mismo `CONNECTION_STRING_CORE` a `core-api` (línea 120) y a `core-ict` (línea 305); lo que los
   separa son los esquemas `tramites` e `ict`, no la instancia. La diferencia que se ve en los
   `appsettings` de desarrollo (`flitdev` vs `flit_dev`) es un default obsoleto, no el diseño.
2. **Nadie escribe `ict.procedure_type_mapping` desde código.** Solo la siembran dos DDL (`01` y
   `21`). Todos los consumidores leen:
   - `SendToCoreApiJob` y `AttachmentDocTypeResolver`, vía EF (`IctDbContext.ProcedureTypeMappings`);
   - `05-ICT-sp-business.sql`, con dos `JOIN` en SQL;
   - `IctQueryRepository` de **core-api**, con dos `LEFT JOIN` en SQL crudo.
3. **core-api ya cruza la frontera.** Ese último punto importa: la lectura de `ict.*` desde core-api
   ya existe y se aceptó al construir la bandeja de pre-trámites. La frontera entre ambos servicios
   ya es porosa en la dirección contraria a la que este ADR propone.

## Decisión

**El tipo de trámite es dueño de sus equivalencias con los sistemas externos. Se guardan en
`procedure_types.external_refs`, y `ict.procedure_type_mapping` pasa a ser una VISTA sobre esa
tabla.**

```
tramites.procedure_types.external_refs
  ├─ quipux : { familia, tipoTramite, tipoRequisito, prefijo, campoPlaca, campoVin, maxLongitudEmpresa }
  └─ ict    : { transactionType, isPublished, requiresCommercialValue, resolvesTransitOfficeFromRunt }
                        │
                        ▼
              VISTA ict.procedure_type_mapping
              (mismas columnas que la tabla actual)
```

Lo que hace viable la vista es el hallazgo (2): **core-ict solo lee**. Su entidad EF, su `DbContext`,
sus repositorios y sus procedimientos almacenados siguen funcionando sin cambiar una línea, porque
una vista se consulta igual que una tabla.

### Alcance de la vista

Debe reproducir las quince columnas actuales para no tocar el mapeo de EF:

- `id`, `created_at`, `created_by`, `updated_at`, `updated_by`, `deleted_at`, `deleted_by`,
  `row_version` → salen de la fila de `procedure_types`.
- `external_transaction_type` ← `external_refs->'ict'->>'transactionType'`
- `procedure_type_code` ← `code`
- `family` ← `family`
- `is_published`, `requires_commercial_value`, `resolves_transit_office_from_runt` ← del bloque `ict`.
- `description` ← `name`.

Solo se proyectan las filas con bloque `ict`: un tipo sin equivalencia no aparece en el mapeo, que es
exactamente lo que hoy significa no tener fila.

## Alternativas consideradas

**A. Dejarlo como está y añadir una pantalla de administración en core-ict.**
Preserva la frontera entre servicios y no toca su esquema. Se descarta porque no cumple el objetivo:
seguiría habiendo dos sitios donde configurar un mismo trámite, y el administrador tendría que saber
en cuál está cada cosa. Además exige construir en core-ict una superficie de administración que hoy
no existe (autenticación, autorización, UI), para gestionar catorce filas.

**B. Mantener las dos tablas y que el configurador escriba en ambas.**
Un solo punto de configuración en la UI, sin cambiar esquemas. Se descarta porque duplica la verdad:
dos copias del mismo dato que pueden divergir es precisamente el problema que ADR-0050 vino a
resolver — allí eran `modalidad_entrada` y `family`, aquí serían `procedure_type_code` y `code`.

**C. Invertir la propiedad: que el mapeo de ICT sea la fuente y `procedure_types` lo lea.**
Se descarta por dirección: la llave del mapeo es `external_transaction_type`, el vocabulario del
cliente v1. Colgar de él la identidad del catálogo de FLIT ataría el modelo propio al contrato de un
tercero, que es lo contrario de lo que ADR-0050 decidió.

## Consecuencias

### A favor

- Un solo lugar donde ver y cambiar todo lo que define un trámite, incluidas sus equivalencias.
- Dar de alta un trámite en ICT deja de ser una migración.
- Desaparece la posibilidad de que el código del tipo y el mapeado diverjan: son la misma columna.
- core-ict no cambia. Ni su código, ni sus consultas, ni sus procedimientos almacenados.

### En contra, y hay que asumirlas

- **La vista impone que ambos servicios compartan base de datos.** Hoy es cierto en producción, pero
  deja de ser una decisión reversible sin trabajo: separarlos exigiría replicación o un endpoint.
  Esta es la consecuencia más seria de este ADR.
- **`core-ict` pasa a depender del esquema `tramites`.** La dependencia ya existía en sentido
  contrario, pero ahora es bidireccional y explícita.
- **El trigger de auditoría `tr_ptm_audit` deja de disparar**: cuelga de la tabla, y una vista no
  emite eventos de fila. La auditoría del mapeo pasa a ser la del tipo de trámite.
- **La unicidad de `external_transaction_type` deja de ser un `UNIQUE` de columna.** Se sustituye por
  un índice único sobre la expresión JSONB:

  ```sql
  CREATE UNIQUE INDEX ux_procedure_types_ict_transaction
      ON tramites.procedure_types ((external_refs->'ict'->>'transactionType'))
   WHERE external_refs ? 'ict';
  ```

  Sin él, dos tipos podrían reclamar el mismo número de transacción y la materialización elegiría uno
  arbitrariamente.

### Lo que este ADR NO resuelve

Centralizar da **dónde** escribir las equivalencias, no **de dónde** sacarlas:

- Los códigos de Quipux (`tipoTramite`, `prefijo`) los asigna la secretaría.
- El número de transacción de ICT pertenece al contrato v1 del cliente de integración. Un tipo nuevo
  de FLIT no tiene número hasta que ese cliente lo añada y empiece a enviarlo. La pantalla permite
  **registrar** el acuerdo, no crearlo.

Es la razón real por la que diecisiete tipos siguen sin radicar, y no cambia con este ADR.

## Plan de implementación

1. **DDL de migración**: leer las filas actuales de `ict.procedure_type_mapping`, escribirlas en
   `external_refs->'ict'` del tipo correspondiente (emparejando por `procedure_type_code`), y
   verificar que ninguna se queda huérfana antes de continuar.
2. Crear el índice único sobre la expresión.
3. `DROP TABLE ict.procedure_type_mapping` y crear la vista con las mismas columnas.
4. Reubicar `tr_ptm_audit` o aceptar su retirada, documentándolo.
5. Pestaña «Integración (ICT)» en el configurador, con la misma separación que la de Quipux entre lo
   derivable y lo que aporta el tercero.
6. `Down` que reconstruye la tabla desde la vista: la migración debe ser reversible (regla A17).

**Puerta**: el paso 3 es destructivo sobre un esquema que no es el de core-api. Debe validarse
ejecutando la cadena completa de DDL de ambos servicios en un cluster aislado, y comprobando que
`SendToCoreApiJob`, `AttachmentDocTypeResolver` y los dos SP siguen leyendo lo mismo que antes.

## Relacionados

- [ADR-0050 — El tipo de trámite es la única fuente de verdad de la conformación](ADR-0050-tipo-de-tramite-fuente-unica-de-conformacion.md)
- [ADR-0021 — La analítica lee de la fuente de datos de trámites](ADR-0021-analitica-fuente-datos-tramites.md)
- `21-ICT-procedure-type-mapping-v2.sql` — alineación del mapeo a los codes canónicos
- `docs/handoff-adr-0050-tipo-tramite-fuente-unica.md`
