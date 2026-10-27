# Comparendos: manual y pruebas

*FLIT Suite · Gestión de Comparendos*

> Versión en Markdown del artifact https://claude.ai/artifact/2afN1rM7YmV3w1aE3RY9vH (corte: 23 de octubre de 2026). El artifact es la versión de referencia; este archivo conserva el mismo contenido para tenerlo en el repositorio.

Cómo se usa cada pantalla y cómo probarla. La parte 1 sirve para aprender el producto; la parte 2 es el plan de pruebas, con casos que se marcan en esta misma página.

Corte: 23 de octubre de 2026. Qué es el producto, cómo está armado y qué falta: ver el documento «Comparendos: entrega del producto».

## Reglas de seguridad (leer primero)

- **Ningún correo de prueba puede llegar a un conductor ni a un organismo de tránsito real.** Los correos de prueba van a **samuel.cardenas@flitsas.com** (buzón de pruebas acordado).
- Como correo del conductor de prueba usa siempre ese buzón o un correo inventado `@example.test`. Nunca el de una persona real.
- **En QA no envíes derechos de petición ni cambios de contraventor** hasta que se confirme que los correos de los organismos en la base de QA se reemplazaron por el buzón de pruebas. En DEV ya están reemplazados.
- No borres ni edites datos que no creaste tú. Los comparendos que llegan de SIMIT son reales.
- Antes de enviar algo, revisa a quién va (la vista previa lo muestra). Si dudas, no envíes y pregunta.
- No pruebes contra producción.

Como Samuel no está, no vas a ver los correos en tu bandeja. El contenido exacto de cada correo (destinatario, texto, adjuntos) se revisa en **Operación → Centro de mensajes**, que muestra el correo tal como salió.

## Ambiente y usuarios

| Ambiente | Cómo entrar | Qué hay |
|---|---|---|
| **DEV** (recomendado ahora) | Hub `dev.flitsas.online` → ▦ → Comparendos (o directo `dev.comparendos.flitsas.online`) | Todo lo construido. Correos de organismos ya reemplazados por el buzón de pruebas. |
| **QA** | Hub `qa.flitsas.online` → ▦ → Comparendos | La primera entrega. Las mejoras de octubre llegan a medida que se promueven (el documento de entrega dice cuáles). |

Necesitas dos usuarios, idealmente en dos navegadores o una ventana privada:

- **SuperAdmin de FLIT:** para el menú «Administración FLIT» y para parametrizar empresas.
- **Usuario de una empresa** con el rol «Administrador de Comparendos»: para operar. Para probar permisos, otro con «Operador» o «Consulta».

Pide los usuarios a la persona encargada o a Jorman; las contraseñas no van en este documento. La empresa de pruebas es **Empresa Demo**.

### Datos que conviene tener listos

- Un comparendo **en tiempo con conductor** que tenga correo, para avisos.
- Un comparendo **sin conductor**, para asignar.
- Un comparendo con **organismo que permita el cambio de contraventor**, con conductor con documento, para el cambio.
- Un **representante legal** con firma, cédula, certificado y «Escritura o poder» cargados (archivos de prueba).
- Imágenes de prueba para cédula y licencia, y 2 o 3 PDF o fotos de comparendos para OCR.

## Conceptos en 2 minutos

| Concepto | Qué significa |
|---|---|
| Estado legal | Se calcula con días hábiles: **Sin notificación** (falta la fecha), **En tiempo** (dentro del límite de gestión de 11 días hábiles), **Vencido**, **Caducado** (más de 1 año), **Prescrito** (más de 3 años). |
| Ventana de descuento | Fotomulta: 50 % hasta el día hábil 11 desde la notificación, 25 % hasta el 26. En vía: 50 % hasta el día 5 desde la imposición, 25 % hasta el 20. Luego, sin descuento. |
| Fuente | SIMIT (por NIT o placa), secretaría municipal (por NIT o placa) u OCR. |
| Conductor / contraventor | Quién manejaba. Se asigna a mano o automáticamente (Renting). |
| Avisos 1 a 4 | Correos al conductor: 1 inicial, 2 cuando pasa a 25 %, 3 cuando se acaba el descuento, 4 cuando se pide el cambio de contraventor. |
| DP | Derecho de petición al organismo, firmado por el representante legal. |
| Inactivo | El comparendo dejó de aparecer en todas sus fuentes, o una persona lo inactivó. Nada se borra. |

---

# Parte 1 · Manual de uso

## Entrar

1. Entra al hub con tu usuario. Abre el menú de productos ▦ y elige **Comparendos**. No vuelve a pedir la contraseña.
2. Si eres SuperAdmin, elige primero la empresa. Verás su operación en solo lectura y el menú «Administración FLIT».
3. El menú se divide en **Operación**, **Reportes**, **Configuración** y, para el SuperAdmin, **Administración FLIT**. Cada usuario solo ve lo que su rol permite y lo que la empresa tiene contratado (por ejemplo, sin OCR contratado no aparece «Paquetes OCR»).

## Parametrizar una empresa (SuperAdmin)

### Parametrizador por empresa

Administración FLIT → Parametrizador por empresa

