# Propuesta de implementación — Aplicativo Diagnóstico Vehicular

**Proyecto:** FLIT Suite  
**Fecha:** 2026-09-24  
**Fuente:** épicas 12637, 12638, 12640, 12641, 12659, 12669, 12678, 12679 y 12680, alineadas con `plan-suite-flit.md`  
**Estado:** propuesta para revisión del líder técnico y del PO  
**Arranque del producto:** después de la puerta de salida de la plataforma, en `diagnostico.flitsas.online`

---

## 1. Qué se va a construir

Un **producto de la suite FLIT**, con despliegue y subdominio propios: `diagnostico.flitsas.online`. Una empresa consulta fuentes externas sobre uno o varios vehículos de su flota y obtiene un reporte de estado o un dictamen de viabilidad para un trámite de tránsito.

El vehículo, identificado por placa o VIN, es la unidad central. A partir de él se obtienen las personas asociadas (propietario y locatario). El RUNT es siempre el primer paso: de ahí salen la placa definitiva y los documentos que habilitan el resto de consultas.

El producto no reemplaza el wizard de trámites ni el saneamiento. Esos viven en `tramites.flitsas.online`. Diagnóstico les abre ese subdominio e inyecta los datos ya recolectados para evitar una segunda consulta al RUNT.

La suite sigue el modelo de Google Workspace. El hub en `flitsas.online` es la consola: login, productos contratados y administración. Trámites, Comparendos y Diagnóstico son productos, cada uno en su subdominio y con su menú. El usuario inicia sesión una vez en el hub y cambia de producto sin volver a escribir la contraseña.

**Relación con el documento de épicas.** La épica 12637 describe Diagnósticos como un módulo nuevo *dentro* de FLIT 2.0. Esta propuesta lo eleva a producto de la suite, con subdominio propio, por la decisión de arquitectura documentada en `plan-suite-flit.md`. El alcance funcional de las nueve épicas se conserva íntegro: lo que cambia es dónde viven el login, el catálogo de compañías y la puerta de acceso, no lo que hacen MOD01 a MOD07. Donde esta propuesta se aparta del texto de una épica, queda señalado en el apartado correspondiente.

## 2. Alcance

### Incluido

- Configuración de un diagnóstico (nombre, fuentes y modo) que aplica igual a todo el lote.
- Tres vías de ingreso: express (un vehículo), carga masiva y selección desde el inventario de FLITO.
- Consulta asíncrona a RUNT, SIMIT (vehículo, propietario y locatario), avalúo e impuesto departamental.
- Dashboard en tiempo real con semáforos por vehículo y por fuente.
- Dictamen de IA (Viable o Bloqueado) solo en modo Con análisis.
- Derivación al wizard de trámites o al módulo de saneamiento.
- Reportes PDF, Excel y ZIP, más métricas de volumetría para facturación.
- Host `diagnostico.flitsas.online`, con `@flit/auth` y `@flit/shell`. El menú del hub abre el producto sin otro login.
- Suscripción del producto por empresa. El Super Admin la activa o la suspende en el hub. El administrador de la compañía asigna ahí los roles de Diagnóstico.
- Configuración propia del producto, de la compañía activa. Usuarios, roles, suscripción y datos generales de la compañía se administran en el hub.
- Orquestación de consultas del lote. La llamada a RUNT, SIMIT, avalúo e impuesto sale por la API de consultas de la plataforma, con consumo medido por empresa y producto.

### Fuera de alcance de esta propuesta

- Login, alta de compañías, suscripciones y asignación de roles. Viven en el hub. Diagnóstico no crea compañías ni usuarios.
- Construir el servidor OIDC, el hub o los paquetes `@flit/auth`, `@flit/ui` y `@flit/shell`. Se consumen cuando la puerta de salida de la plataforma está cumplida.
- Los adaptadores de Verifik, Kyverum, Intempo, Smartcloud, Fasecolda y scraping. Viven en el módulo de consultas de la plataforma. Este producto orquesta y normaliza.
- Rediseño del wizard de trámites o del saneamiento. Solo se define el salto entre subdominios y el contrato de inyección de datos.
- Definición de tarifas. El aplicativo entrega la volumetría agrupada por tipo de análisis; el cobro vive en facturación.
- Implementación concreta de la base de conocimiento del motor de IA. Queda como ADR previo al desarrollo del MOD05.

## 3. Principios de implementación

1. **La cabecera es inmutable después de arrancar.** Lo definido en la parametrización aplica al 100 % del lote. Si hace falta otro trámite, se crea otro diagnóstico.
2. **RUNT bloquea al vehículo, no al lote.** Si el RUNT de un vehículo falla, ese vehículo no continúa. Los demás siguen.
3. **El dictamen es por vehículo, no por lote.** En cuanto todas las consultas de un vehículo quedan en verde, el motor de IA puede correr sin esperar al resto.
4. **Un error no repite lo que ya salió bien.** Reprocesar afecta solo esa fuente de ese vehículo.
5. **El cliente no distingue origen API y origen manual.** La diferencia queda en base de datos para auditoría interna.
6. **MOD06 no crea formularios.** Redirige al wizard o al saneamiento del producto Trámites con el JSON consolidado.
7. **Cada intento de consulta deja log**, haya éxito o error, incluido el servicio que respondió la plataforma.
8. **Hay un solo catálogo de compañías y de usuarios.** Vive en el hub. Diagnóstico guarda `empresa_id` y `usuario_id`, y la configuración propia del producto.
9. **La suscripción abre el producto.** Suspendida, el hub no muestra Diagnóstico y el subdominio no abre el home. Activa, el menú del hub entra sin otro login. En el producto solo se edita la configuración de Diagnóstico de la compañía activa.

