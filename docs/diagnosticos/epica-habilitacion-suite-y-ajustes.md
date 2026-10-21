# Épica nueva y ajustes a épicas existentes — Diagnóstico Vehicular

> **Proyecto:** FLIT - EVOLUTION
> **Fecha:** 2026-09-28
> **Origen:** contraste entre `diagnostico_vehicular_epicas.md` (épicas 12637–12680) y `propuesta_implementacion_diagnostico_vehicular.md`
> **Objetivo:** cubrir en Azure DevOps el trabajo que la propuesta incorpora y que ninguna de las nueve épicas contempla hoy.

Este documento tiene tres partes:

1. **Épica nueva** — lista para crear como work item.
2. **Criterios a agregar** a seis épicas existentes, redactados para pegar tal cual.
3. **Resumen de impacto** — qué se crea, qué se amplía y qué cambia de texto.

---

# Parte 1 — Épica nueva

## Diagnóstico Vehicular — [0] Habilitación en la Suite FLIT

| Campo | Valor |
|---|---|
| **Epic ID** | Por asignar |
| **Etiqueta interna** | MOD00 |
| **Depende de** | Puerta de salida de la plataforma (`plan-suite-flit.md`) |
| **Bloquea a** | 12640, 12641, 12659, 12669, 12678, 12679, 12680 |
| **Relacionada con** | 12637 (visión y arquitectura), 12638 (diseño) |

### Descripción

La épica 12637 describe Diagnósticos como un módulo nuevo **dentro** de FLIT 2.0. La decisión de arquitectura de la suite lo eleva a **producto independiente**, con despliegue y subdominio propios en `diagnostico.flitsas.online`.

Esta épica cubre todo el trabajo que hace posible ese cambio y que ninguna de las nueve épicas originales contempla: el host, la identidad, la suscripción, los roles del producto, la configuración por compañía, la puerta única hacia las consultas externas y el mecanismo de salto entre subdominios.

No modifica el comportamiento funcional de MOD01 a MOD07. Es la infraestructura sobre la que corren. Ninguno de esos siete módulos puede entregarse sin esta épica cerrada.

### Precondición de arranque

La puerta de salida de la plataforma debe estar cumplida y **verificada en QA**:

- Token validado en gateway y API.
- Entrada por el hub y cambio de producto sin otro login.
- Alta y suspensión de suscripción operativas.
- Roles por producto.
- Marca Blanca.
- Producto de prueba levantado con la plantilla.
- Una consulta externa ejecutada con consumo medido.

Mientras esa puerta no esté cumplida, esta épica no arranca, y por lo tanto ninguna otra del módulo.

### Alcance

#### 1. Host y despliegue

- Producto desplegado en `diagnostico.flitsas.online`, sobre la plantilla `flit-product`.
- Versionado y despliegue independientes de `tramites.flitsas.online`.
- Barra y menú común de la suite vía `@flit/shell`.
- Sistema de diseño vía `@flit/ui`, respetando los colores corporativos: azul `#557eff`, naranja `#ff4e00`, verde `#8cc63f`.

#### 2. Identidad y sesión

- `@flit/auth` valida el token del producto en el gateway y en la API.
- El token transporta: compañía activa, usuario, rol del producto y estado de la suscripción.
- Sin sesión, el subdominio redirige al login del hub. **Diagnóstico no tiene formulario de login propio.**
- Cerrar sesión es global: revoca la sesión de toda la suite.
- El cambio de compañía activa lo resuelve el hub. Al volver, Diagnóstico trabaja con la compañía del token.

#### 3. Suscripción del producto

La puerta del producto es la suscripción, no una bandera dentro de Trámites.

| Suscripción | Menú del hub | `diagnostico.flitsas.online` |
|---|---|---|
| Suspendida o inexistente | No muestra Diagnóstico | No abre el home |
| Activa | Abre el producto sin otro login | Entra con el token del producto |

- El Super Admin activa o suspende Diagnóstico por empresa, desde el hub.
- Cada request comprueba **primero** la suscripción de la compañía activa y **después** el rol para la acción concreta.
- El efecto de activar o suspender se nota en minutos, sin volver a iniciar sesión.
- Los diagnósticos ya guardados sobreviven a la suspensión: siguen referenciando ese `empresa_id`.

#### 4. Roles del producto

- El hub asigna el rol de Diagnóstico por empresa y por usuario, con `product_code` propio.
- El rol de Diagnóstico **no se hereda** del rol de Trámites.
- Diagnóstico no define ni administra el directorio de roles; solo consume la asignación.

Mapa con los roles nombrados en la épica 12637:

