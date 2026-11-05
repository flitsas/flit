# Resumen de producto — Diagnóstico Vehicular

> Lectura: 2026-10-30. Fuente: proyecto Azure DevOps `FLIT - DIAGNOSTICO`, área `FLIT - DIAGNOSTICO`.
> Alcance de la lectura: épicas en estado **Active**. Se leyeron título, descripción y el campo Acceptance Criteria.
> El campo Acceptance Criteria está vacío en las nueve. Los criterios viven en la descripción.
> Donde una épica trae la sección «Ajustes 2026-10 (plan v2)», esa sección prevalece sobre el texto anterior.
> No se leyeron Features ni Historias de Usuario. Este documento describe el producto comprometido por las épicas activas, no el código ya construido.

## Qué es

Diagnóstico Vehicular es un producto de la Suite FLIT, con subdominio propio (`diagnostico.flitsas.online`) y despliegue independiente de Trámites. Una empresa consulta fuentes externas sobre los vehículos de su flota y obtiene un reporte o un dictamen orientado a un trámite.

Las fuentes son RUNT, SIMIT, avalúo e impuesto departamental. El vehículo (placa o VIN) es la unidad de trabajo. A partir del RUNT se obtiene el documento del propietario. El documento del locatario lo declara el cliente en la captura; si no lo declara, las consultas del locatario quedan en N/A.

Identidad, compañías, usuarios, roles y suscripción viven en el hub. Diagnóstico no los crea ni los edita. La puerta del producto es la suscripción, no una bandera dentro de Trámites. Sin sesión, el subdominio redirige al login del hub. Una sesión es una compañía.

Opera en dos modos:

- **Simple.** Extrae y presenta la información de las fuentes elegidas, sin dictamen.
- **Con Análisis IA.** Además emite un veredicto (Viable o Bloqueado) para un tipo de trámite. El veredicto lo da un motor de reglas versionado. La IA redacta y explica siempre el dictamen. Si la IA no responde, el dictamen se entrega con redacción de plantilla y se completa después. Primera entrega: familia TRASPASO (TRASPASO, TRASPASO UNILATERAL y TRANSFERENCIA DE DOMINIO).

El diagnóstico avanza por vehículo: cada uno pasa a dictamen y a derivación cuando cierran sus consultas, sin esperar al lote. Un vehículo origina como máximo un trámite. Sanear, mientras no exista el módulo de saneamiento, es una salida informativa con la deuda por fuente.

## Qué hace

1. El hub muestra Diagnóstico solo si la compañía tiene la suscripción activa. El usuario entra sin un segundo login.
2. Define la cabecera del diagnóstico: nombre, fuentes a consultar y modo. Esa cabecera aplica igual a todos los vehículos del lote. Al guardarla, el diagnóstico queda en estado **CREADO**. Después de salir de CREADO, la cabecera no se modifica.
3. Carga vehículos por Diagnóstico Express (uno) o por archivo (.xlsx, .xls, .csv). El sistema distingue placa y VIN, descarta identificadores inválidos y no dispara consultas externas con datos inválidos.
4. El motor consulta primero el RUNT de cada vehículo. Si el RUNT falla, no consulta ninguna otra fuente de ese vehículo. Con RUNT exitoso, dispara en paralelo las fuentes elegidas que apliquen.
5. El usuario ve el avance en una grilla de semáforos que se actualiza sola.
6. En Con Análisis IA, al cerrar las consultas de un vehículo el motor de reglas dictamina y la IA redacta. En Simple no hay dictamen.
7. El usuario descarga evidencia (PDF, Excel, ZIP) y, cuando el vehículo ya cerró, puede derivar a trámite o ver la deuda para sanear.
8. El backoffice FLIT audita origen de los datos, volumetría y conversión a trámites.

## Funcionalidades que el alcance activo compromete

### Acceso, sesión y suscripción

- Producto en `diagnostico.flitsas.online`, sobre la plantilla de Comparendos, con barra y menú de `@flit/shell` y sistema de diseño de `@flit/ui` (azul `#557eff`, naranja `#ff4e00`, verde `#8cc63f`), más la biblioteca de iconos y el loader del UI kit de Diseño.
- El token lleva compañía activa, usuario, rol del producto y estado de la suscripción.
- Sin sesión no hay formulario de credenciales propio: redirige al login del hub y, al autenticarse, regresa a Diagnóstico.
- Cerrar sesión es global: revoca la sesión de toda la suite.
- Suscripción suspendida o inexistente: el hub no muestra Diagnóstico y el subdominio no abre el home, aunque haya sesión. El efecto de activar o suspender se nota en minutos, sin volver a iniciar sesión. Los diagnósticos ya guardados sobreviven a la suspensión.
- Cada petición comprueba primero la suscripción y después el rol. Mientras el hub no valide la suscripción de forma central, el producto aplica su propio guard por petición.
- Un rol de Trámites no da acceso a Diagnóstico. `AdminCompany` recibe `admin_diagnostico` por diseño; si la compañía no tiene el producto encendido, ese rol no abre el producto.
- Backoffice solo opera en compañías con `tenant_type=FLIT`. En cualquier otra, las rutas de backoffice responden 403.

