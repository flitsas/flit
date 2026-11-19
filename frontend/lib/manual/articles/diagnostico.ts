import type { ManualArticle } from "../types";

/**
 * HU #14577 — sección «Diagnóstico vehicular»: el producto de la Suite y su saneamiento (ADR-0087).
 *
 * Diagnóstico corre en su propia aplicación (`frontend-diagnostico`), con su login en el hub. Los rótulos salen de las
 * pantallas reales (`frontend-diagnostico/components/**`, `lib/**`) y de los nombres accesibles congelados en
 * `frontend-diagnostico/e2e/*.spec.ts`; las reglas, de `docs/diagnosticos/` y de los ADR-0072 a 0080, 0084, 0086 y 0087.
 *
 * Audiencias: lo que opera la compañía va como «Gestor» (lo ven también el Admin de Compañía y el Super Admin); la
 * configuración operativa, «Admin de Compañía»; lo que solo hace el equipo FLIT (backoffice, llenado a mano, ajuste del
 * dictamen, gestión del saneamiento), «Super Admin». Ningún artículo usa placas reales: el ejemplo es ABC123.
 */
export const DIAGNOSTICO_ARTICLES: ManualArticle[] = [
  {
    slug: "6-diagnostico/1-que-es",
    title: "Diagnóstico vehicular: qué es, cómo entrar y quién puede hacer qué",
    audience: "Gestor",
    sectionId: "diagnostico",
    keywords: [
      "diagnostico vehicular",
      "que es diagnostico",
      "producto diagnostico",
      "suite flit",
      "hub",
      "mis productos",
      "entrar a diagnostico",
      "suscripcion de diagnostico",
      "usuario de diagnostico",
      "administrador de diagnostico",
      "rol de diagnostico",
      "no tienes acceso",
      "aislamiento por compania",
      "dock de diagnostico",
      "inicio diagnostico",
      "enlaces guardados",
      "no tienes permisos en diagnostico",
      "sin rol en diagnostico",
    ],
    summary:
      "Para qué sirve Diagnóstico, cómo se abre desde el hub de la Suite, qué roles existen y qué ve cada uno.",
    blocks: [
      {
        id: "que-es",
        title: "1. Qué es Diagnóstico vehicular",
        paragraphs: [
          "Diagnóstico vehicular es un producto de la Suite FLIT, con aplicación propia, separado de Trámites. Tu compañía consulta varias fuentes externas sobre los vehículos de su flota y recibe un consolidado por vehículo y, si lo pide, un dictamen orientado a un tipo de trámite.",
          "La unidad de trabajo es el vehículo, por placa o por VIN. Un diagnóstico agrupa uno o varios vehículos (un lote) que se consultan con los mismos parámetros. Las fuentes son: RUNT del vehículo, RUNT del propietario y del locatario, SIMIT del vehículo, del propietario y del locatario, avalúo comercial e impuesto departamental.",
        ],
        bullets: [
          "Simple: consulta las fuentes elegidas y entrega el consolidado, sin dictamen.",
          "Con análisis: además emite un dictamen Viable o Bloqueado para un tipo de trámite de la familia traspaso. El veredicto lo da un motor de reglas; la IA solo redacta la explicación.",
          "Después de diagnosticar puedes crear el trámite en Trámites o pedir a FLIT el saneamiento del vehículo.",
        ],
      },
      {
        id: "entrar",
        title: "2. Cómo se entra",
        paragraphs: [
          "Diagnóstico se abre desde el hub de la Suite: la tarjeta «Diagnóstico» del inicio o el menú de productos. No tiene un formulario de inicio de sesión propio: si llegas sin sesión, te lleva al login del hub y, al ingresar, vuelve a Diagnóstico. Cerrar sesión en cualquier producto la cierra en toda la Suite.",
          "La tarjeta solo aparece si tu compañía tiene Diagnóstico activo. Si el producto está apagado o la suscripción suspendida, verás «No tienes acceso» con el aviso «Diagnóstico no está habilitado para tu empresa o su suscripción está suspendida» y el botón «Ir a mis productos». Los diagnósticos ya guardados no se pierden por una suspensión.",
          "Al entrar se abre el «Dashboard» de Diagnóstico, con el resumen de tus diagnósticos del último mes (lo explica el artículo «Dashboard de Diagnóstico»). El botón central del dock, «Inicio Diagnóstico», siempre te devuelve ahí.",
          "El dock de Diagnóstico tiene a la izquierda «Diagnósticos» (el listado de lotes y de vehículos), «Reportes» y, si eres del equipo FLIT, la sección «Backoffice»; a la derecha, «Ajustes → Configuración». Cada entrada aparece solo si tu rol la permite. También puedes saltar a cualquiera con Ctrl K.",
        ],
        callouts: [
          {
            variant: "tip",
            title: "Tus enlaces guardados siguen sirviendo",
            text: "Antes el listado de diagnósticos estaba en la página de inicio. Si tienes enlaces guardados o marcadores de esa página con una pestaña o un filtro (por ejemplo la pestaña «Vehículos»), te llevan solos al listado «Diagnósticos» con los mismos filtros.",
          },
        ],
      },
      {
        id: "sin-acceso",
        title: "4. Si ves «No tienes acceso»",
        paragraphs: [
          "La pantalla «No tienes acceso» dice el motivo y ofrece «Ir a mis productos». Los mensajes más comunes:",
        ],
        bullets: [
          "«Diagnóstico no está habilitado para tu empresa o su suscripción está suspendida.»: el producto está apagado para tu compañía. Escribe a tu ejecutivo de cuenta.",
          "«No tienes permisos en Diagnóstico. Pídele acceso a tu administrador.» o «No tienes un rol en Diagnóstico…»: tu usuario no tiene un rol del producto. Lo asigna el administrador de tu compañía en el hub.",
          "«Tu usuario no tiene una empresa asociada…»: tu usuario no pertenece a ninguna compañía. Pide ayuda a tu administrador.",
          "«Tu cuenta cambió o fue suspendida. Vuelve a iniciar sesión.»: tu sesión dejó de ser válida.",
          "«No tienes permiso para abrir esta página.»: tu rol no incluye esa pantalla, por ejemplo «Reportes» sin el permiso de ver reportes.",
        ],
      },
      {
        id: "roles",
        title: "5. Roles y permisos",
        paragraphs: [
          "Los usuarios y los roles se administran en el hub, no en Diagnóstico. Un rol de Trámites no da acceso a Diagnóstico: hace falta un rol del producto.",
        ],
        bullets: [
          "Usuario de Diagnóstico: ve el Dashboard de su compañía, crea diagnósticos y carga vehículos, ve el tablero y el detalle de cada consulta, descarga PDF, Excel y ZIP, crea trámites, ve y exporta los «Reportes» de su compañía, ve la configuración en solo lectura y solicita saneamientos.",
          "Administrador de Diagnóstico: lo mismo, más editar la pestaña «Operación» de la configuración y reprocesar consultas en error. El administrador de la compañía lo recibe por defecto.",
          "Equipo FLIT (Super Admin): el backoffice entre compañías, el contrato de cada compañía, el llenado a mano de consultas, el ajuste del dictamen, las fuentes obligatorias por trámite, la gestión de los saneamientos y la vista global del Dashboard y de los Reportes, con el selector «Compañía». El backoffice no crea diagnósticos: los crea la compañía que usa el producto.",
          "Ver los reportes en pantalla y exportarlos son dos permisos distintos: si ves la tabla pero no los botones «Exportar Excel» y «Exportar PDF», tu rol no incluye la descarga de reportes.",
        ],
        callouts: [
          {
            variant: "info",
            title: "Tus datos son de tu compañía",
            text: "Una sesión es una compañía: nunca ves diagnósticos, vehículos ni avisos de otra. El equipo FLIT solo lee otra compañía desde el backoffice, eligiéndola de forma explícita, y cada lectura queda auditada.",
          },
        ],
      },
    ],
  },
  {
    slug: "6-diagnostico/2-inicio-y-vehiculos",
    title: "Listado «Diagnósticos»: pestañas «Diagnósticos» y «Vehículos»",
    audience: "Gestor",
    sectionId: "diagnostico",
    keywords: [
      "inicio de diagnostico",
      "pestana diagnosticos",
      "pestana vehiculos",
      "todas las placas",
      "placas de la compania",
      "lotes de la compania",
      "estado del vehiculo",
      "modalidad express masivo",
      "abrir el diagnostico",
      "ver deuda",
      "descargar deuda",
      "listado de diagnosticos",
      "donde quedo el listado",
    ],
    summary:
      "El listado que abre «Diagnósticos» del dock: los diagnósticos (lotes) de tu compañía y todas las placas diagnosticadas, con sus filtros.",
    blocks: [
      {
        id: "inicio",
        title: "1. El listado",
        paragraphs: [
          "El listado se abre con «Diagnósticos» del dock (antes era la página de inicio, que ahora es el Dashboard). Arriba está el panel para empezar: «Placa o VIN» con «Diagnosticar en línea» (Express, un vehículo) y la tarjeta del masivo con «Nuevo diagnóstico masivo» y «Descargar plantilla». Debajo, dos pestañas: «Diagnósticos» y «Vehículos». La pestaña elegida queda en la dirección de la página, así que puedes compartir el enlace o volver a ella.",
        ],
      },
      {
        id: "diagnosticos",
        title: "2. Pestaña «Diagnósticos»",
        paragraphs: [
          "La tabla «Diagnósticos de la compañía» lista cada lote con Nombre, Modalidad (Express o Masivo), Tipo (Simple o Con análisis), Trámite analizado, Estado, Creado y Creador. Pulsa el nombre para abrir el tablero del lote.",
          "Filtros: rango de fechas (desde y hasta), tipo, estado y «Buscar por nombre del diagnóstico». Si no hay resultados verás «Ningún diagnóstico coincide»; si aún no hay diagnósticos, «Aún no tienes diagnósticos».",
          "Qué lotes ves depende de la configuración «Quién ve los diagnósticos»: por defecto, todos los de la compañía; si el administrador eligió «Solo los propios», cada usuario ve los que creó y el administrador los ve todos.",
        ],
      },
      {
        id: "vehiculos",
        title: "3. Pestaña «Vehículos»",
        paragraphs: [
          "La tabla «Vehículos de la compañía» muestra todas las placas de tus diagnósticos, en proceso y terminadas, de todos los lotes. Columnas: Placa, Diagnóstico (enlace al lote), Modo, Estado (En proceso o Terminado), Dictamen (Viable, Bloqueado o Sin dictamen), Saneamiento, Trámite y Fecha.",
          "Filtros: Placa (se aplica al pulsar Enter o al salir del campo), Estado del vehículo, Dictamen, Saneamiento (Con saneamiento, Sin saneamiento o un estado concreto), Trámite (Con trámite o Sin trámite), Modo y fechas. «Limpiar filtros» los quita. La tabla se pagina con «Filas por página».",
          "Desde cada fila puedes abrir la ventana «Deuda y saneamiento» («Ver deuda de la placa…») y solicitar el saneamiento. Si seleccionas varias placas aparecen «Sanear (N)» y «Descargar deuda (N)», que descarga un solo Excel con las placas elegidas.",
        ],
        callouts: [
          {
            variant: "tip",
            text: "La pestaña «Vehículos» es la forma rápida de ver qué placas tienen saneamiento en curso sin abrir lote por lote.",
          },
        ],
      },
    ],
  },
  {
    slug: "6-diagnostico/3-nuevo-diagnostico",
    title: "Nuevo diagnóstico: Express o Masivo, Simple o Con análisis",
    audience: "Gestor",
    sectionId: "diagnostico",
    keywords: [
      "nuevo diagnostico",
      "diagnostico express",
      "diagnosticar en linea",
      "placa o vin",
      "diagnostico masivo",
      "archivo de vehiculos",
      "plantilla identificador_vehiculo",
      "documento_locatario",
      "validar archivo",
      "resumen de validacion",
      "continuar solo con los validos",
      "modo simple",
      "con analisis",
      "alcance del diagnostico",
      "fuentes obligatorias",
      "estimado",
      "limite de vehiculos por lote",
      "parametros arriba",
      "una columna",
      "informacion general modo alcance",
      "cargar vehiculos",
      "diagnostico sin lanzar",
      "subir archivo revisar iniciar",
    ],
    summary:
      "Cómo crear un diagnóstico de un vehículo o de un archivo, qué fuentes elegir y qué cambia entre los modos Simple y Con análisis.",
    blocks: [
      {
        id: "express",
        title: "1. Express: un vehículo en línea",
        paragraphs: [
          "En el listado «Diagnósticos» escribe la placa o el VIN en «Placa o VIN» (por ejemplo ABC123) y pulsa «Diagnosticar en línea». Se abre «Nuevo diagnóstico» en modo Express con el vehículo precargado y un «Nombre del diagnóstico» propuesto a partir del identificador y la fecha, que puedes cambiar.",
          "Opcionalmente indica el documento del locatario (tipo y número). Se guarda cifrado y en pantalla solo se ven los 4 últimos caracteres. Pulsa «Continuar» o «Iniciar diagnóstico». Si tu compañía muestra el estimado antes de lanzar, revisa las consultas previstas y pulsa «Lanzar diagnóstico».",
          "Al final ves «Diagnóstico iniciado con 1 vehículo» y «Ver tablero»; la aplicación te lleva sola al tablero.",
        ],
      },
      {
        id: "masivo",
        title: "2. Masivo: varios vehículos desde un archivo",
        paragraphs: [
          "Pulsa «Nuevo diagnóstico masivo» (o elige «Masivo» en el selector «Tipo de diagnóstico»). Llena los parámetros, descarga la plantilla si la necesitas y sube el «Archivo de vehículos (.xlsx o .csv)», de máximo 5 MB, con una fila por vehículo: la columna obligatoria Identificador_Vehiculo (placa o VIN) y, si lo tienes, el documento del locatario (Tipo_Documento_Locatario y Documento_Locatario).",
          "Pulsa «Validar archivo» en la barra del final de la pantalla. La validación no dispara consultas externas: solo revisa el archivo. El «Resumen de validación» muestra los válidos, los que tienen error, los duplicados que se consolidaron en una sola fila y los válidos frente al límite de vehículos por lote de tu contrato.",
          "Si hay filas con error, corrígelas en la tabla (se revalidan solas al dejar de escribir y quedan «Corregida» o «Inválido» con el motivo) o pulsa «Continuar solo con los válidos». Sin ningún válido no se puede continuar. Una vez validado el archivo, el selector Express o Masivo queda bloqueado; para cambiar parámetros o archivo usa «Subir otro archivo».",
        ],
        bullets: [
          "Placas reconocidas: particular o público (tres letras y tres números, como ABC123), moto, remolque o semirremolque y maquinaria. VIN: 17 caracteres, sin las letras I, O ni Q.",
          "Lo que no es una placa ni un VIN válido se marca como inválido y nunca dispara una consulta. El RUNT confirma en definitiva.",
          "El texto se normaliza a mayúsculas y sin espacios.",
        ],
        media: [
          {
            kind: "diagram",
            id: "flujo-diagnostico-alta",
            alt: "Flujo de alta de un diagnóstico. Desde el inicio, con un vehículo vas a Express: escribes la placa o el VIN y pulsas Diagnosticar en línea; luego eliges nombre, alcance y modo. Con muchos vehículos vas a Masivo: llenas los parámetros, subes el archivo y pulsas Validar archivo; si hay filas con error las corriges o sigues solo con los válidos, y confirmas. En ambos casos inicias el diagnóstico y las consultas se siguen en el tablero.",
            caption: "Alta de un diagnóstico Express o Masivo.",
          },
        ],
      },
      {
        id: "parametros",
        title: "3. Parámetros: alcance y modo",
        paragraphs: [
          "La pantalla de alta va en una sola columna. Arriba, a lo ancho, la tarjeta «Parámetros» con tres partes en este orden: «Información general», «Modo» y «Alcance». En una pantalla ancha se ven las tres lado a lado; en una mediana, «Información general» y «Modo» arriba y «Alcance» debajo; en el celular, una debajo de la otra. Debajo va la tarjeta «Vehículos» (la placa o el archivo) y, al final, una barra con el estimado de consultas, los avisos, «Cancelar» y el botón para continuar. El orden en que avanzas con la tecla Tab es el mismo que ves.",
          "El «Alcance» agrupa las fuentes por vehículo, por propietario y por locatario. Solo ves las que tu compañía tiene contratadas y vienen marcadas las fuentes por defecto de la configuración. El RUNT del vehículo siempre se consulta: es la base del diagnóstico y va marcado sin poder quitarse.",
          "Las fuentes del propietario dependen del propietario que reporta el RUNT del vehículo. Las del locatario dependen del documento que declares en la carga; sin documento, quedan como Novedad «Sin documento del locatario» y no se consumen.",
          "«Modo»: Simple consulta las fuentes y entrega el consolidado. Con análisis suma el dictamen y exige elegir el tipo de trámite (la familia traspaso: traspaso, traspaso unilateral y transferencia de dominio). En Con análisis las fuentes que exige ese trámite quedan marcadas y bloqueadas con el aviso «Fuente obligatoria para el tipo de trámite elegido»; si alguna no está en tu contrato verás «Fuentes obligatorias no contratadas» y no podrás lanzar.",
        ],
        callouts: [
          {
            variant: "warning",
            text: "Los parámetros aplican a todos los vehículos del lote y no se cambian después de lanzar. Un lote no mezcla modos ni tipos de trámite: si necesitas dos trámites distintos, crea dos diagnósticos.",
          },
        ],
      },
      {
        id: "cargar-vehiculos",
        title: "4. Un diagnóstico guardado sin vehículos: «Cargar vehículos»",
        paragraphs: [
          "Si un diagnóstico masivo quedó guardado sin confirmar los vehículos (por ejemplo, saliste antes de terminar), su tablero lo muestra como creado y sin lanzar, con el botón «Cargar vehículos». Ahí retomas la carga con sus parámetros ya guardados, en tres pasos: «Subir archivo», «Revisar» e «Iniciar».",
          "Sube el archivo, revisa el resumen de validación, corrige o descarta las filas con error y pulsa iniciar. Si los vehículos no se pudieron confirmar verás «No se confirmaron los vehículos» con el motivo y, cuando aplica, la opción de reintentar. Sin el permiso de crear diagnósticos, la pantalla te pide que solicites acceso al administrador de tu compañía.",
        ],
      },
    ],
  },
  {
    slug: "6-diagnostico/4-tablero-y-dictamen",
    title: "Tablero del diagnóstico: semáforo, detalle de cada consulta y dictamen",
    audience: "Gestor",
    sectionId: "diagnostico",
    keywords: [
      "tablero del diagnostico",
      "semaforo",
      "menu acciones",
      "boton acciones",
      "acciones de la fila",
      "cabecera runt simit",
      "ventana del dictamen",
      "semaforo de consultas",
      "detalle de la consulta",
      "respuesta de la fuente",
      "dictamen",
      "viable",
      "bloqueado",
      "alertamientos",
      "ajustado por flit",
      "reprocesar",
      "reintentar pegadas",
      "cerrar lote",
      "estado del lote",
      "motor de reglas",
    ],
    summary:
      "Cómo leer la grilla de semáforos, abrir la respuesta de una fuente, entender el dictamen por reglas y qué significa «Ajustado por FLIT».",
    blocks: [
      {
        id: "grilla",
        title: "1. La grilla de semáforos",
        paragraphs: [
          "El tablero muestra la cabecera del lote (nombre, modo, quién lo creó y cuándo), el estado del lote y la tabla «Semáforo de consultas del lote»: una fila por vehículo y una columna por fuente, más Dictamen y «Acciones». El tablero se actualiza solo cada pocos segundos, sin recargar la página; si pierde la conexión verás «Datos desactualizados».",
          "Las tarjetas de arriba filtran la grilla: Vehículos, Terminados, Con error, Con deuda, Bloqueados y Listos para trámite. También hay un buscador por placa o VIN y el filtro «Con saneamiento / Sin saneamiento».",
          "Las columnas de fuentes van agrupadas: una cabecera «RUNT» y otra «SIMIT», centradas, y debajo de cada una sus subcolumnas «vehículo», «propietario» y «locatario». Siguen «Avalúo» e «Impuesto». En el celular la tabla pasa a tarjetas y cada dato lleva su nombre completo, por ejemplo «RUNT propietario».",
          "Primero se consulta el RUNT del vehículo. Si falla, las demás fuentes de ese vehículo esperan («Espera al RUNT del vehículo») y no se consultan. Con el RUNT listo, el resto corre en paralelo. Si una misma placa y fuente ya se consultó hoy en tu compañía, se reutiliza ese dato sin una consulta nueva.",
        ],
        media: [
          {
            kind: "diagram",
            id: "flujo-diagnostico-consulta-dictamen",
            alt: "Flujo de consulta y dictamen de un vehículo. Primero se consulta el RUNT del vehículo; si no responde, las demás fuentes esperan y no se consultan. Si responde, se consultan en paralelo SIMIT, RUNT y SIMIT de las personas, avalúo e impuesto según el alcance, y cada celda termina en Ejecutado, Con deuda, Novedad o Error. En modo Simple el vehículo queda terminado con su PDF y la opción de crear trámite. En Con análisis el motor de reglas emite Viable o Bloqueado y la IA redacta la explicación; con Viable se puede crear el trámite y con Bloqueado no.",
            caption: "Consulta de las fuentes y dictamen por vehículo.",
          },
        ],
      },
      {
        id: "celda",
        title: "2. El detalle de una consulta",
        paragraphs: [
          "Cada celda es un enlace que abre en una pestaña nueva el detalle de esa consulta, con el título «Fuente · placa». Ahí está la «Respuesta de la fuente» en español, organizada por secciones (por ejemplo comparendos, acuerdos de pago, pólizas o vigencias), con «Expandir todo» y «Contraer todo». Cuando la fuente guardó su respuesta original completa, también aparece en una tarjeta plegable aparte.",
          "La región «Auditoría de consulta» lista los intentos con su inicio, duración, disparador (automático o reproceso) y estado. Si una consulta todavía no ha corrido, el detalle lo dice y puedes volver a abrirlo en un momento.",
        ],
      },
      {
        id: "acciones",
        title: "3. El menú «Acciones» de cada fila; reprocesar y cerrar el lote",
        paragraphs: [
          "La última columna de la tabla, «Acciones», queda fija a la derecha. Cada fila tiene un botón «Acciones» que abre un menú con lo que puedes hacer con ese vehículo, en este orden: «Solicitar saneamiento» (o «Quitar solicitud de saneamiento» o «Ver saneamiento»), «Crear trámite» (o «Ver trámite #N»), «Ver deuda», «Descargar PDF» y «Reintentar consultas». Solo ves las opciones que tu rol permite. Si una opción aplica a tu rol pero no a ese vehículo todavía, aparece atenuada con el motivo escrito debajo (por ejemplo, que el PDF estará listo cuando terminen las consultas).",
          "El menú se maneja también con el teclado: Enter o Espacio lo abre, las flechas, Inicio y Fin recorren las opciones y Escape lo cierra y devuelve el foco al botón. Encima del botón, si el vehículo tiene saneamiento, un rótulo dice su estado (por ejemplo «Solicitado» o «En gestión»). El resultado de cada acción aparece bajo el botón. Si una fila no tiene ninguna acción disponible, verás un guion.",
          "El administrador de Diagnóstico ve «Reprocesar» en las celdas en error: repite solo esa consulta y deja las que ya están en verde. «Reintentar consultas» (en el menú «Acciones») y «Reintentar pegadas (N)» vuelven a encolar las consultas que llevan demasiado tiempo sin avanzar. El usuario de Diagnóstico no ve estas opciones.",
          "Cuando todas las consultas terminan sin error, el lote pasa solo a Completado. Si quedan consultas en error y decides no reprocesarlas, «Cerrar lote» lo deja Incompleto: esas consultas quedan sin resultado y no se vuelven a intentar. Reprocesar o llenar una consulta devuelve el lote a En proceso hasta que vuelva a terminar.",
        ],
      },
      {
        id: "dictamen",
        title: "4. El dictamen por reglas (Con análisis)",
        paragraphs: [
          "Cuando un vehículo cierra sus consultas, el motor de reglas evalúa el catálogo vigente y emite el veredicto: Viable o Bloqueado. Una regla que bloquea deja el dictamen Bloqueado; si solo hay advertencias, o ninguna regla dispara, el dictamen es Viable. La IA redacta la explicación y no cambia el veredicto; mientras redacta verás «redacción IA pendiente» y, si no responde, el dictamen sale con un texto de plantilla.",
          "Pulsa «Dictamen» bajo la placa («Ver dictamen de ABC123») para abrir la ventana del dictamen: veredicto, trámite analizado, versión del catálogo, fecha de emisión y los alertamientos, cada uno con su fuente y su efecto («Bloquea» o «Advertencia»).",
          "El dictamen y la deuda se abren en una ventana centrada sobre el tablero, de 640 px de ancho en pantallas grandes, con el mismo contenido que antes. Se cierra con «Cerrar», con la tecla Escape o con un clic fuera, y al cerrarla el foco vuelve al botón que la abrió. Desde un dictamen Bloqueado, «Ver deuda» cambia la ventana por la de «Deuda y saneamiento» sin abrir una encima de otra.",
        ],
        bullets: [
          "Bloquean: SOAT no vigente (en maquinaria solo advierte, mientras se confirma si se le exige); revisión técnico-mecánica no vigente cuando el vehículo ya cumplió el plazo de su primera revisión; multas pendientes en el SIMIT del vehículo, del propietario o del locatario; y un SIMIT que no se pudo verificar.",
          "En el traspaso unilateral las multas del SIMIT y la revisión técnico-mecánica solo advierten.",
          "Advierten: estado del vehículo distinto de activo o registrado, limitaciones, gravámenes, garantías mobiliarias, prendas, infracciones en el RUNT, licencia de conducción suspendida o cancelada, acuerdos de pago con saldo, impuesto departamental pendiente y una fuente obligatoria que no llegó.",
        ],
        callouts: [
          {
            variant: "info",
            title: "Ajustado por FLIT",
            text: "El equipo FLIT puede corregir un dictamen. Verás el dictamen corregido con la marca «Ajustado por FLIT» y su fecha; ese ajuste no se notifica. Si después el dictamen se recalcula solo (por un reproceso o un llenado), gana el recálculo y la marca desaparece.",
          },
        ],
      },
    ],
  },
  {
    slug: "6-diagnostico/5-impuesto-departamental",
    title: "Impuesto departamental en el diagnóstico",
    audience: "Gestor",
    sectionId: "diagnostico",
    keywords: [
      "impuesto departamental",
      "impuesto vehicular",
      "vigencias",
      "pagada",
      "monto total",
      "departamento no soportado",
      "deuda sin valor informado",
      "servicio publico",
      "impuesto no confirmado",
    ],
    summary:
      "Qué consulta la fuente de impuesto, en qué departamentos aplica y cómo leer el monto total, las vigencias y la columna «Pagada».",
    blocks: [
      {
        id: "que-consulta",
        title: "1. Qué consulta",
        paragraphs: [
          "La fuente «Impuesto departamental» consulta la deuda del impuesto vehicular en el departamento del organismo de tránsito que reporta el RUNT, con el primer propietario que figura en el RUNT. Por eso corre después del RUNT del vehículo.",
          "Solo se ofrece en el alcance si tu contrato la habilita y la plataforma la tiene disponible; mientras no lo esté, verás la fuente con el aviso «Disponible cuando la consulta automatizada esté en producción».",
        ],
        bullets: [
          "Departamentos: solo los que tienen consulta automatizada. Esa lista la administra FLIT en la plataforma de consultas, es la misma para todas las compañías y no se elige en el diagnóstico.",
          "Vehículo de un departamento sin consulta: la celda queda en Novedad «Departamento no soportado», sin consumo. No es un error.",
          "Vehículo de servicio público: Novedad «Servicio público».",
        ],
        media: [
          {
            kind: "diagram",
            id: "flujo-diagnostico-impuesto",
            alt: "Flujo de la consulta del impuesto departamental. Tras el RUNT del vehículo, si es de servicio público queda en Novedad Servicio público; si su departamento no tiene consulta automatizada queda en Novedad Departamento no soportado. Si no, se consulta el impuesto con el primer propietario del RUNT. Sin deuda confirmada queda Ejecutado; con deuda confirmada queda Con deuda con el monto total y las vigencias, o Deuda sin valor informado; si no se confirma queda en Error con su motivo. En Con análisis la deuda se anota como advertencia en el dictamen.",
            caption: "Cómo se decide el resultado del impuesto.",
          },
        ],
      },
      {
        id: "leer",
        title: "2. Cómo leer el resultado",
        paragraphs: [
          "En la grilla la celda queda «Ejecutado» si se confirmó que no hay deuda y «Con deuda» si la hay. En el detalle («Impuesto departamental · placa») la «Respuesta de la fuente» muestra el «Monto total» y la tabla «Vigencias», una fila por año con su valor y la columna «Pagada» (Sí o No). Si la fuente no trae vigencias, verás «Sin registros».",
          "Si la fuente confirma la deuda pero no informa el valor, verás «Deuda sin valor informado». Diagnóstico nunca convierte un valor vacío en cero ni muestra «$ 0» por falta de dato.",
          "Una respuesta que no confirma el resultado queda en Error con un motivo en español (por ejemplo «El impuesto no quedó confirmado.» o «El documento del propietario no coincide con el del impuesto.»); nunca se muestra como «sin deuda».",
        ],
        callouts: [
          {
            variant: "info",
            text: "El impuesto no se llena a mano: el backoffice FLIT puede llenar otras fuentes en error, pero no esta ni el RUNT del vehículo. Si queda en error, el administrador de Diagnóstico la reprocesa.",
          },
          {
            variant: "tip",
            text: "En Con análisis una deuda de impuesto es una advertencia del dictamen, no un bloqueo. En la ventana «Deuda y saneamiento» aparece como «Vigencias del impuesto» con el «Total pendiente».",
          },
        ],
      },
    ],
  },
  {
    slug: "6-diagnostico/6-evidencias-y-avisos",
    title: "Evidencias del diagnóstico (PDF, Excel y ZIP) y avisos de la campana",
    audience: "Gestor",
    sectionId: "diagnostico",
    keywords: [
      "pdf del vehiculo",
      "descargar pdf",
      "excel del lote",
      "zip de evidencias",
      "descargas del lote",
      "evidencias del diagnostico",
      "campana",
      "avisos",
      "notificaciones de diagnostico",
      "diagnostico terminado",
    ],
    summary:
      "Qué archivos descargas de un diagnóstico, cuándo están disponibles y qué te avisa la campana.",
    blocks: [
      {
        id: "pdf",
        title: "1. PDF por vehículo",
        paragraphs: [
          "En el menú «Acciones» de la fila, «Descargar PDF» baja el informe del vehículo en formato corporativo, con el encabezado del diagnóstico y solo las secciones de las fuentes que se pidieron. Está disponible cuando terminan las consultas de ese vehículo, sin esperar al resto del lote. Mientras no lo esté, la opción aparece atenuada con el motivo.",
          "Si el lote se cerró como Incompleto, el PDF también sale para los vehículos con consultas en error y esas fuentes aparecen como no disponibles. El PDF se vuelve a generar solo cuando cambia la evidencia, por ejemplo tras un reproceso o un nuevo dictamen.",
        ],
      },
      {
        id: "lote",
        title: "2. Excel y ZIP del lote",
        paragraphs: [
          "La región «Descargas del lote» ofrece el «Excel del lote» (una fila por vehículo, con columnas según las fuentes elegidas) y el ZIP con los PDF de los vehículos terminados. El ZIP se prepara en segundo plano: verás «Preparando N PDF…» y, cuando quede listo, el aviso en la campana con «Descargar».",
          "Descargar exige el permiso de reportes, que tienen el usuario y el administrador de Diagnóstico.",
        ],
      },
      {
        id: "campana",
        title: "3. La campana «Avisos»",
        paragraphs: [
          "La campana del encabezado de Diagnóstico muestra tus avisos con el número de no leídos. Se consulta sola cada medio minuto y puedes marcar los avisos como leídos. No envía correo.",
        ],
        bullets: [
          "ZIP de evidencias listo, o no se pudo generar el ZIP de evidencias.",
          "Diagnóstico terminado (también si terminó con consultas incompletas).",
          "Creación de trámites terminada, con cuántos se crearon, se omitieron o fallaron.",
          "Saneamiento en gestión, terminado, no procede, reabierto o anulado, para quien lo solicitó.",
        ],
        callouts: [
          {
            variant: "info",
            text: "Los avisos no llevan placas, documentos ni nombres de personas: solo el nombre del lote y cantidades. Se borran a los 90 días. El ajuste del dictamen por FLIT no genera aviso.",
          },
        ],
      },
    ],
  },
  {
    slug: "6-diagnostico/7-crear-tramite",
    title: "Crear trámite desde un diagnóstico",
    audience: "Gestor",
    sectionId: "diagnostico",
    keywords: [
      "crear tramite desde diagnostico",
      "crear tramites seleccionados",
      "derivar a tramite",
      "borrador del tramite",
      "ir al wizard",
      "listos para tramite",
      "reintentar fallidas",
      "placas omitidas",
    ],
    summary:
      "Cómo pasar un vehículo diagnosticado a un borrador en Trámites, uno por uno o en bloque, y qué pasa mientras se crean.",
    blocks: [
      {
        id: "cuando",
        title: "1. Cuándo se puede",
        paragraphs: [
          "Un vehículo se puede llevar a trámite cuando terminó sus consultas y todavía no tiene trámite. En Con análisis además su dictamen vigente debe ser Viable: con Bloqueado no se ofrece «Crear trámite». En Simple no hay dictamen y tú eliges el tipo de trámite. Un vehículo origina como máximo un trámite.",
        ],
      },
      {
        id: "individual",
        title: "2. Un vehículo",
        paragraphs: [
          "En la fila abre «Acciones» y elige «Crear trámite». En Simple elige el «Tipo de trámite»; en Con análisis se usa el trámite analizado del lote. Confirma y, al terminar, verás «Se creó el borrador del trámite» con «Ir al wizard», que abre Trámites en una pestaña nueva para que completes allí el borrador. Si la placa ya tenía trámite, verás «La placa ya tenía el trámite» y «Ver trámite». Cuando el vehículo ya tiene trámite, el menú «Acciones» muestra «Ver trámite #N», que lo abre en una pestaña nueva. Si el vehículo aún no cumple las condiciones, «Crear trámite» aparece atenuada con el motivo.",
        ],
      },
      {
        id: "masivo",
        title: "3. Varios vehículos",
        paragraphs: [
          "Marca las casillas «Seleccionar la placa…» (solo se seleccionan vehículos Viables y sin trámite) y pulsa «Crear trámites (N)». En Simple eliges una vez el tipo para todas las placas.",
          "Los borradores se crean en segundo plano, en orden y por turnos: cada compañía tiene unos pocos en curso a la vez, de modo que un lote grande no frena a los demás clientes. Verás «Creando trámites…» con el avance y puedes salir de la pantalla: al volver lo retomas. Al final, el resumen muestra las placas creadas, omitidas y fallidas con su motivo, y «Reintentar fallidas». La campana avisa cuando termina.",
          "Una placa se omite si, al momento de crearla, dejó de cumplir las condiciones (por ejemplo, ya tiene trámite). Puede fallar si Trámites no está disponible o rechaza el borrador, por ejemplo porque el organismo de tránsito del vehículo no está habilitado para tu compañía.",
        ],
        callouts: [
          {
            variant: "info",
            text: "Si el vehículo tiene un saneamiento en curso, verás «Este vehículo tiene un saneamiento en curso. Puedes crear el trámite igual.»: es solo un aviso, no bloquea.",
          },
        ],
      },
    ],
  },
  {
    slug: "6-diagnostico/8-configuracion",
    title: "Configuración de Diagnóstico: contrato y operación",
    audience: "Admin de Compañía",
    sectionId: "diagnostico",
    keywords: [
      "configuracion de diagnostico",
      "contrato de diagnostico",
      "fuentes habilitadas",
      "limite de vehiculos por lote",
      "operacion de diagnostico",
      "quien ve los diagnosticos",
      "solo los propios",
      "fuentes por defecto",
      "estimado de consultas",
      "guardar operacion",
    ],
    summary:
      "Las dos pestañas de «Configuración» de Diagnóstico: el contrato, que fija FLIT, y la operación, que ajusta el administrador de la compañía.",
    blocks: [
      {
        id: "contrato",
        title: "1. Pestaña «Contrato» (solo lectura)",
        paragraphs: [
          "En «Ajustes → Configuración» la pestaña «Contrato» muestra las «Fuentes habilitadas» para tu compañía y el «Límite de vehículos por lote». FLIT administra tu contrato: para cambiarlo, escribe a tu ejecutivo de cuenta.",
        ],
      },
      {
        id: "operacion",
        title: "2. Pestaña «Operación»",
        paragraphs: [
          "Cómo trabaja tu compañía con Diagnóstico dentro de lo contratado. La edita el administrador de Diagnóstico con «Guardar operación»; los demás usuarios la ven en solo lectura. Los cambios aplican a los diagnósticos nuevos.",
        ],
        bullets: [
          "Quién ve los diagnósticos: «Todos los diagnósticos de la compañía» (por defecto) o «Solo los propios». Con «Solo los propios» el administrador sigue viéndolos todos.",
          "Fuentes por defecto: se proponen al crear un diagnóstico. Solo puedes elegir entre las fuentes habilitadas en el contrato.",
          "Estimado de consultas: «No mostrar» (por defecto), «Antes de lanzar el diagnóstico» o «Al finalizar el diagnóstico».",
        ],
        callouts: [
          {
            variant: "warning",
            title: "El contrato cambió",
            text: "Si FLIT quita una fuente del contrato, también se recorta de tus fuentes por defecto. La pestaña lo avisa con «El contrato cambió» para que revises la operación.",
          },
          {
            variant: "info",
            text: "Usuarios, roles, suscripción y la ficha de la compañía no se editan aquí: se administran en el hub.",
          },
        ],
      },
    ],
  },
  {
    slug: "6-diagnostico/9-backoffice-flit",
    title: "Backoffice FLIT de Diagnóstico: lotes por compañía, llenado a mano y ajuste del dictamen",
    audience: "Super Admin",
    sectionId: "diagnostico",
    keywords: [
      "backoffice de diagnostico",
      "lotes por compania",
      "solo lotes con errores",
      "llenar a mano",
      "llenado manual",
      "confirmar llenado",
      "ajuste flit",
      "ajuste del dictamen",
      "corregir el dictamen",
      "versiones del dictamen",
      "auditoria del dictamen",
      "editar contrato",
      "suspender la redaccion con ia",
      "fuentes obligatorias por definicion",
    ],
    summary:
      "Lo que hace el equipo FLIT en el backoffice de Diagnóstico: revisar lotes de una compañía, llenar consultas en error, ajustar el dictamen, editar el contrato y las fuentes obligatorias.",
    blocks: [
      {
        id: "acceso",
        title: "1. Acceso y lotes por compañía",
        paragraphs: [
          "La sección «Backoffice» del dock solo aparece para el equipo FLIT (Super Admin). Ahí está «Lotes por compañía»: elige la «Empresa» (busca por nombre o NIT) y revisa sus lotes con el filtro de estado y «Solo lotes con errores». Al abrir un lote ves su grilla con las mismas celdas que la compañía, más las marcas que solo ve FLIT: «Manual» en una celda llenada a mano y la «Auditoría» del dictamen, que se abre en una ventana como la del dictamen. Cada celda abre el detalle de la consulta en la vista del backoffice, con su respuesta y sus intentos, incluido el origen (automático o manual).",
          "Cada lectura de otra compañía queda auditada. El backoffice no crea diagnósticos ni solicita saneamientos en nombre de la compañía.",
        ],
      },
      {
        id: "llenado",
        title: "2. Llenar a mano una consulta en error",
        paragraphs: [
          "En una celda en error pulsa «Llenar a mano» para abrir el formulario de esa fuente (por ejemplo comparendos del SIMIT o el valor del avalúo) y confirma con «Confirmar llenado». La celda queda verde con la marca «Manual», el lote vuelve a En proceso y, en Con análisis, el dictamen se recalcula.",
          "No se llenan a mano el RUNT del vehículo ni el impuesto departamental. Una celda que depende del RUNT del vehículo en error se resuelve reprocesando el RUNT.",
        ],
        callouts: [
          {
            variant: "info",
            text: "El origen manual y el usuario que llenó quedan registrados, pero la compañía no los ve: ni en la grilla, ni en el detalle, ni en el PDF.",
          },
        ],
      },
      {
        id: "ajuste",
        title: "3. Ajuste FLIT del dictamen",
        paragraphs: [
          "Desde la ventana del dictamen en el backoffice puedes cambiar el veredicto y agregar, editar o eliminar alertamientos (incluido un «Alertamiento libre»). El motivo es obligatorio en todo ajuste (de 10 a 500 caracteres). Un dictamen Viable no puede tener alertamientos que bloqueen y uno Bloqueado necesita al menos uno.",
          "Cada ajuste crea una versión nueva del dictamen. La compañía ve el dictamen corregido con «Ajustado por FLIT» y la fecha; el original, el motivo y quién hizo cada cambio quedan en «Versiones del dictamen», visible solo en el backoffice. Mientras editas el ajuste, la ventana no se cierra con un clic fuera, para no perder lo escrito: ciérrala con «Cerrar» o Escape. Si el vehículo ya tiene trámite, el sistema pide confirmarlo y el trámite no se revierte. Si otra persona ajustó antes, se avisa y no se pisa su cambio. Un recálculo automático posterior gana y el ajuste queda como «Superado por recálculo».",
        ],
      },
      {
        id: "contrato-fuentes",
        title: "4. Contrato y fuentes obligatorias",
        paragraphs: [
          "En «Configuración → Contrato» el backoffice elige la empresa y edita sus «Fuentes habilitadas» y su «Límite de vehículos por lote» con «Guardar contrato». Al quitar una fuente habilitada se recortan las fuentes por defecto de la empresa. El interruptor «Suspender la redacción con IA» es de emergencia: mientras esté activo, los dictámenes salen sin redacción automática.",
          "«Fuentes obligatorias» define, por tipo de trámite o por familia, qué fuentes se exigen en Con análisis. El RUNT del vehículo siempre es obligatorio y no se configura aquí. Hay pisos que no se pueden quitar: el SIMIT del propietario en el traspaso y en la transferencia de dominio. En el traspaso unilateral no se exige el SIMIT y no se puede marcar. Una lista vacía guardada es válida y evita heredar las fuentes de la familia.",
        ],
      },
    ],
  },
  {
    slug: "6-diagnostico/10-saneamiento",
    title: "Saneamiento: pedirle a FLIT que gestione un vehículo",
    audience: "Gestor",
    sectionId: "diagnostico",
    keywords: [
      "saneamiento",
      "sanear",
      "solicitar saneamiento",
      "bandera sanear",
      "deuda y saneamiento",
      "quitar la solicitud",
      "saneamiento solicitado",
      "en saneamiento",
      "saneado",
      "saneamiento no procede",
      "evidencia de pago",
      "excel de deuda",
    ],
    summary:
      "Cómo solicitar el saneamiento de un vehículo con un clic, qué hace FLIT, cómo seguirlo y qué no cambia (el dictamen y el cobro).",
    blocks: [
      {
        id: "que-es",
        title: "1. Qué es el saneamiento",
        paragraphs: [
          "Sanear es quitar lo que impide el trámite de un vehículo: comparendos, falta de paz y salvo, impuesto, SOAT o revisión técnico-mecánica. En Diagnóstico lo pides con una bandera y el equipo FLIT lo gestiona por fuera del sistema; luego deja constancia del resultado por fuente, con un PDF de evidencia cuando aplica.",
          "Diagnóstico no cobra, no liquida y no registra pagos: el saneamiento es un circuito de gestión, no un módulo de pagos.",
        ],
        media: [
          {
            kind: "diagram",
            id: "flujo-saneamiento",
            alt: "Circuito del saneamiento. Con el vehículo terminado, la compañía elige Solicitar saneamiento en el menú Acciones y queda Saneamiento solicitado. Si la compañía quita la solicitud, o FLIT anula con motivo, queda Anulado y se puede volver a pedir. Si FLIT lo toma, queda En saneamiento con un concepto por fuente con hallazgo; FLIT marca cada concepto y puede adjuntar un PDF de evidencia. FLIT cierra como saneado o como no procede con motivo, y puede reabrir con motivo. El dictamen no cambia: para desbloquear, FLIT ajusta el dictamen o se reprocesa.",
            caption: "Estados del saneamiento y quién mueve cada paso.",
          },
        ],
      },
      {
        id: "solicitar",
        title: "2. Cómo se solicita",
        paragraphs: [
          "Cuando un vehículo terminó todas sus consultas (en verde o en Novedad), el menú «Acciones» de su fila ofrece «Solicitar saneamiento». Al elegirlo, encima del botón aparece el rótulo «Solicitado» con la bandera encendida; mientras FLIT no la haya tomado, la opción pasa a «Quitar solicitud de saneamiento». Cuando FLIT ya lo tomó o lo cerró, la opción es «Ver saneamiento». Con alguna consulta en error, «Solicitar saneamiento» aparece atenuada con el motivo hasta que se reprocese.",
          "La solicitud está en los dos modos y con cualquier veredicto: un vehículo Viable puede tener a la vez «Crear trámite» y «Solicitar saneamiento». FLIT decide si procede.",
          "Para varias placas, selecciónalas en el tablero o en la pestaña «Vehículos» y pulsa «Sanear (N)», hasta 500 por solicitud. Las placas que no cumplen (faltan consultas por terminar o ya tienen un saneamiento) se omiten y se informa cuántas.",
        ],
        bullets: [
          "Puede solicitarlo cualquier usuario de la compañía con rol de Diagnóstico.",
          "Un vehículo tiene como máximo un saneamiento vigente. Después de un cierre (saneado o no procede) no se vuelve a pedir: solo FLIT lo reabre. Si se anuló, puedes marcarlo de nuevo.",
        ],
      },
      {
        id: "panel",
        title: "3. La ventana «Deuda y saneamiento»",
        paragraphs: [
          "Se abre desde «Ver deuda» o «Ver saneamiento» del menú «Acciones», desde «Ver deuda de la placa…» en la pestaña «Vehículos» o desde la ventana de un dictamen Bloqueado. Es una ventana centrada de 640 px de ancho; como puede tener campos que estás llenando, no se cierra con un clic fuera: ciérrala con «Cerrar» o Escape. Muestra primero los motivos del dictamen vigente, si lo hay, y debajo la deuda conocida por fuente: comparendos del SIMIT con su valor tal como lo reporta la fuente (Diagnóstico no los suma), acuerdos de pago, paz y salvo, vigencias del impuesto con su total pendiente y vigencia del SOAT y de la revisión técnico-mecánica. Si no hay nada pendiente, lo dice; nunca sale vacío.",
          "Debajo están el estado del saneamiento, los conceptos que registró FLIT con su resultado (Pendiente, Saneado o No procede) y su nota, la «Observación de FLIT» y el historial. Si FLIT cargó una evidencia, la descargas con «Descargar evidencia». La ventana descarga el PDF y el Excel de la deuda; el Excel trae una hoja por fuente.",
        ],
        callouts: [
          {
            variant: "warning",
            title: "El dictamen no cambia solo",
            text: "El saneamiento no cambia el dictamen. Un vehículo puede verse «Saneado» y «Bloqueado» a la vez: para desbloquearlo, FLIT ajusta el dictamen o el administrador de Diagnóstico reprocesa las consultas para que el dictamen se recalcule con los datos nuevos.",
          },
          {
            variant: "info",
            text: "En el historial el actor de la gestión aparece como «FLIT»; nunca se muestra el usuario del operador. La campana te avisa cuando FLIT toma, cierra, reabre o anula tu solicitud.",
          },
        ],
      },
    ],
  },
  {
    slug: "6-diagnostico/11-saneamiento-gestion-flit",
    title: "Gestión FLIT del saneamiento: bandeja «Saneamientos» y cierre",
    audience: "Super Admin",
    sectionId: "diagnostico",
    keywords: [
      "bandeja de saneamientos",
      "solicitudes sin tomar",
      "tomar saneamientos",
      "gestionar saneamiento",
      "conceptos a sanear",
      "agregar otro concepto",
      "quitar concepto",
      "observacion para la compania",
      "cerrar como saneado",
      "cerrar como no procede",
      "reabrir el saneamiento",
      "anular el saneamiento",
    ],
    summary:
      "Cómo el equipo FLIT toma las solicitudes de saneamiento de todas las compañías, registra cada concepto con su evidencia y cierra, reabre o anula.",
    blocks: [
      {
        id: "bandeja",
        title: "1. La bandeja «Saneamientos»",
        paragraphs: [
          "En el dock del backoffice, «Saneamientos» lleva una insignia con las solicitudes sin tomar. La bandeja reúne las solicitudes de todas las compañías y abre filtrada por «Solicitado», las más antiguas primero. Filtros: estado, compañía, placa y fechas de la solicitud. «Descargar Excel» exporta lo filtrado.",
          "Para tomar en bloque marca las filas (o «Seleccionar las solicitudes sin tomar») y pulsa «Tomar (N)». Solo se toman las solicitudes sin tomar; el resultado dice cuántas se tomaron. El cierre es siempre por vehículo.",
        ],
        callouts: [
          {
            variant: "info",
            text: "Es la única vista del backoffice que muestra varias compañías a la vez; cada lectura queda auditada por cada compañía que aparece en la página.",
          },
        ],
      },
      {
        id: "gestion",
        title: "2. Gestionar un saneamiento",
        paragraphs: [
          "Al abrir una fila ves la cabecera (placa, lote, dictamen), la columna «Deuda del vehículo» y la gestión. Pulsa «Tomar»: el saneamiento pasa a En saneamiento y, la primera vez, el sistema crea un concepto por cada fuente con hallazgo: SIMIT del vehículo, del propietario y del locatario (con deuda, sin paz y salvo o sin verificar), impuesto con deuda y SOAT o revisión técnico-mecánica no vigentes. Funciona también en lotes Simple.",
          "En «Conceptos a sanear» marca el resultado de cada concepto (Pendiente, Saneado o No procede) y su nota; se guarda solo («Guardando…», «Guardado»). Con «Cargar evidencia» adjuntas un PDF de evidencia de pago de hasta 10 MB por concepto, que puedes ver, reemplazar o quitar mientras está en gestión. «Agregar otro concepto» suma lo que las fuentes no reflejan, con una descripción. «Quitar concepto» lo saca de la gestión sin borrarlo: queda en el historial.",
          "La «Observación para la compañía» es opcional y la compañía la ve. Si otra persona cambió el saneamiento mientras lo editabas, verás «Otra persona cambió este saneamiento» y «Recargar».",
        ],
        bullets: [
          "Cerrar como saneado: exige al menos un concepto, ninguno pendiente y al menos uno saneado. La compañía lo verá como saneado; el dictamen no cambia.",
          "Cerrar como no procede: exige un motivo de 10 a 500 caracteres, que la compañía verá.",
          "Reabrir: un saneado o un no procede vuelve a En saneamiento, con motivo obligatorio.",
          "Anular: desde Solicitado o En saneamiento, con motivo obligatorio. La compañía podrá volver a pedirlo.",
        ],
      },
      {
        id: "trazabilidad",
        title: "3. Trazabilidad",
        paragraphs: [
          "Cada acción queda en el «Historial» del saneamiento, que no se edita ni se borra: quién solicitó, quién tomó, cada concepto, cada evidencia, los cierres, reaperturas y anulaciones con su motivo. En la vista de FLIT el actor aparece como «Tú», «Otro operador de FLIT» o «Usuario de la compañía»; la compañía solo ve «FLIT».",
          "El saneamiento no cobra ni registra pagos estructurados, y no toca el dictamen: si corresponde desbloquear, haz el ajuste FLIT del dictamen desde el backoffice.",
        ],
      },
    ],
  },
  {
    slug: "6-diagnostico/12-estados-y-preguntas",
    title: "Diagnóstico: estados y preguntas frecuentes",
    audience: "Gestor",
    sectionId: "diagnostico",
    keywords: [
      "estados de diagnostico",
      "glosario de diagnostico",
      "que significa novedad",
      "sin ejecutar",
      "ejecutado",
      "sin solicitar",
      "incompleto",
      "completado",
      "estados del saneamiento",
      "preguntas frecuentes diagnostico",
    ],
    summary:
      "Glosario de los estados de cada consulta, del lote, del dictamen y del saneamiento, y respuestas a las dudas más comunes.",
    blocks: [
      {
        id: "consulta",
        title: "1. Estados de una consulta (semáforo)",
        paragraphs: [],
        bullets: [
          "Sin ejecutar: la consulta está en cola. Sin ejecutar (en curso): se está consultando.",
          "Ejecutado: la fuente respondió y el dato quedó guardado, con su fecha.",
          "Con deuda: la fuente respondió y reporta deuda (por ejemplo, impuesto pendiente).",
          "Error: no se pudo consultar; la nota dice el motivo (por ejemplo «Espera al RUNT del vehículo» o «Límite del proveedor»). El administrador de Diagnóstico puede reprocesarla.",
          "Novedad: la consulta no aplica a este vehículo, con su motivo: «Servicio público», «Departamento no soportado» o «Sin documento del locatario». No consume.",
          "Sin solicitar: la fuente no se pidió en este lote.",
        ],
      },
      {
        id: "lote-dictamen",
        title: "2. Estados del lote, del vehículo y del dictamen",
        paragraphs: [],
        bullets: [
          "Lote: Creado o sin lanzar (cabecera guardada, sin vehículos confirmados), En proceso, Completado (todas las consultas terminaron sin error) e Incompleto (se cerró con consultas en error).",
          "Vehículo: En proceso mientras le quedan consultas y Terminado cuando todas están en Ejecutado o Novedad.",
          "Dictamen: Viable, Bloqueado o Sin dictamen (lotes Simple o vehículos aún en proceso). Puede llevar «Ajustado por FLIT» y, mientras la IA redacta, «redacción IA pendiente».",
        ],
      },
      {
        id: "saneamiento",
        title: "3. Estados del saneamiento",
        paragraphs: [],
        bullets: [
          "Saneamiento solicitado: la compañía marcó la bandera; aún puede quitarla.",
          "En saneamiento: FLIT lo tomó y lo está gestionando.",
          "Saneado: FLIT lo cerró con éxito. No cambia el dictamen.",
          "Saneamiento no procede: FLIT lo cerró sin sanear, con un motivo.",
          "Anulado: la compañía quitó la solicitud o FLIT la anuló; para la compañía se ve como sin saneamiento. Se puede volver a pedir.",
          "Cada concepto de un saneamiento queda Pendiente, Saneado o No procede, y su hallazgo es Con deuda, No vigente o Sin verificar.",
        ],
      },
      {
        id: "faq",
        title: "4. Preguntas frecuentes",
        paragraphs: [
          "¿Por qué no se consultaron las demás fuentes de un vehículo? Porque su RUNT falló: sin RUNT no hay placa definitiva ni propietario, y las demás fuentes esperan. Al reprocesar el RUNT, se consultan.",
          "¿Por qué una consulta no aparece en el consumo? Porque se reutilizó el dato del mismo día de esa placa y fuente en tu compañía, o porque quedó en Novedad.",
          "¿Puedo cambiar las fuentes de un lote ya lanzado? No. Crea un diagnóstico nuevo con el alcance que necesitas.",
          "¿Por qué no puedo crear el trámite? El vehículo aún no termina, ya tiene trámite, ya está en curso de creación o, en Con análisis, su dictamen no es Viable.",
          "¿El saneamiento cuesta algo o cambia el dictamen? Diagnóstico no cobra el saneamiento y no cambia el dictamen al cerrarlo; si corresponde, FLIT ajusta el dictamen.",
          "¿Quién llena datos a mano o corrige el dictamen? Solo el equipo FLIT. Ningún usuario de la compañía puede hacerlo, ni por pantalla ni por API.",
          "¿Dónde quedaron los botones de cada vehículo? En el menú «Acciones» de la última columna del tablero. La bandera de saneamiento, «Crear trámite», «Ver deuda», el PDF y el reintento están ahí.",
          "¿Por qué el Dashboard y un reporte dan cifras distintas para el mismo periodo? Porque cada indicador cuenta por su propia fecha: los diagnósticos y vehículos por la fecha de creación del diagnóstico, los saneamientos por la fecha de solicitud y los trámites por la fecha de creación. Revisa la fecha que usa cada uno.",
          "¿Por qué no puedo exportar un reporte? Tu rol no incluye la descarga de reportes, la tabla está vacía o el filtro supera el máximo del formato. El motivo aparece junto al botón.",
        ],
      },
    ],
  },
];