| Rol en 12637 | Rol en la suite |
|---|---|
| Admin FLIT (Backoffice) | Admin del producto Diagnóstico |
| Usuario Cliente (Operador/Analista) | Usuario del producto Diagnóstico |

| Capacidad | Usuario del producto | Admin del producto |
|---|---|---|
| Entrar al subdominio | Con suscripción activa y rol de Diagnóstico | Con suscripción activa y rol de backoffice del producto |
| Editar la configuración del producto de la compañía activa | Según el permiso de su rol | Sí |
| Editar usuarios, roles, suscripción o la ficha general de la compañía | No. Se hace en el hub | No. Se hace en el hub |
| Crear diagnóstico y cargar vehículos | Sí, en su compañía | Sí |
| Ver dashboard y descargar reportes | Sí, en su compañía | Sí, entre compañías con suscripción activa |
| Reprocesar y llenar datos | No | Sí |
| Override del dictamen | No | Sí |
| Ver origen manual, override y volumetría de facturación | No | Sí |

Los nombres internos de estos roles se cierran en el contrato de productos del hub. Esta tabla fija las capacidades, no la nomenclatura.

#### 5. Configuración del producto por compañía

- Pantalla propia donde se edita **solo** la configuración de Diagnóstico de la compañía activa.
- Usuarios, roles, suscripción, datos generales de la compañía y configuración de Trámites se abren y se guardan en el hub. La API de Diagnóstico **rechaza** esos cambios.
- El catálogo de compañías es el del hub. Diagnóstico no guarda copia ni ofrece alta de compañías.
- Si un PDF o un listado necesita el nombre de la compañía o del usuario, se resuelve en el momento contra el hub.
- Los campos concretos de esta configuración los define el PO antes de la construcción.

#### 6. Puerta única hacia las consultas externas

- Toda consulta a RUNT, SIMIT, avalúo e impuesto departamental sale por la **API de consultas de la plataforma**.
- El producto no guarda credenciales de proveedores ni integra adaptadores propios.
- La respuesta llega normalizada e indica qué servicio la resolvió.
- El consumo queda **medido por empresa y por producto**.
- Caminos que deben quedar registrados en ese contrato antes de MOD03: Verifik, Kyverum, Intempo, Smartcloud, Fasecolda y el scraping de impuesto departamental.

#### 7. Mecanismo de salto entre subdominios

Habilitador de MOD06. El salto a Trámites ya no es una navegación interna.

- Diagnóstico entrega un **contexto firmado y de un solo uso**.
- El destino, en `tramites.flitsas.online`, lo canjea con su propio token y precarga el formulario.
- El mecanismo pertenece a esta épica; el **contrato de campos** lo cierran los equipos de trámites y de saneamiento (ver ajustes a 12679).

### Criterios de aceptación

1. Con la suscripción suspendida, el hub no muestra Diagnóstico y el subdominio no abre el home, aunque exista una sesión válida de la suite.
2. Con la suscripción activa, el menú del hub abre el producto sin pedir un segundo login.
3. Sin sesión, `diagnostico.flitsas.online` redirige al login del hub y no renderiza ningún formulario de credenciales propio.
4. Cerrar sesión desde cualquier producto de la suite revoca también la sesión de Diagnóstico.
5. Activar o suspender la suscripción se refleja en minutos, sin que el usuario deba volver a iniciar sesión.
6. La API de Diagnóstico rechaza cualquier intento de crear o modificar compañías, usuarios, roles o suscripciones, venga de la pantalla o de una llamada directa.
7. Desde la pantalla de configuración se edita únicamente la configuración de Diagnóstico de la compañía activa.
8. Un usuario con rol de Trámites pero sin rol de Diagnóstico no entra al producto, aunque su compañía tenga la suscripción activa.
9. Ninguna consulta externa sale del producto sin pasar por la API de consultas de la plataforma, y cada una queda contabilizada contra la empresa y el producto.
10. El contexto de salto a Trámites caduca tras un único canje; un segundo intento con el mismo contexto es rechazado.
11. Suspender la suscripción no borra ni desvincula los diagnósticos ya guardados de esa empresa.

### Fuera de alcance

- Construir el hub, el servidor OIDC o los paquetes `@flit/auth`, `@flit/ui` y `@flit/shell`. Se consumen, no se construyen.
- Alta de compañías y de usuarios.
- Los adaptadores de Verifik, Kyverum, Intempo, Smartcloud, Fasecolda y el scraping. Viven en el módulo de consultas de la plataforma.
- Definición de tarifas y cobro.
- El comportamiento funcional de MOD01 a MOD07, cubierto por las épicas 12640 a 12680.

### Entregable demostrable