1. La empresa debe tener encendido el producto Comparendos en la administración de la plataforma (hub).
2. **Registrar empresa:** elígela de la lista. El nombre y el NIT (con su dígito de verificación) vienen de la plataforma y no se editan.
3. Mientras le falte algo, aparece «Pendiente de configuración» con la lista de lo que falta.
4. **Capacidades:** enciende las contratadas: derechos de petición, carga por OCR, consulta del contraventor, cambio de contraventor y reportes.
5. **Integración del conductor:** Manual o consulta automática (Renting). Con la automática, marca qué NIT la usan y pulsa **Probar conexión**: solo inicia sesión, no consulta ninguna placa.
6. **Tipologías de DP:** habilita las que la empresa puede usar.
7. **Consultas:** en Trabajos e ingesta, define por canal (SIMIT NIT, SIMIT placa, municipal NIT, municipal placa) el encendido y la agenda. Luego pasa la configuración a **Activa**; sin eso no se consulta nada.
8. El canal de correo y el conjunto de plantillas se ven en solo lectura: salen del «Enrutamiento de notificaciones» de la empresa en Trámites.

## Configurar la empresa (Administrador de la empresa)

### NITs y placas

Configuración → NITs y placas

- **Agregar NIT:** con o sin dígito de verificación. Si el dígito está errado, lo rechaza y dice cuál es. Se consultan las dos variantes.
- **Agregar placas:** una a una, pegadas en lote, o con la **plantilla de Excel** (hasta 1.000). Informa las agregadas, las que ya existían, las repetidas y las inválidas.
- Se pueden desactivar y reactivar; el historial dice quién y cuándo. La columna «Conductor» dice si el NIT usa la consulta automática (solo lectura).

### Representantes legales

Configuración → Representantes legales

- Datos, firma (imagen), cédula, certificado de existencia y **Escritura o poder**. Solo el documento y el nombre son obligatorios; lo que falte sale como advertencia y el documento se envía sin ese anexo.
- Un solo representante activo por alcance (la empresa o cada NIT).
- Permisos: firmar derechos de petición, firmar cambio de contraventor. Puede ser de la empresa o de un NIT concreto.
- Sin representante con permiso no se envían DP ni cambios de contraventor, y los avisos salen sin la autorización adjunta.

### Organismos de tránsito

Configuración → Organismos de tránsito

Lista de organismos con sus correos de radicación y si permiten el cambio de contraventor (eso lo define FLIT y la empresa lo ve en solo lectura). La empresa puede bloquear el cambio ante un organismo, en general o para un representante.

### Empresa y correo

Configuración → Empresa y correo

- Canal de correo en solo lectura («Lo define FLIT para tu empresa»).
- Nombre del remitente, correo para respuestas, copias (CC y CCO) por tipo de envío y destinatarios de los reportes.
- La agenda de consultas automáticas (solo lectura): días, horas, última y próxima consulta.

### Plantillas

Configuración → Plantillas

Los textos de cada correo y documento con vista previa. Las edita el SuperAdmin en el Editor de plantillas.

## Bandeja

### La lista de trabajo

Operación → Bandeja

- **Tarjetas de estado legal** arriba, con su conteo. Un clic filtra; otro clic quita el filtro.
- **Búsqueda** por número o placa: busca sola mientras escribes.
- **Búsqueda rápida:** Próximos a vencer, Sin contraventor, Rechazados por OT, Sin gestión y los filtros jurídicos.
- **Filtros:** agrupados por tema, con «es» o «no es». Se pueden pegar listas desde Excel (por ejemplo 20 placas). Lo aplicado se ve como etiquetas; «Activos» viene puesto y se puede quitar.
- **Columnas:** elige cuáles ver; se recuerdan para tu usuario.
- **Orden:** por defecto, lo que vence primero arriba.
- **Exportar:** salen las columnas visibles, en archivos de 5.000 filas; si son más de 5 archivos pide confirmación.
- **Selección múltiple:** marca varios y usa «Enviar aviso» o «Enviar derecho de petición». Al final dice cuántos salieron y por qué se omitieron los demás.
- El menú de cada fila lleva a la sección del detalle (conductor, DP, pago…).

## Detalle del comparendo

### La ficha

Bandeja → clic en el comparendo

- **Encabezado:** estado legal, último hito, ventana de descuento con su fecha límite, organismo, fuente y valor.
- **Secciones:** datos del comparendo, conductor, avisos, derecho de petición, cambio de contraventor, pago, documentos.
- **Línea de tiempo:** todo lo que pasó, con fecha, quién y resultado. Se puede filtrar. Un correo enlaza al Centro de mensajes.
- **Observación:** botón flotante; queda en la línea de tiempo y no se edita.
- **Inactivar:** pide motivo y doble confirmación. Un comparendo inactivado a mano no se reactiva aunque la fuente lo vuelva a traer.

## Asignar el conductor

### Contraventor

Detalle → Contraventor

- **Manual:** tipo y número de documento, nombres, correo y teléfono, y la casilla de tratamiento de datos (obligatoria).
- **Reemplazar:** exige un motivo. El historial muestra el anterior, el nuevo, quién y cuándo. El nuevo recibe sus propios avisos.
- **Automático (Renting):** si el NIT tiene la consulta automática, el conductor llega solo. Si no se encuentra, queda «Pendiente de asignación manual» (naranja). Hay un botón para consultar de nuevo. El documento puede quedar «pendiente»: hay que completarlo.
- **Documentos:** carga la cédula y la licencia. Al cargar otra, la anterior queda como reemplazada.

## Avisos al conductor

### Enviar avisos

Detalle → Avisos · Bandeja → Enviar aviso