### Configuración del producto por compañía

Pantalla propia, solo de Diagnóstico, con dos pestañas:

| Nivel | Quién edita | Qué contiene |
|---|---|---|
| Contrato | Solo backoffice FLIT. La compañía la ve en solo lectura | Fuentes habilitadas y límite de vehículos por lote |
| Operación | Admin de la compañía y backoffice FLIT | Visibilidad de diagnósticos (por defecto, toda la compañía), fuentes por defecto (subconjunto de las habilitadas) y estimado de consultas (no mostrar, antes de lanzar, o al finalizar; por defecto, no mostrar) |

Usuarios, roles, suscripción, ficha de la compañía y configuración de Trámites se abren y se guardan en el hub. La API de Diagnóstico rechaza esos cambios. Diagnóstico no guarda copia del catálogo de compañías ni ofrece alta.

### Parametrización del diagnóstico

Formulario en tres bloques: información general, alcance y modo.

- Nombre obligatorio. En Express se autogenera desde el identificador y la fecha, y queda editable.
- Alcance en tres grupos: por vehículo, por propietario y por locatario. Solo se ofrecen las fuentes habilitadas para la compañía y se premarcan las fuentes por defecto.
- RUNT del vehículo va bloqueado en activo. Propietario y locatario no son fuentes independientes: el propietario depende del RUNT y el locatario del documento declarado en la captura.
- Por propietario y por locatario se ofrecen RUNT persona y SIMIT, cada una en su celda.
- Modo Simple o Con Análisis IA. En Con Análisis IA el tipo de trámite es obligatorio. En esta etapa el catálogo ofrece la familia TRASPASO. Registro y levantamiento de prenda entran en la siguiente versión del catálogo; cancelación de matrícula, cuando el wizard la habilite.
- En Con Análisis IA, las fuentes que exige el trámite quedan marcadas y bloqueadas. Para la familia TRASPASO: RUNT vehículo, SIMIT vehículo, RUNT propietario y SIMIT propietario. El resto sigue a la carta.
- Un lote no mezcla modos ni tipos de trámite. Si hacen falta dos trámites, son dos diagnósticos.
- Si la compañía lo configuró, el resumen muestra antes de lanzar el estimado de consultas (vehículos válidos por fuentes elegidas, menos las que quedarán en N/A).

### Captura de vehículos

Dos vías en esta épica. La vía FLITO queda fuera: es un sistema externo y entra después, por API de integración (ola D9).

- **Express.** Un identificador desde la pantalla principal, panel compacto de alcance y modo, y paso directo al tablero. Se persiste como un diagnóstico de un vehículo.
- **Carga masiva.** Plantilla .xlsx con columna obligatoria `Identificador_Vehiculo` y columna opcional `Documento_Locatario` (tipo y número). Acepta .xlsx, .xls y .csv. El documento del locatario se guarda cifrado y marcado como declarado por el cliente. Express tiene el mismo campo opcional.
- Normalización a mayúsculas y sin espacios. Duplicados del mismo archivo se consolidan en una fila y se informan en el resumen.
- Detección de placa: carro o público (`ABC123`), moto (`ABC12D` y `ABC12`), remolque o semirremolque (`R` o `S` más 5 o 6 números) y maquinaria (2 letras y 6 números). VIN: 17 caracteres ISO 3779, sin I, O ni Q. Lo que no calza es inválido. El RUNT valida en definitiva.
- Resumen de validación si hay filas inválidas: corregir ahora o continuar solo con los válidos. Si no hay ningún válido, no se continúa. Un identificador inválido nunca dispara una consulta externa.
- El lote no supera el límite de vehículos de la compañía. El resumen lo informa antes de encolar.
- Los vehículos válidos quedan asociados al diagnóstico y entran a cola asíncrona. El RUNT es siempre la primera consulta.

### Motor de consultas