## 4. Plataforma: producto de la suite

Modelo de Google Workspace aplicado a la suite FLIT. La fuente de este corte es `plan-suite-flit.md`.

| Host | Qué es |
|---|---|
| `flitsas.online` | Hub: login, productos y administración |
| `tramites.flitsas.online` | Trámites, lo que hoy es FLIT 2.0 |
| `comparendos.flitsas.online` | Comparendos, después de la plataforma |
| `diagnostico.flitsas.online` | Este producto, después de la puerta de salida |

| En Workspace | En esta propuesta |
|---|---|
| Cuenta de Google y Admin Console | Hub: compañías, usuarios, suscripciones y roles por producto |
| Gmail, Drive o Docs, cada uno en su subdominio | Diagnóstico en `diagnostico.flitsas.online`, con su menú |
| El admin activa el servicio para la organización | El Super Admin activa o suspende la suscripción de Diagnóstico para la empresa |
| La app no tiene su propio directorio de cuentas | Usuarios y roles se asignan en el hub. El rol de Diagnóstico lleva `product_code` y no se hereda del rol de Trámites |
| Abrir un archivo de Drive en Docs | Saltar a trámites, saneamiento o FLITO con el token del producto destino |

```
flitsas.online  (hub)
  login OIDC · compañías · usuarios
  suscripción de Diagnóstico por empresa
  roles del producto, asignados por el administrador de la compañía
        │
        │  token propio del producto, validado en gateway y API
        │  menú de productos, si la suscripción está activa
        │  sin sesión, el login vuelve al hub
        ▼
diagnostico.flitsas.online
  configuración del producto de la compañía activa
  lotes, orquestación, dictámenes, evidencias
        │
        │  API de consultas de la plataforma (consumo medido)
        ▼
plataforma  RUNT · SIMIT · avalúo · impuesto
        │
        │  cambio de producto, sin otro login
        ▼
tramites.flitsas.online  wizard · saneamiento · inventario FLITO
```

MOD01 a MOD07 son capacidades internas de este producto, no módulos de Trámites.

```
  Aplicativo Diagnóstico Vehicular
        │
        ├── MOD01  Parametrización     → persiste cabecera
        ├── MOD02  Captura             → valida y encola vehículos
        ├── MOD03  Motor de consultas  → workers + adaptadores de fuentes
        ├── MOD04  Dashboard           → lectura en tiempo real
        ├── MOD05  Motor de IA         → dictamen por vehículo
        ├── MOD06  Derivación          → salto a wizard / saneamiento
        └── MOD07  Reportes            → PDF, Excel, ZIP, volumetría
```

Dependencia de negocio:

```
MOD01 → MOD02 → MOD03 → MOD04 → MOD05 → MOD06 → MOD07
```

MOD04 puede mostrarse desde que el lote entra en cola. MOD05 se dispara por vehículo. MOD07 puede emitir evidencia parcial de lo ya completado y el reporte definitivo cuando el lote cierra.

### Componentes nuevos

| Componente | Responsabilidad |
|---|---|
| Front y API del producto | Despliegue propio en `diagnostico.flitsas.online`, sobre la plantilla `flit-product`. Casos de uso y persistencia de diagnósticos. |
| Cliente de identidad | `@flit/auth`. Valida el token del producto. Lee compañía, usuario, rol del producto y si la suscripción está activa. Sin sesión, redirige al login del hub. |
| Cliente de consultas | Llama la API de consultas de la plataforma. No habla directo con Verifik, Kyverum, Intempo, Smartcloud, Fasecolda ni el scraping. |
| Validador de identificadores | Clasifica placa de carro, moto, remolque o VIN antes de cualquier consulta externa. |
| Cola de consultas | Un trabajo por vehículo. El worker ejecuta RUNT y luego las fuentes opcionales. |
| Adaptadores de servicio | Viven en el módulo de consultas de la plataforma, un adaptador por camino. Diagnóstico recibe la respuesta ya normalizada al modelo de la fuente. |
| Publicador de eventos | Avisa cambios de semáforo, cierre de vehículo y disparo de dictamen. |
| Motor de dictamen | Arma el contexto, llama al LLM y persiste veredicto y alertamientos. |
| Generador de evidencias | PDF por vehículo, Excel del lote y ZIP. |
| Canal en tiempo real | Empuja el estado de cada celda al dashboard sin recargar la página. |

El producto se despliega y versiona aparte de Trámites, después de la puerta de salida de la plataforma. Usa `@flit/shell` para la barra y el menú común. La identidad, las compañías, las suscripciones y los roles no se reimplementan. Las consultas externas tampoco: se piden a la plataforma y el consumo queda medido por empresa y por producto.

### Acceso

El hub crea la compañía y su administrador. El Super Admin da de alta la compañía y el usuario administrador, y activa o suspende Diagnóstico para esa empresa. El administrador de la compañía se autogestiona en el hub: crea usuarios y les asigna el rol de cada producto, entre ellos el de Diagnóstico.