- Salen solos los días hábiles a la hora del «Horario de avisos» (hoy 7:00).
- **A mano:** el detalle propone el aviso que toca o explica por qué no se puede. En la bandeja se envían varios a la vez.
- No salen si está pagado, inactivo, notificado en persona o sin correo del conductor.
- **Adjuntos (1 a 3):** la autorización de cambio de infractor firmada, el poder del representante y el documento OCR si lo hay. **Aviso 4:** la autorización radicada.
- **Marca:** empresa con «API Renting cliente» en Trámites → textos e imagen de Renting. «Colas FLIT» → diseño FLIT.

## Cambio de contraventor

### Solicitar al organismo

Detalle → Cambio de contraventor

1. Revisa la lista de condiciones; cada una dice si se cumple. Lo que falte hay que resolverlo antes (por ejemplo, cargar la licencia).
2. Revisa la vista previa del formato y de la carta (llevan marca de agua «VISTA PREVIA»).
3. Pulsa **Solicitar**. Sale el correo al organismo con el formato firmado y los anexos; si se entrega, sale el aviso 4 al conductor. Estado: «En proceso».
4. Cuando el organismo responda (por correo a la empresa), registra la respuesta: **aceptado** → Exitoso; **rechazado** → causal (mínimo 10 caracteres), fecha y soporte. Un rechazado se puede volver a solicitar.

## Derecho de petición

### Individual y en lote

Detalle → Derecho de petición · Operación → Envíos masivos de DP

- **Individual:** elige la tipología (solo aparecen las que aplican al estado legal), revisa la vista previa con destinatarios, copias, firma y anexos, y envía. Queda un radicado `PREFIJO-AAMMDDHHMMSS-mmm`.
- **En lote:** selecciona en la bandeja → Enviar derecho de petición. Omite los que ya tienen DP o no cumplen. El resumen del lote se ve en «Envíos masivos de DP» y se exporta a Excel.
- **Respuesta:** favorable o desfavorable, con radicado, fecha y PDF. Solo se registra una.
- Bloqueos: pagado, tipología que no aplica, sin representante legal (avisa a los encargados).

## Pagos

### Registrar pagos

Detalle → Registrar pago · Operación → Carga de pagos

- Uno a uno desde el detalle o la fila de la bandeja.
- En lote: descarga la plantilla, llénala y súbela. El resultado dice cuáles entraron y, de las que no, por qué.
- Un pagado deja de recibir avisos y no admite DP.

## Carga por OCR

### Paquetes OCR

Operación → Paquetes OCR

1. **Nuevo paquete:** sube PDF, JPG, PNG o TIFF. Puedes asociarlo a un NIT.
2. Espera la lectura. Cada documento queda: **Completo**, **Incompleto** (faltan datos), **Fallido** o **Duplicado** (ya existe; enlaza al original).
3. Abre un documento: vista dividida (imagen a un lado, campos al otro). Las marcas señalan qué revisar, por ejemplo un número con un dígito de más o la fecha de notificación tomada de un sello.
4. Corrige lo necesario. **Reprocesar** vuelve a leer y conserva lo que corregiste (hasta 3 veces). Se puede reprocesar en lote.
5. **Registrar** crea los comparendos con fuente OCR; el documento queda adjunto.
6. **Exportar** el paquete a Excel.

Cada lectura tiene un costo para FLIT: en pruebas usa pocos documentos.

## Centro de mensajes

### Todo lo que salió

Operación → Centro de mensajes

- Filtros por tipo, estado y cuenta de salida.
- De cada correo: remitente, destinatario, copias, estado (pendiente, enviado, abierto, rebotado, fallido), intentos, adjuntos y el correo tal como se vio.
- Un correo fallido se puede reintentar a mano.

## Reportes

### Dashboard y productividad

Reportes → Dashboard · Reportes → Productividad

- Dashboard: indicadores del periodo, radar de vencimientos (clic → bandeja filtrada), series por mes y tabla por NIT. Se filtra por periodo y NIT.
- Productividad: lo que gestionó cada usuario en el periodo, con detalle y exportación.

## Administración FLIT (SuperAdmin)

| Pantalla | Cómo se usa |
|---|---|
| Trabajos e ingesta | Por empresa: agenda por canal, «Ejecutar ahora», ciclos con sus resultados (nuevos, actualizados, sin cambios, errores) y la respuesta de la fuente cuando falla. Trabajos programados con su horario en lenguaje normal y «Ejecutar ahora» por fila. |
| Horario de avisos | Hora de los avisos 1, 2 y 3; pausar; historial de cambios. |
| Calendario | Agregar o quitar festivos; muestra cuántos comparendos se recalculan. |
| Catálogo de organismos | Pestaña Catálogo (correos, permiso de cambio) y pestaña «Alias de secretarías» (crear alias asocia los comparendos sin organismo con ese texto). |
| Fuentes municipales | Activar o desactivar municipios (afecta a todas las empresas). |
| Alertas de integración | Destinatarios, filtro (todas, críticas, SIMIT, municipal) y umbral. |
| Tipologías de DP | Crear, editar, desactivar; historial. |
| Editor de plantillas | Textos de FLIT y de Renting por tipo, con variables, versiones y vista previa. |
| Prompt OCR | Crear versión → Evaluar → Publicar (solo si la evaluación aprueba) → «Volver a la versión N». |
| Log de migración | Cada consulta a fuente con su resultado y la migración desde V1. |

## Problemas frecuentes