Corresponde a la **Fase 0** del plan de entrega de la propuesta: con suscripción activa el menú del hub entra sin otro login; sin suscripción el producto no abre; sin sesión redirige al hub.

---

# Parte 2 — Criterios a agregar a épicas existentes

## 12638 — Diseño y Experiencia Visual

| # | Criterio a agregar | Razón |
|---|---|---|
| 1 | Se diseña una décima pantalla: **Configuración del producto de la compañía activa**. No contiene campos de usuarios, roles ni suscripción; esos viven en el hub. | 12638 describe nueve pantallas. La propuesta necesita diez. El comportamiento de esta pantalla pertenece a la épica nueva; su diseño, a 12638. |

## 12640 — MOD01 Parametrización

| # | Criterio a agregar | Razón |
|---|---|---|
| 1 | En Diagnóstico Express el nombre del diagnóstico se **autogenera** a partir del identificador y la fecha, y queda editable. El campo sigue siendo obligatorio. | 12640 exige nombre obligatorio; 12637 define el Express como modalidad sin ceremonia de lote. Sin autogeneración, las dos reglas chocan y el Express deja de ser express. |
| 2 | Los checkboxes de alcance se presentan en los tres grupos jerárquicos (Por Vehículo, Por Propietario, Por Locatario). Propietario y Locatario dependen del RUNT y no pueden marcarse como fuente independiente. | La agrupación está en 12638 pero no en la regla de negocio de 12640. La dependencia con RUNT no está escrita en ninguna de las dos. |

## 12641 — MOD02 Captura e Ingesta

| # | Criterio a agregar | Razón |
|---|---|---|
| 1 | Los identificadores se normalizan a mayúsculas y se les quitan los espacios **antes** de clasificarlos como placa o VIN. | Sin esto, `abc 123` se rechaza como inválido y nunca se consulta. No está en la épica. |
| 2 | Los duplicados dentro del mismo archivo se consolidan en una sola fila y el duplicado se reporta en el Resumen de Validación. | 12641 no define el caso. Sin regla, la misma placa se consulta dos veces y se factura dos veces. |
| 3 | El Diagnóstico Express persiste como un diagnóstico de **un** vehículo. La expresión «sin creación de lote» de 12637 aplica a la experiencia del usuario, no al modelo de datos. | Evita que MOD03 a MOD07 necesiten un segundo camino de código para el caso unitario. |

## 12659 — MOD03 Motor de Consultas

> ⚠ El criterio 1 **corrige** el texto actual de la épica.

| # | Criterio a agregar | Razón |
|---|---|---|
| 1 | El enum de fuentes del log de auditoría es: `RUNT`, `SIMIT_VEHICULO`, `SIMIT_PROPIETARIO`, `SIMIT_LOCATARIO`, `AVALUO`, `IMPUESTO_DEPARTAMENTAL`. **`FASECOLDA` sale del enum de fuentes.** | 12659 lista `FASECOLDA` como fuente. Fasecolda es un proveedor, no una fuente. Si el proveedor de avalúo cambia, la celda del semáforo, el JSON consolidado y el PDF no deben cambiar. |
| 2 | El log de auditoría incorpora el campo **`servicio`**, con valores `VERIFIK`, `KYVERUM`, `INTEMPO`, `SMARTCLOUD`, `FASECOLDA`, `SCRAPING`. Es nulo en el llenado manual. | Sin este campo no se puede cumplir la métrica de «fallos por proveedor» que pide 12680. |
| 3 | Toda consulta externa sale por la API de consultas de la plataforma. El módulo no integra proveedores ni almacena sus credenciales. | 12659 asume integración directa con las fuentes. La arquitectura de la suite lo prohíbe. |
| 4 | El consumo de cada consulta queda medido por empresa y por producto. | Insumo de facturación que 12659 no contempla. |
| 5 | El worker es **idempotente por `(vehiculo_id, fuente)`**: un segundo mensaje no dispara otra llamada si la celda ya está en `COMPLETADO` o `NA`. | 12659 no define concurrencia. Sin esto, dos disparos sobre la misma placa generan doble consumo. |
| 6 | El llenado manual escribe registro de auditoría igual que la consulta automática, sin `transaccion_proveedor_id`. | 12659 registra solo los intentos de API. El manual también debe ser auditable. |

## 12669 — MOD04 Dashboard y Monitoreo

| # | Criterio a agregar | Razón |
|---|---|---|
| 1 | La restricción del Usuario Cliente se aplica **también en la API**, no solo ocultando botones. Una llamada directa a reprocesar o llenar datos con rol de cliente es rechazada. | 12669 describe la restricción como visual. Ocultar el botón no es un control de acceso. |
| 2 | El estado global del lote se **calcula**; no existe acción de usuario que lo edite. | 12669 define las condiciones pero no prohíbe la edición manual del estado. |