La puerta del producto es la suscripción, no una bandera dentro de Trámites.

| Suscripción | Menú del hub | `diagnostico.flitsas.online` |
|---|---|---|
| Suspendida o inexistente | No muestra Diagnóstico | No abre el home |
| Activa | Abre el producto sin otro login | Entra con el token del producto |

- Con sesión en el hub y suscripción activa, el menú abre el subdominio directo.
- Sin sesión, el subdominio redirige al login del hub. El formulario de login no vive en Diagnóstico.
- Cerrar sesión es global: revoca la sesión de la suite.
- El cambio de compañía activa lo resuelve el hub. Al volver, Diagnóstico trabaja con la compañía del token.

Con token válido, cada request comprueba la suscripción de la compañía activa y después el rol de Diagnóstico para la acción concreta. El efecto de activar o suspender se nota en minutos, sin volver a iniciar sesión.

### Compañías y configuración

El catálogo de compañías es el del hub. Diagnóstico no guarda una copia ni ofrece alta de compañías. La compañía activa llega en el token.

En el producto, la ficha de la compañía activa permite editar solo la configuración de Diagnóstico. Usuarios, roles, suscripción, datos generales y configuración de trámites se abren y se guardan en el hub. La API de Diagnóstico rechaza esos cambios.

Suspender la suscripción oculta el producto en el hub y cierra el home del subdominio. Los diagnósticos ya guardados siguen referenciando ese `empresa_id`.

### Datos que viven en cada lado

| Dato | Dueño |
|---|---|
| Compañía, usuario, suscripción, roles por producto | Hub |
| Medición de consumo de consultas externas | Plataforma, por empresa y producto |
| Configuración del producto Diagnóstico | Este producto, referenciando `empresa_id` |
| Diagnóstico, vehículos, consultas, dictamen, evidencias | Este producto, referenciando `empresa_id` y `usuario_id` |
| Trámite, saneamiento, inventario FLITO | Producto Trámites |

Si un PDF o un listado necesita el nombre de la compañía o del usuario, se resuelve en el momento contra el hub.

### Integraciones existentes que hay que reutilizar

| Sistema | Uso |
|---|---|
| API de consultas de la plataforma | Única puerta hacia RUNT, SIMIT, avalúo e impuesto. Devuelve el dato normalizado y el servicio que respondió. El consumo queda medido. |
| RUNT | Paso 1 obligatorio. Placa definitiva, propietario, locatario, servicio y departamento. Caminos a registrar en la plataforma: Verifik, Kyverum, Intempo. |
| SIMIT | Comparendos y resoluciones de vehículo, propietario y locatario. Caminos a registrar en la plataforma: Verifik, Smartcloud. |
| Avalúo | Historial de asegurabilidad y pérdidas totales. Hoy Fasecolda, ya previsto en la plataforma. La fuente admite otro servicio sin cambiar la celda. |
| Impuesto departamental | Deuda de impuesto vehicular, por scraping. Hay que sumarlo al contrato de consultas de la plataforma. Solo si la secretaría está en la tabla maestra y el servicio del vehículo no es público. |
| Hub (`@flit/auth`, suscripciones, roles) | Origen del token, de la compañía activa y de la autorización. |
| `@flit/shell` | Barra común y menú de productos. |
| FLITO | Selección múltiple en el inventario de Trámites; el salto vuelve a este subdominio con las placas. |
| Wizard de trámites | Destino de *Crear trámite*, en `tramites.flitsas.online`, con datos precargados. |
| Saneamiento | Destino de *Sanear*, con el valor de la deuda. |
| Tipos de trámite activos | Catálogo del combobox de modo Con análisis, leído del producto Trámites. |

## 5. Modelo de dominio

### Diagnóstico (lote / cabecera)

| Campo | Notas |
|---|---|
| `id` | Identificador único. |
| `empresa_id` | Referencia a la compañía del hub. No es una compañía local. |
| `nombre` | Obligatorio. |
| `modo` | `SIMPLE` o `CON_ANALISIS`. |
| `tipo_tramite` | Obligatorio si el modo es `CON_ANALISIS`. Nulo en Simple. |
| `alcance` | JSON de fuentes marcadas. RUNT siempre presente. |
| `estado` | `CREADO`, `EN_PROCESO`, `COMPLETADO`, `INCOMPLETO`. |
| `configuracion_json` | Copia inmutable de lo definido en MOD01. |
| `usuario_creador_id` | Referencia al usuario del hub que creó el diagnóstico. |
| `creado_en` | Fecha y hora de inicio. |

Transiciones:

- `CREADO` al guardar la cabecera, antes de encolar vehículos.
- `EN_PROCESO` al encolar el primer vehículo.
- `COMPLETADO` cuando todas las consultas aplicables de todos los vehículos están en verde.
- `INCOMPLETO` cuando el usuario cierra el panel con al menos una consulta en error sin reprocesarla.

A partir de `EN_PROCESO` la cabecera no se edita.

### Vehículo del diagnóstico

| Campo | Notas |
|---|---|
| `id` | |
| `diagnostico_id` | |
| `identificador_original` | Lo que ingresó el usuario. |
| `tipo_identificador` | `PLACA_CARRO`, `PLACA_MOTO`, `PLACA_REMOLQUE`, `VIN`. |
| `placa` | Definitiva cuando el RUNT responde. |
| `vin` | Si aplica. |
| `documento_propietario` | Sale del RUNT. |
| `documento_locatario` | Sale del RUNT. Puede no existir. |
| `servicio` | Particular o público, según RUNT. |
| `departamento` | Según RUNT. Define si el impuesto aplica. |
| `datos_consolidados` | JSON normalizado de todas las fuentes completadas. |
| `estado_consultas` | Derivado de sus celdas. |