| Síntoma | Causa probable | Qué hacer |
|---|---|---|
| La empresa no ve Comparendos en ▦ | Producto no encendido para la empresa | Encenderlo en la administración de la plataforma. |
| «Pendiente de configuración» | Falta algo del parametrizador | La misma pantalla lista lo que falta. |
| No llegan comparendos | Configuración de consultas no Activa, canal apagado o fuente no conectada | Trabajos e ingesta: revisar estado y último ciclo; «Ejecutar ahora». |
| Comparendo sin organismo | El texto de la secretaría no está en el catálogo ni en los alias | SuperAdmin crea el alias en Catálogo de organismos. |
| No sale el aviso | Sin correo del conductor, pagado, notificado en persona, inactivo, ya salió uno hoy o no le toca | El detalle dice por qué; la línea de tiempo también. |
| El aviso salió sin la autorización | Representante sin firma o sin permiso de cambio de contraventor | Completar en Representantes legales. |
| No deja enviar DP | Pagado, tipología no aplica al estado, ya tiene DP, sin representante | La vista previa muestra el bloqueo. |
| Fechas en el año 2000 | La fuente mandó 01-01-2000 como vacío | Ya se trata como vacío; si aparece, reportar. |
| Error 431 al abrir en local | Cookies de localhost muy grandes | Borrar cookies de localhost. |

---

# Parte 2 · Plan de pruebas

## Cómo usar el plan

- Cada caso tiene un código, la condición previa, los pasos y el resultado esperado.
- Marca el resultado en el selector (OK, Falla, Bloqueado) y escribe una nota si hace falta. En el artifact se marca en la página y se copia con **Copiar resultados**; en este archivo, marca las casillas de la columna Resultado.
- Prioridad **A** = flujo principal, probar sí o sí. **B** = importante. **C** = detalle.
- Prueba primero en DEV. En QA, solo lo que ya se haya promovido (ver el documento de entrega).

## Casos de prueba

Marca cada caso como OK, Falla o Bloqueado. Prioridad A = flujo principal, B = importante, C = detalle.

### Entrada y permisos (CP-ENT · 7 casos)

| Código | Prior. | Caso | Antes | Pasos | Esperado | Resultado |
|---|---|---|---|---|---|---|
| CP-ENT-01 | A | Entrar desde el hub | Usuario de una empresa con Comparendos encendido. | 1. Entrar al hub.<br>2. Abrir ▦ → Comparendos. | Abre la bandeja de la empresa sin pedir otra vez la contraseña. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-ENT-02 | B | Empresa sin el producto | Usuario de una empresa sin Comparendos. | 1. Intentar abrir Comparendos (por ▦ o por la dirección). | Pantalla de producto no habilitado, sin datos. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-ENT-03 | A | SuperAdmin elige empresa y solo lee | Usuario SuperAdmin. | 1. Abrir Comparendos.<br>2. Elegir Empresa Demo.<br>3. Abrir un comparendo e intentar una acción. | Ve la bandeja y el detalle; no puede actuar (solo «Ver detalle»); ve el menú «Administración FLIT». | ☐ OK ☐ Falla ☐ Bloq. |
| CP-ENT-04 | B | Rol Consulta | Usuario con rol Consulta de Comparendos. | 1. Recorrer el menú.<br>2. Abrir un comparendo. | Ve y exporta; no ve botones de enviar, asignar, pagar ni configurar. Una ruta de configuración abierta a mano responde sin acceso. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-ENT-05 | B | Rol Operador | Usuario con rol Operador. | 1. Recorrer el menú.<br>2. Ir a Configuración. | Opera (asignar, avisos, DP, pagos, OCR); no edita NITs, representantes, organismos ni correo. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-ENT-06 | C | Aislamiento entre empresas | Dos empresas con datos. | 1. Entrar con un usuario de la empresa A.<br>2. Buscar un número de comparendo de la empresa B. | No aparece. Ninguna pantalla muestra datos de otra empresa. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-ENT-07 | B | Menú de productos desde Comparendos | Sesión abierta en Comparendos. | 1. Abrir ▦.<br>2. Ir a Trámites y volver. | Lista los productos y cambia sin perder la sesión. | ☐ OK ☐ Falla ☐ Bloq. |

### Parametrizador (SuperAdmin) (CP-PAR · 7 casos)

| Código | Prior. | Caso | Antes | Pasos | Esperado | Resultado |
|---|---|---|---|---|---|---|
| CP-PAR-01 | A | Registrar empresa | Empresa con el producto encendido y sin registrar. | 1. Parametrizador → Registrar.<br>2. Elegirla de la lista. | Nombre y NIT con DV de solo lectura. Queda «Pendiente de configuración» con la lista de faltantes. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-PAR-02 | A | Encender capacidades | Empresa registrada. | 1. Encender DP, OCR, consulta y cambio de contraventor, reportes.<br>2. Entrar como la empresa. | El menú de la empresa refleja lo encendido; lo apagado no aparece (por ejemplo, sin OCR no hay «Paquetes OCR»). | ☐ OK ☐ Falla ☐ Bloq. |
| CP-PAR-03 | B | Tipologías de DP | Empresa registrada. | 1. Habilitar 2 tipologías.<br>2. Entrar como empresa y abrir un DP. | Solo ofrece las habilitadas (y que apliquen al estado legal). | ☐ OK ☐ Falla ☐ Bloq. |
| CP-PAR-04 | B | Consulta automática: probar conexión | Empresa con integración Renting. | 1. Pulsar «Probar conexión». | Dice «Conectado» y el tiempo, sin consultar ninguna placa. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-PAR-05 | B | NIT con consulta automática | Integración Renting encendida. | 1. Marcar un NIT con consulta automática y guardar.<br>2. Ver NITs y placas como empresa. | La columna «Conductor» dice Automático para ese NIT y Manual para los demás. Los comparendos de NIT no marcados pasan a «no aplica». | ☐ OK ☐ Falla ☐ Bloq. |
| CP-PAR-06 | C | Canal en solo lectura | Empresa con «API Renting cliente» en Trámites. | 1. Ver el parametrizador y Empresa y correo. | Canal y plantillas Renting, sin poder cambiarlos. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-PAR-07 | C | Auditoría de cambios | Cualquier cambio hecho. | 1. Ver el historial de la configuración. | Cada cambio con quién, cuándo, antes y después. | ☐ OK ☐ Falla ☐ Bloq. |