Toda consulta externa sale por la API de consultas de la plataforma. El producto no guarda credenciales de proveedores ni integra adaptadores. La respuesta llega normalizada e indica qué servicio la resolvió. El consumo se mide por empresa y por producto.

Orden:

1. RUNT por placa, sin documento (lo resuelve Intempo). De esa respuesta salen la placa definitiva y el documento del propietario. Por VIN se sigue la cadena normal. Si el RUNT del vehículo falla, ninguna otra fuente de ese vehículo se consulta.
2. En paralelo, según la cabecera: SIMIT vehículo, RUNT persona y SIMIT del propietario, avalúo, impuesto departamental, y —solo si el cliente declaró el documento— RUNT y SIMIT del locatario.

Reglas de N/A y de fallo parcial:

- RUNT persona del propietario, si falla, no bloquea al vehículo.
- Sin documento de locatario: RUNT y SIMIT del locatario en N/A, motivo «sin documento de locatario», sin consumo. Cuando un proveedor entregue el locatario por placa, el dato se toma de ahí.
- Impuesto: se ofrece solo en departamentos con consulta automatizada (lo decide la plataforma de consultas). El departamento se deriva del organismo de tránsito del RUNT. Vehículo de servicio público, o de un departamento sin consulta: N/A («departamento no soportado»), no Error.
- Una celda en N/A muestra el motivo (servicio público, sin documento de locatario, departamento no soportado, sin proveedor).
- Si una consulta queda en Error, las que ya están en Completado no se repiten.
- El motor es idempotente por vehículo y fuente: un segundo disparo no repite una consulta ya en Completado o N/A.
- El trabajo encolado sobrevive a un reinicio y a dos réplicas sin doble consumo. Una consulta tomada y no terminada se retoma al vencer su plazo.
- Al cerrar todas las consultas aplicables de un vehículo en modo Con Análisis IA, se dispara el dictamen de ese vehículo, sin esperar al lote.

Cada intento queda en log: diagnóstico, identificador, fuente, servicio que resolvió, inicio y fin, estado, mensaje de error e id de transacción del proveedor. Fuentes del log: `RUNT`, `RUNT_PROPIETARIO`, `RUNT_LOCATARIO`, `SIMIT_VEHICULO`, `SIMIT_PROPIETARIO`, `SIMIT_LOCATARIO`, `AVALUO`, `IMPUESTO_DEPARTAMENTAL`. El servicio (Verifik, Kyverum, Intempo, Smartcloud, Fasecolda, scraping, entre otros) es nulo en el llenado manual. Cada reproceso queda con disparador `REPROCESO` y el usuario que lo lanzó, y solo repite la consulta real.

### Tablero de monitoreo

Grilla: filas de vehículos y columnas de las consultas elegidas. Cada celda es un círculo: gris en proceso, verde completado, rojo error, gris claro N/A. Se actualizan solo las celdas que cambiaron, sin recargar la página, con no más de 3 segundos de retraso.

Estado global, calculado, no editable a mano:

| Estado | Cuándo |
|---|---|
| En Proceso | Hay al menos una consulta en Pendiente o Error |
| Completado | Todas las consultas aplicables de todos los vehículos están en verde |
| Incompleto | El usuario cerró el panel con consultas en Error sin reprocesarlas y puede seguir con la información disponible |

Un diagnóstico Completado o Incompleto vuelve a En Proceso si se reprocesa o se llena una consulta, y se recalcula al terminar. Cada cambio de estado queda en el historial con disparador y usuario. El panel no se cierra solo.

Al hacer clic en una celda, cualquier usuario con acceso al diagnóstico ve los campos extraídos. Los documentos de persona salen enmascarados. No se muestra la respuesta cruda del proveedor ni si la celda se llenó a mano. Ninguna lectura de la grilla devuelve datos de otra compañía.

### Dictamen, derivación y saneamiento

Estas capacidades están descritas en las épicas activas de visión, diseño, motor, tablero, reportes y paraguas. Las épicas dedicadas de dictamen y de derivación no están en estado Active, así que su texto propio no forma parte de esta lectura.