### Consulta (celda del semáforo)

| Campo | Notas |
|---|---|
| `id` | |
| `vehiculo_id` | |
| `fuente` | `RUNT`, `SIMIT_VEHICULO`, `SIMIT_PROPIETARIO`, `SIMIT_LOCATARIO`, `AVALUO`, `IMPUESTO_DEPARTAMENTAL`. |
| `estado` | `PENDIENTE`, `COMPLETADO`, `ERROR`, `NA`. |
| `origen` | `API` o `MANUAL`. Nulo mientras está pendiente. |
| `payload` | Respuesta normalizada. Vacío en error y en N/A. |
| `usuario_manual_id` | Solo si `origen = MANUAL`. No se expone al cliente. |

### Intento de consulta (auditoría)

Un registro por cada llamada o reintento. No se pisa: el historial se acumula.

| Campo | Notas |
|---|---|
| `diagnostico_id`, `vehiculo_id`, `fuente` | La celda. SIMIT vehículo, propietario y locatario son fuentes distintas. |
| `servicio` | Camino usado en ese intento: `VERIFIK`, `KYVERUM`, `INTEMPO`, `SMARTCLOUD`, `FASECOLDA`, `SCRAPING`. Nulo en el llenado manual. |
| `inicio_ms`, `fin_ms` | |
| `estado` | `Success` o `Error`. |
| `mensaje_error` | Si aplica. |
| `transaccion_proveedor_id` | Si el servicio lo devuelve. |

### Dictamen

| Campo | Notas |
|---|---|
| `vehiculo_id` | Uno vigente por vehículo. |
| `veredicto` | `VIABLE` o `BLOQUEADO`. En pantalla, `BLOQUEADO` se rotula *Bloqueado / Requiere Saneamiento*, como lo fija la épica 12678. |
| `alertamientos` | Lista de textos. |
| `analisis_override` | `true` si el admin lo modificó. |
| `usuario_override_id`, `override_en` | Solo auditoría interna. |
| `version` | Sube en cada recálculo. La versión anterior se archiva, no se muestra. |

### Derivación

En el trámite creado por el wizard: `origen_diagnostico_id` y referencia al vehículo del lote. En la fila del diagnóstico se muestra *Derivado al Trámite #XXXXX*.

## 6. Diseño de las capacidades internas

### MOD01 — Parametrización

**Entrada.** Formulario en tres secciones: información general, alcance y modo.

**Reglas.**

- Nombre obligatorio.
- RUNT se envía siempre activo. El cliente no puede desmarcarlo; la API también lo rechaza si llega apagado.
- Fuentes opcionales, presentadas en los tres grupos jerárquicos que fija la épica 12638:

  | Grupo | Fuentes |
  |---|---|
  | Por Vehículo | RUNT (marcado y bloqueado), SIMIT vehículo, avalúo, impuesto departamental |
  | Por Propietario | SIMIT propietario |
  | Por Locatario | SIMIT locatario |

  El documento del propietario y el del locatario salen del RUNT. Por eso los dos últimos grupos dependen del primero y no pueden marcarse como fuente independiente.
- En Con análisis, `tipo_tramite` es obligatorio y debe existir entre los tipos activos de la plataforma.
- Tipos previstos: TRASPASO, TRASPASO UNILATERAL, TRANSFERENCIA DE DOMINIO, REGISTRO DE PRENDA, LEVANTAMIENTO DE PRENDA, CANCELACION DE MATRICULA.
- Guardar crea la cabecera en `CREADO` y congela `configuracion_json`.

**Express.** El mismo contrato, en un panel compacto, con un solo identificador ya capturado.

La épica 12637 lo define como *modalidad unitaria sin creación de lote* y la 12641 describe su flujo. Se cumple de cara al usuario: no hay nombre de lote que inventar, ni archivo, ni pantalla de validación. En persistencia sigue siendo un diagnóstico con un vehículo, para que MOD03 a MOD07 no necesiten un segundo camino. El nombre se autogenera con el identificador y la fecha, y queda editable. Al confirmar, las consultas arrancan de inmediato y el usuario aterriza directo en el dashboard del MOD04.

### MOD02 — Captura e ingesta

**Detección del identificador**, aplicada igual en las tres vías:

| Resultado | Regla |
|---|---|
| Placa carro | 3 letras + 3 números. Ejemplo: `ABC123`. |
| Placa moto | 3 letras + 2 números + 1 letra. Ejemplo: `ABC12D`. |
| Placa remolque | `R` + 5 números. Ejemplo: `R12345`. |
| VIN | 17 caracteres alfanuméricos, sin `I`, `O` ni `Q`. |
| Inválido | Cualquier otro valor. No se encola. |

Normalizar a mayúsculas y quitar espacios antes de clasificar.

**Carga masiva.**