### Configuración de la empresa (CP-CFG · 8 casos)

| Código | Prior. | Caso | Antes | Pasos | Esperado | Resultado |
|---|---|---|---|---|---|---|
| CP-CFG-01 | A | Agregar NIT con DV errado y correcto | Usuario administrador de la empresa. | 1. Agregar el NIT con un DV equivocado.<br>2. Agregarlo con el DV correcto. | Rechaza el errado y dice el correcto; con el correcto lo agrega y muestra las dos variantes que se consultan. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CFG-02 | A | Agregar placas | — | 1. Pegar 3 placas: una nueva, una repetida, una inválida. | Agrega la nueva y explica la repetida y la inválida. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CFG-03 | B | Placas por Excel | — | 1. Descargar la plantilla.<br>2. Subirla con filas buenas y malas. | Informa agregadas, existentes, repetidas e inválidas. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CFG-04 | B | Desactivar y reactivar un NIT | NIT activo. | 1. Desactivarlo.<br>2. Reactivarlo.<br>3. Ver su historial. | Pide confirmación; historial con quién y cuándo; sus comparendos se conservan. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CFG-05 | A | Representante legal completo | — | 1. Crear un representante con firma, cédula, certificado y Escritura o poder.<br>2. Darle permiso de DP y de cambio de contraventor. | Queda vigente; desaparece «Sin representante legal» de los faltantes. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CFG-06 | B | Bloquear un organismo | — | 1. Configuración → Organismos de tránsito: ver los correos y el permiso (solo lectura).<br>2. Bloquear el cambio ante ese organismo. | Se guarda; en un comparendo de ese organismo la condición «organismo no bloqueado» falla. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CFG-07 | B | Remitente, copias y destinatarios | — | 1. Cambiar nombre del remitente, correo de respuesta, CC/CCO de avisos y destinatarios de reportes. | Se guarda y queda en el historial. El siguiente correo lo usa (verificar en Centro de mensajes). | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CFG-08 | C | Agenda visible sin proveedor | — | 1. Ver Empresa y correo. | Muestra días, horas, última y próxima consulta. No menciona proveedores, API ni nada técnico. | ☐ OK ☐ Falla ☐ Bloq. |

### Fuentes e ingesta (SuperAdmin) (CP-ING · 6 casos)

| Código | Prior. | Caso | Antes | Pasos | Esperado | Resultado |
|---|---|---|---|---|---|---|
| CP-ING-01 | A | Ejecutar una consulta a mano | Empresa con configuración Activa y NIT vigilado. | 1. Trabajos e ingesta → la empresa → SIMIT por NIT → Ejecutar ahora.<br>2. Esperar a que termine. | El ciclo queda Exitoso con nuevos, actualizados y sin cambios. Los nuevos aparecen en la bandeja con organismo, plazos y línea de tiempo. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-ING-02 | A | Segunda corrida | Ciclo anterior hecho. | 1. Ejecutar de nuevo. | No duplica; los que no cambiaron cuentan como sin cambios. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-ING-03 | B | Agenda | — | 1. Cambiar la agenda de un canal (horas o intervalo, solo días hábiles).<br>2. Guardar. | Se entiende en lenguaje normal; la empresa la ve en Empresa y correo; la próxima consulta coincide. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-ING-04 | B | Falla de la fuente | Una fuente caída o un municipio con error (preguntar cómo provocarlo). | 1. Ejecutar el canal. | El ciclo queda Parcial o Fallido; no se inactiva ningún comparendo de esa clave; se ve la respuesta del error; llega la alerta de integración. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-ING-05 | B | Trabajos programados | — | 1. Ver la lista de trabajos (reintento del conductor, réplica de organismos, reportes).<br>2. Ejecutar ahora la réplica de organismos. | Horarios en lenguaje normal; la réplica actualiza el catálogo sin tocar correos, permisos ni alias; «última ejecución» se actualiza. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-ING-06 | C | Log de migración | — | 1. Abrir el log y filtrar por la empresa. | Cada consulta con resultado, tipo de error, duración y cantidad. | ☐ OK ☐ Falla ☐ Bloq. |

### Bandeja (CP-BAN · 9 casos)