## 12679 — MOD06 Derivación y Accionabilidad

> ⚠ El criterio 1 **cambia** el texto actual de la épica.

| # | Criterio a agregar | Razón |
|---|---|---|
| 1 | El botón **Crear Trámite** se habilita **por vehículo**, en cuanto ese vehículo cierra todas sus consultas aplicables. No espera a que el lote completo esté en verde. | 12679 lo condiciona a que «el diagnóstico esté completo». Contradice el principio de 12637 y 12678, donde el dictamen sí es por vehículo. Un lote de 200 placas no debe retener la derivación de las 199 que ya cerraron por una en error. |
| 2 | En modo Con Análisis con veredicto **Bloqueado**, el botón Crear Trámite **no se ofrece**. Solo se ofrece *Sanear*. | 12679 lo deja implícito al enumerar los dos escenarios habilitados. Conviene que sea explícito y verificable. |
| 3 | El salto a Trámites viaja como **contexto firmado y de un solo uso** entre subdominios, canjeado en destino con el token de Trámites. | 12679 asume una redirección interna dentro de la misma aplicación. Ya no lo es. El mecanismo lo aporta la épica nueva. |
| 4 | Hacia saneamiento, el **monto de la deuda y su fuente** (SIMIT o impuesto departamental) viajan juntos. | 12679 dice «inyectando el valor exacto de la deuda» sin nombrar de dónde salió. Sin la fuente, el gestor de saneamiento no sabe qué está saneando. |
| 5 | El contrato de campos de entrada del wizard y del saneamiento se cierra **antes** de construir MOD06. | 12679 no fija esta dependencia. Sin contrato, MOD06 no puede arrancar. |

## 12680 — MOD07 Reportes y Trazabilidad

| # | Criterio a agregar | Razón |
|---|---|---|
| 1 | El dashboard de Admin reporta los **trámites originados en diagnósticos**: conteo de trámites con `origen_diagnostico_id`, por cliente y por tipo de trámite, con la tasa de conversión sobre los vehículos diagnosticados. | 12679 anuncia este reporte gerencial como propósito de la trazabilidad, pero 12680 no lo lista como entregable. Queda huérfano entre las dos épicas. |
| 2 | MOD07 puede emitir **evidencia parcial** de lo ya completado, sin esperar al cierre del lote. | 12680 asume que el reporte se genera con el lote cerrado. Un lote `INCOMPLETO` también debe poder entregar evidencia. |
| 3 | Los archivos se generan bajo demanda y se asocian al `diagnostico_id` para no regenerarlos si la evidencia no cambió. Un recálculo de dictamen o un llenado manual **invalida** el PDF de ese vehículo. | 12680 no define ciclo de vida de los archivos. Sin invalidación, el cliente descarga un PDF que contradice el dictamen vigente. |
| 4 | Todo listado y detalle filtra por la compañía activa del token cuando el rol es de cliente. El filtro solo puede quedar vacío en el tablero de facturación del Admin. | 12680 describe los filtros como funcionalidad, no como aislamiento de datos entre empresas. |

---

# Parte 3 — Resumen de impacto

| Épica | Acción | Ítems |
|---|---|---|
| **Nueva (MOD00)** | Crear | Host, identidad, suscripción, roles, configuración por compañía, puerta de consultas, salto entre subdominios. 11 criterios de aceptación |
| 12637 | Sin cambios | La divergencia de encuadre queda declarada en §1 de la propuesta |
| 12638 | Ampliar | 1 criterio |
| 12640 | Ampliar | 2 criterios |
| 12641 | Ampliar | 3 criterios |
| 12659 | **Corregir** + ampliar | 6 criterios, uno de ellos corrige el enum de fuentes |
| 12669 | Ampliar | 2 criterios |
| 12678 | Sin cambios | Cubierta 1:1 por la propuesta |
| 12679 | **Cambiar** + ampliar | 5 criterios, uno de ellos cambia cuándo aparece *Crear Trámite* |
| 12680 | Ampliar | 4 criterios |

**Total:** 1 épica nueva y 23 criterios repartidos en 6 épicas existentes. Dos de esos criterios (12659 #1 y 12679 #1) no son adiciones sino correcciones del texto vigente, y deben revisarse con el PO antes de editarlas en Azure DevOps.

---

*Documento derivado del contraste entre las épicas 12637–12680 del proyecto FLIT - EVOLUTION y la propuesta de implementación del Aplicativo Diagnóstico Vehicular.*