- Botón **Descargar Plantilla Base** en la pantalla principal. Entrega un `.xlsx` con la columna obligatoria `Identificador_Vehiculo`.
- Se aceptan `.xlsx`, `.xls` y `.csv`.
- La validación es local. No llama fuentes externas.
- Terminada la validación se muestra el **Resumen de Validación**: conteo de filas válidas y de filas con error, y dos salidas.
  - *Corregir ahora*: lista solo las filas erróneas, editables en la misma pantalla, cada una con el motivo del rechazo. La corrección se revalida al vuelo. No hay que descargar ni volver a subir el archivo.
  - *Continuar solo con los válidos*: descarta las filas erróneas y encola el resto.
- Si no queda ninguna fila válida, no se crea el avance a cola y se informa el error.
- Duplicados dentro del mismo archivo: se conserva una fila y se reporta el duplicado en el resumen.

**FLITO.** El inventario envía las placas seleccionadas al mismo validador. No hay archivo intermedio.

**Salida.** Vehículos válidos asociados al `diagnostico_id`, cada uno con sus celdas creadas según el alcance (RUNT en `PENDIENTE`; opcionales en `PENDIENTE`; las no elegidas no se crean). El lote pasa a `EN_PROCESO` y se publica un trabajo por vehículo.

### MOD03 — Motor de consultas

**Orden por vehículo.**

1. Consultar RUNT con placa o VIN.
2. Persistir placa definitiva, documentos, servicio y departamento.
3. Si RUNT falla: todas las celdas de ese vehículo quedan en `ERROR`. No se disparan otras fuentes. El lote sigue con los demás vehículos.
4. Si RUNT responde: lanzar en paralelo solo las fuentes del alcance.
5. Evaluar reglas de impuesto antes de llamar al proveedor.
6. Al cerrar cada fuente, publicar evento de semáforo.
7. Si todas las celdas aplicables están en `COMPLETADO` o `NA` y el modo es Con análisis, publicar trigger de dictamen para ese vehículo.

**Fuente y servicio.** La celda del semáforo es la fuente. El servicio es el camino con el que la plataforma ejecuta esa consulta. Diagnóstico no elige el proveedor ni guarda sus credenciales: pide la fuente a la API de consultas y persiste el servicio que la respuesta indique. El detalle de contratos, orden y respaldo entre caminos lo cierra el contrato de consultas de la plataforma. Kyverum, Intempo, Smartcloud y el scraping de impuesto hay que agregarlos a ese contrato; el plan de la suite ya nombra RUNT, SIMIT y Fasecolda.

La épica 12659 lista `FASECOLDA` como fuente del log de auditoría. Esta propuesta lo corrige: la fuente es `AVALUO` y Fasecolda es uno de los servicios que la resuelven. Así, cambiar de proveedor de avalúo no altera la celda del semáforo, el JSON consolidado ni el PDF.

| Fuente | Identificador | Servicios |
|---|---|---|
| RUNT | Placa o VIN original | Verifik, Kyverum, Intempo |
| SIMIT vehículo | Placa definitiva | Verifik, Smartcloud |
| SIMIT propietario | Documento del propietario | Verifik, Smartcloud |
| SIMIT locatario | Documento del locatario. Si el RUNT no trae locatario, la celda queda en `NA`. | Verifik, Smartcloud |
| Avalúo | Placa definitiva | Fasecolda. La fuente admite otro servicio sin cambiar la celda ni el JSON consolidado. |
| Impuesto departamental | Placa definitiva + departamento | Scraping |

Cada camino se registra en la plataforma, bajo la fuente. Agregar un camino no cambia la celda, el PDF ni el dictamen. El intento de auditoría guarda el servicio que devolvió la plataforma.

**Impuesto.**

- Servicio público según RUNT → celda `NA`, sin llamada.
- Departamento ausente de la tabla maestra de secretarías → celda `ERROR`, con llenado manual disponible para el admin.

**Reproceso.** Nuevo intento solo de esa fuente y ese vehículo. Las celdas ya en `COMPLETADO` no se vuelven a consultar. Si el reproceso ocurre después de un dictamen, al quedar la celda en verde se dispara el recálculo del MOD05.

**Llenado manual.** Lo ejecuta la API del admin, no el worker. Guarda `origen = MANUAL` y `usuario_id`, pone la celda en `COMPLETADO` y evalúa el mismo trigger de dictamen.

**Log.** Cada intento, incluido el manual, escribe el registro de auditoría de la sección 5. El manual no tiene `transaccion_proveedor_id`.

**Concurrencia.** El worker debe ser idempotente por `(vehiculo_id, fuente)`: un segundo mensaje no lanza otra llamada si la celda ya está en `COMPLETADO` o `NA`.

### MOD04 — Dashboard

**Lectura.** Matriz dinámica: filas son vehículos, identificados por placa o VIN; columnas son las fuentes seleccionadas en MOD01.

| Estado de celda | Representación |
|---|---|
| `PENDIENTE` | Círculo gris con spinner |
| `COMPLETADO` | Círculo verde con check |
| `ERROR` | Círculo rojo con cruz |
| `NA` | Círculo gris claro, sin ícono |

El estado global se calcula, no se edita a mano:

| Estado global | Condición |
|---|---|
| En proceso | Alguna celda en `PENDIENTE` o `ERROR` y el usuario no ha cerrado el panel. |
| Completado | Todas las celdas aplicables en `COMPLETADO` o `NA`. |
| Incompleto | El usuario cierra el panel con al menos una celda en `ERROR`. |

El panel no se cierra solo al completar.