| Código | Prior. | Caso | Antes | Pasos | Esperado | Resultado |
|---|---|---|---|---|---|---|
| CP-BAN-01 | A | Tarjetas de estado legal | Comparendos en varios estados. | 1. Clic en «Vencido».<br>2. Clic otra vez. | Filtra y el conteo coincide con las filas; el segundo clic quita el filtro. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-BAN-02 | A | Búsqueda por número y placa | — | 1. Escribir parte de un número.<br>2. Escribir una placa. | Filtra sola en menos de un segundo, sin pulsar botón. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-BAN-03 | A | Búsqueda rápida | — | 1. Probar Próximos a vencer, Sin contraventor, Rechazados por OT y Sin gestión. | Cada una filtra lo que dice; se ve como etiqueta y se quita con un clic. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-BAN-04 | B | Filtros con «no es» y listas | — | 1. Filtros → Organismo «no es» Bogotá.<br>2. Filtro de placas pegando 5 placas desde Excel. | Excluye Bogotá; muestra solo esas placas; las etiquetas muestran lo aplicado. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-BAN-05 | B | Activos e inactivos | — | 1. Quitar la etiqueta «Activos». | Aparecen también los inactivos. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-BAN-06 | B | Columnas recordadas | — | 1. Ocultar 2 columnas y mostrar una oculta.<br>2. Recargar la página. | Se mantiene la elección. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-BAN-07 | A | Orden por urgencia | — | 1. Abrir la bandeja sin filtros. | Arriba lo que vence primero; el control de orden se entiende. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-BAN-08 | A | Exportar | — | 1. Exportar con un filtro pequeño.<br>2. Exportar más de 5.000 filas si hay datos. | Descarga con solo las columnas visibles y valores legibles. Más de 5.000 → varios archivos de 5.000 en el mismo clic, sin repetidos. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-BAN-09 | B | Menú de la fila | — | 1. Abrir el menú de una fila → Registrar pago (en uno ya pagado). | Lleva a la sección del detalle; en un pagado, «Registrar pago» está deshabilitado. | ☐ OK ☐ Falla ☐ Bloq. |

### Detalle y línea de tiempo (CP-DET · 5 casos)

| Código | Prior. | Caso | Antes | Pasos | Esperado | Resultado |
|---|---|---|---|---|---|---|
| CP-DET-01 | A | Encabezado | Comparendo en tiempo. | 1. Abrir el detalle. | Estado legal, último hito, descuento con fecha límite, organismo, fuente y valor; las fechas cuadran con la tabla de descuentos. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-DET-02 | A | Línea de tiempo | Comparendo con varias acciones. | 1. Revisar la línea de tiempo.<br>2. Filtrar por tipo.<br>3. Abrir un correo desde ella. | Cada acción con fecha, quién y resultado; el correo abre el Centro de mensajes. Sin palabras técnicas (API, prompt, proveedor). | ☐ OK ☐ Falla ☐ Bloq. |
| CP-DET-03 | B | Observación | — | 1. Agregar una observación. | Queda en la línea de tiempo y no se puede editar. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-DET-04 | B | Inactivar a mano | Comparendo activo de prueba (no real). | 1. Inactivar con motivo y doble confirmación. | Queda inactivo con el motivo. Si la fuente lo vuelve a traer, sigue inactivo. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-DET-05 | C | Actualización tras una acción | — | 1. Asignar un conductor y mirar la línea de tiempo sin recargar. | La línea de tiempo muestra el evento nuevo. | ☐ OK ☐ Falla ☐ Bloq. |

### Conductor (CP-CON · 7 casos)

| Código | Prior. | Caso | Antes | Pasos | Esperado | Resultado |
|---|---|---|---|---|---|---|
| CP-CON-01 | A | Asignar a mano sin declaración | Comparendo sin conductor. | 1. Llenar los datos sin marcar la declaración de datos.<br>2. Marcarla y guardar. | Sin la declaración no deja; con ella asigna con origen Manual. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CON-02 | A | Reemplazar el conductor | Comparendo con conductor. | 1. Reemplazar sin motivo.<br>2. Reemplazar con motivo. | Exige motivo; el historial muestra anterior → nuevo, quién y cuándo. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CON-03 | B | Consulta automática (Renting) | NIT con consulta automática y placa de la flota de Renting. | 1. Abrir un comparendo nuevo de ese NIT. | Conductor asignado con origen automático, correo desviado al buzón de pruebas y documento pendiente. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CON-04 | B | No encontrado | Placa que Renting no tiene. | 1. Consultar el conductor. | Queda «Pendiente de asignación manual» (naranja) y aparece en «Sin contraventor». | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CON-05 | B | Completar documento pendiente | Conductor con documento pendiente. | 1. Completar el documento. | Se guarda; deja de bloquear el cambio de contraventor. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CON-06 | B | Cédula y licencia | Conductor con documento. | 1. Cargar cédula y licencia.<br>2. Cargar otra licencia. | Quedan vigentes; la anterior queda como reemplazada. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CON-07 | C | Portal del conductor | Aviso enviado. | 1. Abrir el enlace del portal (desde el Centro de mensajes). | Muestra el comparendo, el descuento vigente y cómo pagar, con la marca correcta (FLIT o Renting). | ☐ OK ☐ Falla ☐ Bloq. |

### Avisos (CP-AV · 10 casos)