- Veredicto: Viable o Bloqueado (requiere saneamiento). Lo da el motor de reglas. La IA redacta y explica. La base de conocimiento del motor queda como ADR del líder técnico antes de construir el dictamen.
- Si una consulta en Error se reprocesa con éxito después del dictamen, el dictamen se recalcula y reemplaza al anterior.
- El panel muestra el veredicto y los alertamientos en lenguaje humano.
- Solo el backoffice FLIT llena datos a mano, hace override del dictamen y edita alertamientos. El override pide confirmación. Ningún usuario de la compañía cliente puede llenar datos ni cambiar el dictamen, ni por pantalla ni por API. El admin de la compañía recibe 403 en esas acciones.
- El origen manual se registra (`MANUAL` y usuario) y no se muestra al cliente: ni en la grilla, ni en el detalle de la celda, ni en el PDF.
- **Crear trámite** aparece por fila en cuanto ese vehículo cierra sus consultas. En Simple abre la elección del tipo de trámite. En Con Análisis IA con veredicto Viable confirma el trámite analizado. El salto a Trámites usa un contexto de un solo uso: token opaco de 256 bits, guardado con hash, TTL de 10 minutos, ligado a compañía, usuario y tipo de trámite. Trámites lo canjea una sola vez y, con él, lee el RUNT del vehículo en Diagnóstico y no lo vuelve a consultar si tiene menos de 24 horas. El destino precarga el formulario en `tramites.flitsas.online`. La fila queda etiquetada con el trámite derivado.
- **Sanear** aparece con veredicto Bloqueado, o en Simple cuando el vehículo tiene deuda. Abre un panel con la deuda por fuente (SIMIT vehículo, SIMIT propietario, SIMIT locatario, impuesto), cada una con su monto, y permite exportarlo. No crea todavía un proceso de saneamiento.

El modo Simple se libera con RUNT, SIMIT y avalúo. Con Análisis IA se libera con dictamen y derivación. El backoffice FLIT no crea diagnósticos: los crea un usuario de la compañía que paga el producto.

### Reportes y trazabilidad

Listado de la compañía: id, nombre, fecha, tipo (Simple o Con Análisis), trámite analizado si aplica, estado y usuario que lo creó. Filtros: rango de fechas, tipo, estado y nombre. Por defecto cada usuario ve todos los diagnósticos de su compañía; el admin puede restringirlo a los propios. Una empresa padre ve en solo lectura los de sus hijas; una hija no ve al padre ni a las hermanas. Esa visibilidad de grupo entra en la ola D8, después de liberar Con Análisis IA.

Entregables, también parciales, sin esperar al cierre del lote:

- PDF por vehículo, formato corporativo, solo con las secciones de las fuentes elegidas, sin huecos. Encabezado obligatorio: id, usuario, empresa, fecha y hora, y tipo de análisis (por ejemplo, Diagnóstico Con Análisis IA — TRASPASO).
- Excel del lote: una fila por vehículo y columnas según la parametrización.
- ZIP con los PDF del lote, descarga directa en el navegador.

Los archivos se generan bajo demanda y no se regeneran si la evidencia no cambió. Un recálculo de dictamen o un llenado manual invalida el PDF de ese vehículo. Si la compañía lo configuró, al finalizar se muestran las consultas consumidas por fuente.

Se conserva el JSON original de la cabecera, el origen de cada dato (API o MANUAL) y el usuario de cada acción relevante (creación, llenado, override). El origen MANUAL es visible para el backoffice en base de datos y oculto en el PDF del cliente.

El tablero de facturación y control es del backoffice FLIT: filtros por cliente, fechas, tipo de diagnóstico y tipo de trámite; vehículos procesados; agrupación por tipo de análisis; reprocesos o fallos por proveedor; trámites originados y tasa de conversión. Solo ese tablero puede ver varias compañías, y cada lectura queda auditada.

## Roles

| Capacidad | Usuario | Admin de la compañía | Backoffice FLIT |
|---|---|---|---|
| Crear diagnóstico y cargar vehículos | Sí, en su compañía | Sí, en su compañía | No |
| Ver tablero y descargar reportes | Sí, según la visibilidad configurada | Sí, todo lo de su compañía | Sí, entre compañías, con auditoría |
| Reprocesar celdas en error | No | Sí | Sí |
| Llenar datos a mano y override del dictamen | No | No | Sí |
| Editar configuración operativa | No | Sí | Sí |
| Editar configuración contractual | No | No | Sí |
| Ver origen manual, override, auditoría y volumetría | No | No | Sí |
| Editar usuarios, roles, suscripción o la ficha de la compañía | No. Se hace en el hub | No. Se hace en el hub | No. Se hace en el hub |

La restricción del usuario también aplica en la API: reprocesar o llenar datos sin permiso se rechaza.

## Criterios de aceptación vigentes

Consolidados desde las descripciones de las épicas activas. El campo formal de Acceptance Criteria no aporta ninguno.

**Producto y acceso**