**Tiempo real.** El cliente se suscribe al `diagnostico_id`. Cada evento de celda actualiza solo ese círculo.

**Autorización de acciones.**

| Acción | Cliente | Admin del producto |
|---|---|---|
| Ver grilla y estado global | Sí | Sí |
| Reprocesar | No | Sí, solo celdas en `ERROR` |
| Llenar datos | No | Sí, solo celdas en `ERROR` |
| Ver que el dato fue manual | No | Sí, en herramientas internas; no en la grilla que ve el cliente |

El modal de llenado manual cambia de campos según la fuente. Al guardar, la celda pasa a verde sin esperar un refresh.

### MOD05 — Motor de IA

**No se desarrolla hasta cerrar el ADR de la base de conocimiento.** El ADR debe fijar dónde viven las reglas por tipo de trámite, cómo se versionan y cómo se inyectan al modelo.

**Disparo.**

- Modo Con análisis.
- Todas las celdas del vehículo en `COMPLETADO` o `NA`.
- Un vehículo `INCOMPLETO` no se analiza.

**Entrada al modelo.**

1. Tipo de trámite de la cabecera.
2. JSON consolidado del vehículo.
3. Reglas de la base de conocimiento para ese trámite.

**Salida persistida.**

- `VIABLE` o `BLOQUEADO`. El valor persistido es `BLOQUEADO`; la etiqueta que ve el usuario es *Bloqueado / Requiere Saneamiento*, y es la que habilita el botón *Sanear* del MOD06.
- Lista de alertamientos en lenguaje humano, cada uno explicando el hallazgo y por qué bloquea o no. Ejemplo: *El propietario tiene 2 comparendos en SIMIT que suman $1.200.000 (bloquea el traspaso)*.

**Recálculo.** Un nuevo dictamen reemplaza al visible. La versión anterior queda archivada con su `version`.

**Override.** Endpoint solo de admin. Exige confirmación en cliente (el API recibe un flag `confirmado = true`; si falta, rechaza). Permite cambiar veredicto y editar, agregar o quitar alertamientos. Marca `analisis_override`, usuario y timestamp. La respuesta que consume el cliente no incluye esos campos.

### MOD06 — Derivación

**Crear trámite**, por fila, cuando el vehículo tiene sus consultas aplicables cerradas.

La épica 12679 lo condiciona a que *el diagnóstico* esté completo. Esta propuesta lo adelanta al cierre de cada vehículo, por coherencia con el principio 3: si el dictamen es por vehículo, la derivación también. Un lote de 200 placas no debe retener la derivación de las 199 que ya cerraron por una que quedó en error.

| Modo | Comportamiento |
|---|---|
| Con análisis + Viable | Confirmación y redirección al wizard con el tipo ya definido. |
| Con análisis + Bloqueado | No se ofrece crear trámite. Se ofrece *Sanear*. |
| Simple | Modal para elegir el tipo de trámite y luego el wizard. |

**Payload hacia el wizard.** Placa, VIN, datos del vehículo y datos del propietario tomados del JSON consolidado. El salto cambia de subdominio: este producto entrega un contexto firmado y de un solo uso; el wizard, en `tramites.flitsas.online`, lo canjea con el token de Trámites y precarga el formulario. El wizard sigue siendo el dueño del guardado del trámite. Al crearlo, persiste `origen_diagnostico_id`. El diagnóstico muestra la etiqueta con el número de trámite.

**Sanear.** Redirige al saneamiento de Trámites con el mismo mecanismo, llevando el monto de deuda detectado en SIMIT o en impuesto. El contrato exacto de campos lo define el equipo de saneamiento antes de construir esta pantalla; esta propuesta exige que el monto y la fuente de la deuda viajen juntos.

### MOD07 — Reportes y trazabilidad

**Listado cliente.** Id, nombre, fecha, tipo, trámite analizado, estado, usuario creador. Filtros por rango de fechas, tipo, estado y nombre. Siempre acotado a la empresa del usuario.

**Listado admin.** Los mismos datos más filtro por cliente o empresa, agrupación por tipo de análisis y estas métricas:

- Vehículos procesados por cliente.
- Conteo por modalidad: Simple, Con análisis por cada tipo de trámite.
- Reprocesos y fallos por proveedor, leídos de la tabla de intentos.
- Trámites originados en diagnósticos: conteo de trámites con `origen_diagnostico_id`, por cliente y por tipo de trámite, con la tasa de conversión sobre los vehículos diagnosticados. Es el reporte gerencial que pide la épica 12679 y lo habilita la trazabilidad del MOD06.

La agrupación por modalidad es la pieza que facturación necesita. Cada modalidad tiene tarifa distinta; este aplicativo no calcula el valor.

**Archivos.**

| Entregable | Regla |
|---|---|
| PDF por vehículo | Formato de la suite. Solo secciones de fuentes del alcance. Encabezado: id, usuario, empresa, fecha y hora, tipo de análisis. Oculta `origen = MANUAL`. |
| Excel del lote | Una fila por vehículo. Columnas según la configuración guardada, no según un esquema fijo. |
| ZIP | Todos los PDF del lote, descarga directa en el navegador. |

Los archivos se generan bajo demanda y se asocian al `diagnostico_id` para no regenerarlos si la evidencia no cambió. Un recálculo de dictamen o un llenado manual invalida el PDF de ese vehículo.