| Código | Prior. | Caso | Antes | Pasos | Esperado | Resultado |
|---|---|---|---|---|---|---|
| CP-AV-01 | A | Aviso 1 a mano | Comparendo en tiempo, con conductor con correo del buzón de pruebas. | 1. Detalle → Avisos → enviar el que propone. | Propone el aviso 1; sale; el Centro de mensajes lo muestra enviado al buzón de pruebas; el hito cambia a Aviso 1. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-AV-02 | A | Contenido FLIT | Empresa con «Colas FLIT». | 1. Ver el aviso en el Centro de mensajes. | Diseño FLIT; dice el descuento según la ventana real (no ofrece 50 % si ya no aplica); nombres en formato normal, no en mayúsculas. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-AV-03 | A | Contenido Renting | Empresa con «API Renting cliente». | 1. Enviar un aviso y verlo en el Centro de mensajes. | Textos e imagen de Renting, sin nada de FLIT, por la cuenta de Renting, desviado al buzón de pruebas. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-AV-04 | A | Adjuntos | Representante con firma y poder. | 1. Enviar un aviso 1. | Adjunta la autorización de cambio de infractor firmada y el poder; si se cargó por OCR, también el documento. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-AV-05 | B | Falta la firma | Representante sin firma. | 1. Enviar un aviso. | El aviso sale sin la autorización y la línea de tiempo dice qué faltó. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-AV-06 | A | Sin correo del conductor | Conductor sin correo. | 1. Intentar enviar el aviso. | No deja y explica por qué. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-AV-07 | B | Pagado o inactivo | Comparendo pagado. | 1. Intentar enviar el aviso. | No deja y explica por qué. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-AV-08 | B | Aviso en lote | Varios comparendos seleccionados, mezclando aptos y no aptos. | 1. Bandeja → Enviar aviso. | Dice cuántos salieron y, de los omitidos, el motivo de cada uno. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-AV-09 | B | Automático diario | Comparendo apto sin aviso, día hábil. | 1. Esperar a la corrida de las 7:00 (o pedir al SuperAdmin que ejecute el trabajo).<br>2. Revisar al día siguiente. | Sale el aviso que toca, solo uno por comparendo en el día. En fin de semana o festivo no sale nada. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-AV-10 | C | Horario de avisos | SuperAdmin. | 1. Cambiar la hora, pausar y reanudar.<br>2. Ver el historial. | Se guarda y queda auditado. | ☐ OK ☐ Falla ☐ Bloq. |

### Cambio de contraventor (CP-CC · 7 casos)

| Código | Prior. | Caso | Antes | Pasos | Esperado | Resultado |
|---|---|---|---|---|---|---|
| CP-CC-01 | A | Lista de condiciones | Comparendo con alguna condición faltante (por ejemplo, sin licencia). | 1. Abrir la sección. | Lista cada condición y marca cuál falla; el botón Solicitar no deja. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CC-02 | A | Vista previa | Todas las condiciones cumplidas. | 1. Ver el formato y la carta. | Dos PDF con «VISTA PREVIA», con el NIT del propietario y el representante correctos. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CC-03 | A | Solicitar (solo en DEV) | Condiciones cumplidas; organismo con correo del buzón de pruebas. | 1. Solicitar.<br>2. Ver el Centro de mensajes. | Correo al organismo (buzón de pruebas) con formato firmado, cédula, licencia y poder; luego el aviso 4 al conductor con la autorización. Estado «En proceso». | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CC-04 | A | Registrar rechazo | Solicitud en proceso. | 1. Registrar rechazado con causal corta (menos de 10 caracteres).<br>2. Con causal completa. | Exige la causal; queda «Rechazado OT», aparece en «Rechazados por OT». | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CC-05 | B | Volver a solicitar | Rechazado. | 1. Solicitar de nuevo. | Queda el historial de intentos. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CC-06 | A | Registrar aceptación | Solicitud en proceso. | 1. Registrar aceptado con fecha. | Estado «Exitoso»; la línea de tiempo lo registra. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-CC-07 | C | Organismo bloqueado | Organismo bloqueado por la empresa. | 1. Abrir la sección. | Muestra el bloqueo como condición que falla. | ☐ OK ☐ Falla ☐ Bloq. |

### Derecho de petición (CP-DP · 6 casos)

| Código | Prior. | Caso | Antes | Pasos | Esperado | Resultado |
|---|---|---|---|---|---|---|
| CP-DP-01 | A | Vista previa | Comparendo en tiempo con representante con permiso. | 1. Detalle → DP → elegir tipología → vista previa. | Destinatarios, CC y CCO, radicado, firma y anexos; solo ofrece tipologías que aplican al estado. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-DP-02 | A | Enviar individual (solo en DEV) | — | 1. Enviar. | Radicado con formato PREFIJO-AAMMDDHHMMSS-mmm; estado Enviado; correo al buzón de pruebas en el Centro de mensajes. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-DP-03 | A | Segundo DP | Comparendo con DP. | 1. Intentar otro DP. | No deja: un DP por comparendo. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-DP-04 | B | Lote | Selección con algunos que ya tienen DP. | 1. Bandeja → Enviar derecho de petición.<br>2. Ver Envíos masivos de DP y exportar el resumen. | Omite los que ya tienen DP o no cumplen, con motivo; el resumen baja en Excel. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-DP-05 | A | Registrar respuesta | DP enviado. | 1. Registrar favorable con radicado, fecha y PDF.<br>2. Intentar otra respuesta. | Queda registrada; no admite una segunda; el filtro «Resultado del DP» la encuentra. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-DP-06 | B | Bloqueos | Comparendo pagado; empresa sin representante. | 1. Intentar un DP en cada caso. | No deja y explica; sin representante, además alerta a los encargados. | ☐ OK ☐ Falla ☐ Bloq. |

### Pagos (CP-PAG · 2 casos)

| Código | Prior. | Caso | Antes | Pasos | Esperado | Resultado |
|---|---|---|---|---|---|---|
| CP-PAG-01 | A | Pago individual | Comparendo sin pagar. | 1. Registrar pago desde el detalle. | Queda pagado con quién lo registró; deshabilita Registrar pago, avisos y DP. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-PAG-02 | A | Carga por Excel | — | 1. Descargar la plantilla.<br>2. Subir con 2 filas buenas y 2 malas. | Registra las buenas; explica cada error; el historial dice quién cargó. | ☐ OK ☐ Falla ☐ Bloq. |

### Carga por OCR (CP-OCR · 8 casos)

