# ADR-0080: Los departamentos del impuesto se encienden en una tabla

**Fecha**: 2026-11-04
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana, regla FLIT 15).
**Tags**: arquitectura, backend, datos, consultas, impuesto
**Épica / HU**: #12659 · trabajo en `feature/AB-12881-diagnostico-d0-d2`, sin work item (registro diferido)
**Diseño**: enmienda el interruptor de ADR-0079. La lista legada es `public.vehicle_taxes_query_api_departments`.

## Contexto

ADR-0079 dejaba el encendido del impuesto en la variable `IMPUESTO_DEPARTAMENTOS`. Encender un departamento exigía redesplegar. En el sistema anterior la lista efectiva es una tabla: una fila por código, y `deleted_at` apaga el departamento sin borrarlo.

La foto leída el 2026-11-04 tiene cinco filas. Activas: 94 Guainía, 50 Meta, 52 Nariño. Apagadas: 17 Caldas y 63 Quindío.

El catálogo es el mismo para todas las empresas (R-23: lo decide core-consultas). No lleva datos de personas.

## Decisión

La lista efectiva vive en `consultas.vehicle_tax_departments`. Una fila cuenta si `is_active` es verdadero y `deleted_at` es nulo. `ListarDepartamentosImpuesto` devuelve esos códigos cuando el transporte está listo; si no, lista vacía. `ConsultarImpuestoVehicular` rechaza con `FAILED_PRECONDITION` un código que no está activo.

La administración usa `ConsultasAdminService`, scope `platform.consultas.admin`:

- `ListarCatalogoDepartamentosImpuesto` devuelve todas las filas, encendidas y apagadas.
- `GuardarDepartamentoImpuesto` crea o actualiza. Solo acepta un código de la homologación. `activo=false` conserva la fila y la apaga.

La semilla copia las cinco filas legadas. Los demás departamentos de la homologación no se insertan hasta que alguien los guarde.

Excepción de catálogo global, al modo de ADR-0019: sin `tenant_id`, sin RLS y sin trigger de auditoría. El esquema `consultas` no comparte las funciones de `core-api`, y el identificador lo genera la aplicación (`Guid` versión 7), sin `DEFAULT uuidv7()`.

## Alternativas consideradas

### Opción 1: Tabla en el esquema de consultas

**Pros:**
- Encender o apagar un departamento no redespliega el servicio.
- La réplica de Diagnóstico sigue leyendo `ListarDepartamentosImpuesto`. El contrato de códigos no cambia.
- La semilla arranca con el mismo estado que la tabla legada.

**Cons:**
- Excepción al checklist de `tenant_id` y de triggers. Queda escrita en este ADR.
- Hace falta Postgres para la lista. Sin la migración, la lista queda vacía y el impuesto no consulta.

**Esfuerzo:** S
**Riesgos:** Un `Guardar` con un código de la homologación lo enciende de inmediato en el siguiente listado. El alcance es de plataforma, no de una empresa.

### Opción 2: Seguir con `IMPUESTO_DEPARTAMENTOS`

**Pros:**
- No hay tabla ni migración. El corte C3-3 ya lo lee al arrancar.

**Cons:**
- Cada alta o baja es un cambio de configuración y un reinicio.
- No se puede ver, desde una API, cuáles están apagados.

**Esfuerzo:** S
**Riesgos:** El estado queda fuera de la base y se desalinea de la tabla legada.

### Opción 3: Editar la réplica `diagnostico.departamentos_impuesto`

**Pros:**
- La tabla ya existe en Diagnóstico.

**Cons:**
- Rompe R-23. Esa réplica es caché del rpc, y el plan prohíbe editarla a mano.
- Diagnóstico volvería a decidir qué departamentos consulta el proveedor.

**Esfuerzo:** S
**Riesgos:** Dos listas, y la sincronización pisa la edición.

## Tradeoff aceptado

Se acepta un catálogo global sin `tenant_id` ni RLS. La protección es el scope `platform.consultas.admin` en la escritura. La lectura de la lista efectiva sigue en `ListarDepartamentosImpuesto`, con token de servicio y sin empresa.

## Consecuencias

### Lo que se gana
- El encendido es un dato, con historial de apagado en `deleted_at`.
- Diagnóstico no cambia su contrato: sigue recibiendo códigos.

### Lo que se pierde
- `IMPUESTO_DEPARTAMENTOS` deja de tener efecto. Un ambiente que la tenía puesta no enciende nada hasta la semilla o un `Guardar`.

### Seguimiento
- Backend: migración, catálogo y los dos rpc de administración.
- QA: la lista efectiva de la semilla es 50, 52 y 94. Caldas y Quindío responden `FAILED_PRECONDITION`.
- Seguridad: la tabla no tiene PII. El nombre es el del departamento.
- Infra: aplicar la migración con el arranque del servicio. No hace falta variable nueva.

## Referencias
- ADR-0079: fachada del impuesto. Este ADR sustituye solo el interruptor de departamentos.
- ADR-0019: catálogos globales sin `tenant_id`.
- ADR-0070: gRPC entre servicios.
- Tabla legada: `public.vehicle_taxes_query_api_departments`.