## 7. Contratos internos del aplicativo

Eventos entre las capacidades MOD01 a MOD07:

| Evento | Lo publica | Lo consume |
|---|---|---|
| `diagnostico.encolado` | MOD02 | MOD03 |
| `consulta.actualizada` | MOD03 o llenado manual | MOD04 |
| `vehiculo.consultas_completas` | MOD03 | MOD05, si el modo es Con análisis |
| `dictamen.emitido` | MOD05 | MOD04 y MOD07 |
| `diagnostico.cerrado_incompleto` | MOD04 | MOD07 |

El estado global del lote se recalcula al recibir `consulta.actualizada` y al cerrar el panel.

## 8. Roles del producto

Diagnóstico no define ni administra el directorio de roles. El hub asigna, por empresa y por usuario, el rol de este producto (`product_code` de Diagnóstico). La puerta de entrada es anterior al rol: si la suscripción está suspendida, ninguna capacidad aplica.

| Capacidad | Usuario del producto | Admin del producto |
|---|---|---|
| Entrar al subdominio | Solo con suscripción activa y rol de Diagnóstico | Solo con suscripción activa y rol de backoffice del producto |
| Editar la configuración del producto de la compañía activa | Según el permiso de su rol | Sí |
| Editar usuarios, roles, suscripción o la ficha general de la compañía | No. Eso se hace en el hub | No. Eso se hace en el hub |
| Crear diagnóstico y cargar vehículos | Sí, en su compañía | Sí |
| Ver dashboard y descargar reportes | Sí, en su compañía | Sí, entre compañías con suscripción activa |
| Reprocesar y llenar datos | No | Sí |
| Override del dictamen | No | Sí |
| Ver origen manual, override y volumetría de facturación | No | Sí |

Toda consulta de listado y detalle filtra por la compañía activa del token cuando el rol es de cliente. El admin del producto debe seleccionar compañía; el filtro puede quedar vacío solo en el dashboard de facturación.

Los nombres internos de esos roles se cierran en el contrato de productos del hub. Esta tabla fija las capacidades del producto.

## 9. Experiencia visual

Respetar `@flit/ui` y la barra de `@flit/shell`. Colores de marca: azul `#557eff`, naranja `#ff4e00`, verde `#8cc63f`.

Pantallas a diseñar e implementar:

1. Alta de diagnóstico: tres secciones (Información General, Selección de Alcance, Modo de Operación). El alcance se agrupa en Por Vehículo, Por Propietario y Por Locatario. RUNT aparece marcado y bloqueado, con tooltip que explica por qué. El combobox de tipo de trámite se despliega animado solo al elegir Con análisis.
2. Home con búsqueda express y botón *Descargar Plantilla Base*. El express abre un modal o panel lateral con la versión compacta del formulario de alta y termina en el dashboard.
3. Resumen de validación de carga masiva: conteo de válidos y erróneos, y las dos salidas. *Corregir ahora* muestra las filas erróneas editables en sitio con el motivo del rechazo; *Continuar solo con los válidos* descarta el resto.
4. Grilla de semáforos con indicador global: spinner gris, verde con check, rojo con cruz, gris claro para N/A.
5. Modal de llenado manual (solo admin) y acciones de reproceso.
6. Panel de dictamen por vehículo, con tarjetas de alertamiento.
7. Confirmación de override (solo admin).
8. Acciones *Crear trámite* y *Sanear*, y la etiqueta de trazabilidad.
9. Listado de diagnósticos y, para admin, el tablero de volumetría.
10. Configuración del producto de la compañía activa. Usuarios, roles y suscripción no se editan en esta pantalla.

La vista cliente de la grilla no renderiza botones de acción aunque el API los oculte: la restricción va en los dos lados.

## 10. Plan de entrega

El producto arranca cuando la puerta de salida de la plataforma está cumplida en QA: token validado, entrada por el hub, cambio de producto sin otro login, alta y suspensión de suscripción, roles por producto, Marca Blanca, producto de prueba con la plantilla y una consulta externa con consumo medido. Las fases de abajo son las del producto, no las fases 0 a 2 de la suite.

| Fase | Entrega | Resultado demostrable |
|---|---|---|
| 0 | Host `diagnostico.flitsas.online` sobre la plantilla, `@flit/auth` y `@flit/shell`, lectura de la suscripción. ADR del motor de IA. Contratos de salto al wizard y a saneamiento. | Con suscripción activa, el menú del hub entra sin otro login. Sin suscripción, el producto no abre. Sin sesión, redirige al hub. |
| 1 | MOD01 + MOD02 express y masivo | Se crea un lote y queda una lista válida encolada, sin llamar aún a la API de consultas (cliente simulado). |
| 2 | MOD03 + MOD04 | Consultas por la API de la plataforma, semáforos en vivo, reproceso y llenado manual. El consumo queda medido. |
| 3 | MOD07 operativo | PDF, Excel y ZIP del lote, sin dictamen. Cubre el modo Simple de punta a punta. |
| 4 | MOD05 | Dictamen y recálculo, con la base de conocimiento del ADR. |
| 5 | MOD06 | Crear trámite y sanear, con trazabilidad, en `tramites.flitsas.online`. |
| 6 | Ingesta FLITO + tablero admin de facturación | Selección desde inventario y métricas por cliente y modalidad. |