| Código | Prior. | Caso | Antes | Pasos | Esperado | Resultado |
|---|---|---|---|---|---|---|
| CP-OCR-01 | A | Nuevo paquete | OCR contratado; 3 a 5 archivos (PDF, imagen, TIFF). | 1. Crear paquete y subir los archivos. | Acepta los formatos y procesa; contadores de completos, incompletos, fallidos y duplicados. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-OCR-02 | A | Revisión | Documento incompleto. | 1. Abrir en vista dividida.<br>2. Completar los campos que faltan. | Marcas donde revisar; la fecha de notificación tomada de un sello pide confirmarla; la corrección queda auditada. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-OCR-03 | B | Reprocesar | Documento incompleto ya corregido. | 1. Reprocesar.<br>2. Reprocesar en lote. | Vuelve a leer y conserva lo corregido; máximo 3 veces. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-OCR-04 | A | Registrar | Documentos completos. | 1. Registrar. | Crea los comparendos con fuente OCR y el documento adjunto; aparecen en la bandeja. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-OCR-05 | A | Duplicado | Subir un comparendo que ya existe. | 1. Procesar. | Queda como duplicado, no se registra y enlaza al original. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-OCR-06 | B | Exportar paquete | — | 1. Exportar el paquete. | Excel con los datos de cada documento. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-OCR-07 | B | Sin tecnología a la vista | Usuario de empresa. | 1. Recorrer la pantalla de OCR. | No menciona prompt, modelo, proveedor ni costos (eso solo lo ve el SuperAdmin). | ☐ OK ☐ Falla ☐ Bloq. |
| CP-OCR-08 | C | Versiones de lectura (SuperAdmin) | — | 1. Crear versión → Evaluar → Publicar.<br>2. Volver a la versión anterior. | Sin evaluación aprobada no publica; la anterior vuelve sin evaluar; todo auditado. | ☐ OK ☐ Falla ☐ Bloq. |

### Centro de mensajes (CP-MSG · 3 casos)

| Código | Prior. | Caso | Antes | Pasos | Esperado | Resultado |
|---|---|---|---|---|---|---|
| CP-MSG-01 | A | Ver un correo | Correo enviado. | 1. Abrir el correo. | Remitente, destinatario, copias, estado, intentos, adjuntos y el correo tal como salió. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-MSG-02 | B | Filtros | — | 1. Filtrar por tipo y estado. | La lista cambia correctamente. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-MSG-03 | C | Apertura | Aviso abierto en el buzón de pruebas. | 1. Ver el estado. | Pasa a «Abierto» (posiblemente abierto). | ☐ OK ☐ Falla ☐ Bloq. |

### Reportes (CP-REP · 4 casos)

| Código | Prior. | Caso | Antes | Pasos | Esperado | Resultado |
|---|---|---|---|---|---|---|
| CP-REP-01 | A | Dashboard | Reportes contratado. | 1. Abrir el Dashboard con periodo y NIT. | Indicadores y tabla por NIT coherentes con la bandeja. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-REP-02 | A | Radar de vencimientos | — | 1. Clic en el radar. | Lleva a la bandeja filtrada con la misma cantidad. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-REP-03 | B | Productividad | — | 1. Abrir Productividad y el detalle de un usuario.<br>2. Exportar. | Cuenta lo gestionado en el periodo; el detalle dice qué hizo; exporta. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-REP-04 | C | Sin reportes contratados | Empresa sin la capacidad. | 1. Buscar Reportes en el menú. | No aparece para la empresa (el SuperAdmin sí lo ve). | ☐ OK ☐ Falla ☐ Bloq. |

### Administración FLIT (CP-ADM · 6 casos)

| Código | Prior. | Caso | Antes | Pasos | Esperado | Resultado |
|---|---|---|---|---|---|---|
| CP-ADM-01 | B | Festivo | SuperAdmin. | 1. Calendario: agregar un festivo en un día hábil próximo.<br>2. Quitarlo. | Dice cuántos comparendos recalcula y lo hace; al quitarlo vuelve todo como estaba. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-ADM-02 | B | Alias de secretaría | Comparendo sin organismo. | 1. Catálogo de organismos → Alias → crear alias con ese texto. | Los comparendos sin organismo con ese texto quedan asociados. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-ADM-03 | B | Plantillas | — | 1. Editor: abrir un aviso de FLIT y uno de Renting, ver versiones y vista previa. | FLIT con su diseño; Renting sin nada de FLIT. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-ADM-04 | C | Fuentes municipales | — | 1. Desactivar y activar un municipio. | Queda en la auditoría. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-ADM-05 | C | Alertas de integración | — | 1. Configurar destinatario, filtro y umbral. | Se guarda; la siguiente falla avisa según el umbral. | ☐ OK ☐ Falla ☐ Bloq. |
| CP-ADM-06 | C | Tipologías | — | 1. Crear, editar y desactivar una tipología de prueba. | Desactivar respeta a las empresas que ya la tenían; historial. | ☐ OK ☐ Falla ☐ Bloq. |

## Cómo reportar un error

- En ADO, proyecto **FLIT - GESTION COMPARENDOS**, como Bug.
- Título: código del caso + qué falla (por ejemplo «CP-AV-04: el aviso 2 sale aunque el 1 salió con 25 %»).
- Incluye: ambiente (DEV o QA), usuario y rol, número del comparendo, pasos, qué esperabas, qué pasó, captura de pantalla y la hora exacta.
- Si es un correo, incluye lo que muestra el Centro de mensajes.
- Si algo puede haber llegado a un destinatario real, avisa de inmediato a la persona encargada y a Jorman.