- Con la suscripción suspendida, el hub no muestra Diagnóstico y el subdominio no abre el home, aunque exista sesión.
- Con la suscripción activa, el menú del hub abre el producto sin un segundo login.
- Sin sesión, el subdominio redirige al login del hub y no renderiza un formulario de credenciales propio.
- Cerrar sesión desde cualquier producto de la suite revoca también la sesión de Diagnóstico.
- Activar o suspender la suscripción se refleja en minutos, sin volver a iniciar sesión, y no borra ni desvincula los diagnósticos ya guardados.
- La API rechaza crear o modificar compañías, usuarios, roles o suscripciones.
- Desde la pantalla de configuración solo se edita la configuración de Diagnóstico de la compañía activa.
- Un usuario con rol de Trámites y sin rol de Diagnóstico no entra, aunque la compañía tenga la suscripción activa.
- El admin de la compañía no llena datos, no hace override y no edita el contrato: la API responde 403.
- Un backoffice en una compañía que no es FLIT recibe 403 en toda ruta de backoffice.
- Las fuentes por defecto son siempre un subconjunto de las fuentes habilitadas.
- Ninguna consulta externa sale sin pasar por la API de consultas de la plataforma, y cada una queda contabilizada contra la empresa y el producto.
- El contexto de salto a Trámites caduca tras un único canje. Un segundo intento con el mismo contexto se rechaza.

**Operación del diagnóstico**

- Un identificador inválido nunca dispara una consulta externa.
- Si el RUNT de un vehículo falla, ninguna otra fuente de ese vehículo se consulta.
- Ningún usuario de la compañía puede llenar datos a mano ni cambiar el dictamen, ni por API ni por pantalla.
- El modo Simple se libera con RUNT, SIMIT y avalúo. Con Análisis IA se libera con dictamen y derivación.
- El backoffice FLIT no crea diagnósticos.
- El estado global del lote se calcula. Un lote Completado o Incompleto vuelve a En Proceso al reprocesar o llenar una consulta.
- El reproceso solo repite la consulta real y queda registrado con el usuario que lo lanzó.
- La grilla no revela el origen manual y no devuelve datos de otra compañía.
- Al clic en el semáforo se ven los campos extraídos, con documentos de persona enmascarados y sin la respuesta cruda del proveedor.

## Orden de entrega declarado en la épica paraguas

D0 esqueleto · D1 parametrización y captura · D2 RUNT y tablero · D3 SIMIT, avalúo y reproceso · D4 evidencias (libera Simple) · D5 locatario e impuesto por departamento · D6 dictamen · D7 derivación (libera Con Análisis IA) · D8 admin · D9 FLITO.

## Épicas activas leídas

| ID | Título | Qué aporta al producto |
|---|---|---|
| 12881 | [DIAGNOSTICO] - Aplicativo Diagnóstico Vehicular | Paraguas: qué se construye, decisiones vigentes, olas y criterios globales |
| 13714 | [0] Habilitación en la Suite FLIT | Host, identidad, suscripción, roles, configuración, puerta de consultas y salto a Trámites |
| 12637 | Visión general y arquitectura | Propósito, modos, flujo por vehículo, estados, roles y recálculo del dictamen |
| 12638 | Diseño y experiencia visual | Pantallas, semáforos, tres perfiles de vista, dictamen, derivación, listado y configuración |
| 12640 | [1] Parametrización y reglas | Cabecera, alcance a la carta, modo y reglas que congelan la configuración |
| 12641 | [2] Captura e ingesta | Express, carga masiva, detección de placa y VIN, locatario declarado y tope del lote |
| 12659 | [3] Motor de consultas y orquestación | Orden RUNT primero, paralelismo, N/A, idempotencia, log y cola durable |
| 12669 | [4] Dashboard y monitoreo | Grilla en tiempo real, estado global y acciones según rol |
| 12680 | [7] Reportes y trazabilidad | Listado, PDF, Excel, ZIP, evidencia parcial y tablero de facturación del backoffice |

Iteración: 12637, 12638, 12640 y 12641 están en `FLIT - DIAGNOSTICO\Sprint 1`. El resto está en la raíz del proyecto.

## Fuera de esta lectura

- Features e Historias de Usuario: no se consultaron.
- Épicas que el paraguas nombra y que no estaban Active el 2026-10-30: dictamen ([5]) y derivación ([6]). Su comportamiento aparece aquí solo en la medida en que las épicas activas lo describen.
- Construcción del hub, del servidor OIDC y de los paquetes `@flit/auth`, `@flit/ui` y `@flit/shell`: se consumen.
- Adaptadores de proveedores y definición de tarifas: viven fuera del producto.
- API de integración FLITO: después del producto (ola D9).