El modo Simple se puede poner en manos del cliente al cerrar la fase 3. El modo Con análisis espera a las fases 4 y 5.

## 11. Decisiones que deben cerrarse antes de construir

| Tema | Cuándo | Quién |
|---|---|---|
| Puerta de salida de la plataforma cumplida en QA | Antes de la fase 0 de este producto | Líder técnico. El plan de la suite ya define el criterio |
| Nombres de los roles de Diagnóstico en el contrato de productos | Antes de la fase 0 | Frente de productos y hub |
| Campos concretos de la configuración del producto | Antes de la fase 1 | PO. La suscripción y los roles viven en el hub |
| Dónde vive la base de conocimiento del dictamen y cómo se versiona | Antes de MOD05 | Líder técnico, documentado como ADR |
| Contrato de entrada del wizard (campos obligatorios del JSON) | Antes de MOD06 | Equipo de trámites |
| Contrato de entrada de saneamiento (monto, fuente, placa) | Antes de MOD06 | Equipo de saneamiento |
| Tabla maestra de secretarías soportadas y su dueño funcional | Antes de MOD03 | Operaciones + plataforma |
| Incluir en el contrato de consultas Kyverum, Intempo, Smartcloud y el scraping de impuesto, con orden y respaldo | Antes de MOD03 | Frente de consultas de la plataforma. RUNT, SIMIT y Fasecolda ya están en el plan de la suite |
| Timeout, reintentos automáticos y qué cuenta como error de cada servicio | Antes de MOD03 | Plataforma. Diagnóstico trata como error lo que la API de consultas devuelva como error |
| Canal en tiempo real | Antes de MOD04 | Líder técnico. Los eventos de plataforma usan outbox y RabbitMQ |
| Ubicación de PDF y ZIP | Antes de MOD07 | Líder técnico |

## 12. Criterios de aceptación globales

1. Con la suscripción suspendida, el hub no muestra Diagnóstico y el subdominio no abre el home aunque exista una sesión de la suite.
2. Con la suscripción activa, el menú del hub entra sin otro login. Sin sesión, `diagnostico.flitsas.online` redirige al login del hub.
3. Desde Diagnóstico se edita solo la configuración del producto de la compañía activa. Usuarios, roles, suscripción y datos generales se rechazan en la API.
4. El producto no crea compañías. Alta, suspensión y roles ocurren en el hub, y el cambio de suscripción se refleja sin volver a iniciar sesión.
5. Cada consulta externa sale por la API de la plataforma y el intento guarda el servicio que respondió. El consumo queda medido por empresa y producto.
6. No se puede guardar un Con análisis sin tipo de trámite, ni apagar el RUNT.
7. Un identificador que no cumple placa ni VIN nunca dispara una consulta externa.
8. Si el RUNT de un vehículo falla, ninguna otra fuente de ese vehículo se llama.
9. Un fallo de SIMIT, avalúo o impuesto no vuelve a consultar las fuentes que ya están en verde.
10. Servicio público deja el impuesto en N/A.
11. Departamento no soportado deja el impuesto en error y permite llenado manual al admin.
12. El cliente no puede reprocesar, llenar datos ni cambiar el dictamen, ni por API ni por pantalla.
13. El dictamen solo existe en Con análisis y solo con el vehículo en verde.
14. Un reproceso exitoso posterior al dictamen lo reemplaza.
15. El trámite creado guarda `origen_diagnostico_id` y la fila del diagnóstico muestra el número.
16. El PDF del cliente no revela datos marcados como manuales en su origen, aunque muestra el mismo contenido.
17. La volumetría de admin separa Simple y cada tipo de trámite con análisis.
18. El resumen de validación permite corregir las filas erróneas en la misma pantalla y revalidarlas sin volver a subir el archivo.
19. El diagnóstico express no pide nombre de lote ni archivo, y al confirmar deja al usuario en el dashboard de ese vehículo.
20. El listado admin reporta cuántos trámites se originaron en diagnósticos, por cliente y por tipo de trámite.

## 13. Riesgos

| Riesgo | Efecto | Mitigación en esta propuesta |
|---|---|---|
| Inestabilidad de RUNT o SIMIT | Lotes atorados en error | Semáforo por celda, reproceso puntual y llenado manual. El lote no se cae entero. |
| Reglas de trámite incompletas | Dictámenes inconsistentes | ADR obligatorio y fase 4 separada del modo Simple. |
| Wizard no acepta precarga | Se pierde el valor de MOD06 | Contrato cerrado en fase 0; si no está listo, MOD06 no se inicia. |
| Volumen de carga masiva | Timeouts en la petición HTTP | La validación del archivo responde ya; las consultas viven en cola. |
| Confusión API vs manual ante el cliente | Hallazgo de auditoría mal expuesto | El origen manual no viaja en los DTO del rol cliente ni en el PDF. |
| Tratar el producto como un módulo de Trámites | Se duplican usuarios, roles y login | Subdominio propio, token del hub y suscripción comprobada en cada request. |
| Arrancar Diagnóstico antes de la puerta de salida | Se reconstruye identidad y consultas que la plataforma ya va a entregar | Las fases del producto empiezan cuando el líder verifica la puerta en QA. |
| Caminos de consulta fuera del contrato de plataforma | Kyverum, Intempo, Smartcloud o el scraping quedan solo dentro del producto | Esos caminos se registran en la API de consultas antes del MOD03. |
