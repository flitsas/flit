import type { ManualArticle } from "../types";

/**
 * HU-H (Fase 3) — sección «Super Admin» (equipo FLIT). Documenta las consolas globales agrupadas
 * por tema; cada artículo cubre varias entradas del dock «Administradores». Fuentes: títulos y
 * subtítulos reales de `app/admin/**`, `components/admin/**`, `RbacAdmin.tsx`, `Auditoria.tsx`,
 * módulos ICT/Log QX y ADR-0059 (procesos periódicos).
 */
export const SUPERADMIN_ARTICLES: ManualArticle[] = [
  {
    slug: "4-superadmin/1-companias-y-organismos",
    title: "Compañías, organismos de tránsito y causales",
    audience: "Super Admin",
    sectionId: "superadmin",
    keywords: [
      "companias",
      "crear compania",
      "codigo de compania",
      "consecutivo comp",
      "administracion de companias",
      "organismos de transito",
      "activar organismo",
      "codigo integrador",
      "divipol",
      "causales de rechazo",
      "concesion",
      "marca blanca",
      "tipo de compania",
      "guardar todo",
      "ficha de compania",
      "incluir ot",
      "cabeza de red",
      "panel de hijas",
      "dominio marca blanca",
      "hub del organismo",
      "revocatorias organismo",
      "filtrar compañias",
      "filtros por columna",
      "buscar compania",
      "integraciones externas",
      "integracion persona natural",
      "rotar secreto",
      "bloquear integracion",
      "identificador y secreto",
    ],
    summary:
      "Alta y ciclo de vida de compañías y organismos, la ficha completa de cada compañía, entrada al hub OT y catálogo de causales de rechazo.",
    blocks: [
      {
        id: "companias",
        title: "1. Compañías",
        paragraphs: [
          "Administradores → Compañías. Catálogo de compañías B2B con el toggle «incluir OT» para ver también los organismos de tránsito en el mismo listado. Arriba de la tabla está el panel con NIT, Razón social, Estado y Fecha desde/hasta, con «Buscar» y «Limpiar». Además, cada columna con datos tiene un embudo en su encabezado para filtrarla (ver «Tablas: cómo leerlas y moverte entre páginas»); los filtros del panel y los de las columnas se combinan. La primera columna, «Código», muestra el consecutivo de cada compañía (COMP-0001, COMP-0002…); las compañías que ya existían recibieron el suyo según su antigüedad. «Crear compañía»: razón social, NIT, tipo de compañía (normal, Concesión o Marca Blanca) y, para Marca Blanca, dominio propio. El código no se escribe: el sistema asigna el siguiente consecutivo (COMP-0001, COMP-0002…) al guardar. «Activar / Desactivar» cambia el estado con confirmación; una compañía de tipo de sistema no se edita desde aquí.",
          "Abrir una fila lleva a la ficha completa de la compañía, con 9 pestañas (más «Clientes hijos» y «Marca» si es cabeza de red):",
        ],
        bullets: [
          "Productos: enciende o apaga los productos de la suite para la compañía (Trámites, Comparendos, Diagnóstico). El cambio se aplica al instante, sin «Guardar todo», y queda en el historial. Cada producto muestra su estado escrito: Encendido, Apagado o Próximamente.",
          "Trámites: familias habilitadas (Matrículas, Traspaso, Otros), cada una en un acordeón con un resumen («Sin restricciones» o «N restricciones activas») y con el estado de cada opción escrito junto a su switch (por ejemplo Permitido o Bloqueado), más la lista blanca de trámites permitidos. Debajo, la sección «Improntas» tiene el interruptor «Generación de improntas» (visible solo para el Super Admin), con el estado escrito junto a él: Habilitada o Deshabilitada. Si lo desactivas, el asistente no genera la impronta automáticamente y el radicador la carga a mano; solo aplica a trámites nuevos. Se guarda con «Guardar todo».",
          "Configuración Empresa: cinco bloques con un índice de acceso rápido arriba. «Firma y documentos» (firma precargada desde el baúl y módulo de Resoluciones), «Notificaciones» (enrutamiento, avisos al aprobar o rechazar y sus destinatarios), «Cobro y recaudo» (métodos de recaudo), «Consultas y fuentes» (validar SOAT ante RUNT, fuente de comparendos, proveedores de consulta y de avalúo; el tiempo de espera de contingencia está en «Opciones avanzadas») y «Organismos de tránsito», con los bloqueos y restricciones que aplica cada uno. Las opciones elegidas se marcan en verde y la de Fasecolda aparece como obligatoria. Esa tabla y la de Bloqueos tienen un buscador arriba por nombre o código y, además, un embudo en el encabezado de cada columna (Organismo, Código, Estado, Convenio; en Bloqueos, Estado en red); recorren todos los organismos, no solo la página que estás viendo.",
          "Documentos: la tabla lista todos los documentos del catálogo con su estado: Obligatorio, Opcional, Oculto o Base (el documento que aún no tiene parámetro para esa compañía). Ya no hay botón «Agregar parámetro»: se edita desde la fila, con el ícono de editar, para elegir el estado. Filtra con el embudo de las columnas Documento (nombre o código) y Estado.",
          "Representantes legales: un resumen de cuántos representantes no tienen firma ni identidad y paginación con «Filas por página». Encima hay un buscador por nombre o documento y, en el encabezado de cada columna (Representante, Documento, Trámites, Firma / Identidad), un embudo para filtrarla. La firma de cada representante se gestiona en su ficha.",
          "Mandatarios: el mismo formulario que ve el Administrador de la compañía, con modelo (Persona natural, Persona jurídica o Formato en blanco), forma de firma y vigencia. La lista muestra el modelo y la etiqueta de vigencia (Vigente, Por vencer, Vencido, Inactivo, Aún no vigente; guion en Persona jurídica y Formato en blanco). Tiene buscador por nombre o documento y un embudo en el encabezado de columnas como Mandatario, Documento, Modelo, Vigencia, Validación y Organismos; las acciones de cada fila son íconos (editar, dar de baja o reactivar, eliminar) con su nombre al pasar el mouse. Ver el artículo «Representantes legales y mandatarios» de Administrador de compañía.",
          "Usuarios: usuarios de la compañía con buscador y selectores de perfil, rol y estado arriba, un embudo en cada columna (Nombre, Correo, Perfil, Rol, Estado, Fecha) e «Invitar usuario».",
          "Integraciones externas: la conexión del portal de Persona Natural con la compañía (solo la ve el Super Admin). Se explica en el apartado «5. Integraciones externas de la compañía».",
          "Historial de Cambios: quién cambió qué y cuándo, con el campo en lenguaje claro y el valor anterior y el nuevo como etiquetas. «Ver detalle» abre los valores extensos. Arriba hay fechas, tipo de cambio y buscador, que actúan sobre la página cargada. Los embudos de las columnas (Fecha, Campo modificado, Valor anterior, Operador) filtran todo el historial de la compañía, no solo la página que ves; puedes buscar por lo que se ve, por ejemplo «Inicio de sesión» en Campo modificado o «Deshabilitada» en Valor anterior. Si en el rango propio la fecha inicial es posterior a la final, te avisa y no aplica.",
        ],
        callouts: [
          {
            variant: "info",
            text: "«Guardar todo» no aplica los cambios de una vez: primero abre un resumen con el detalle de todo lo que vas a modificar, y solo al confirmarlo los guarda.",
          },
        ],
        media: [
          {
            kind: "image",
            src: "/manual/screenshots/admin-companias.png",
            alt: "Administración de compañías con el toggle de incluir organismos, la barra de filtros por columna y el listado con NIT, razón social, tipo y estado",
            caption: "Administración de compañías: el punto de entrada a la ficha de cada tenant.",
          },
        ],
      },
      {
        id: "companias-red",
        title: "2. Cabezas de red",
        paragraphs: [
          "Si la compañía es cabeza de red (Concesión o Marca Blanca), su ficha suma el panel de hijas: agregar un cliente a la red, activar o inactivar una hija e invitar usuarios para que la administren. La tabla de hijas y el Panel de red no tienen filtros arriba: se filtran con el embudo de cada columna (Razón social, NIT, Tipo, Estado y la fecha de vinculación).",
          "Si además es Marca Blanca, configura la marca y el dominio propio de la red: registrar el dominio, cambiarlo, retirarlo y comprobarlo (validación de propiedad). Estas cuatro acciones son exclusivas del Super Admin; el Administrador de la cabeza de red solo las ve en modo lectura.",
        ],
      },
      {
        id: "organismos",
        title: "3. Tránsito → Organismos",
        paragraphs: [
          "Catálogo de organismos de tránsito con código DIVIPOL, código integrador y departamento; filtra por estado, departamento o integrador. «Activar organismo de tránsito» crea su inquilino (con su código consecutivo COMP-XXXX, asignado por el sistema) y lo deja operable; desde la fila entras a su hub, con 10 pestañas: Trámites (bandeja de decisión), Reglas, Documentos (prelación y etiquetas), Requisitos, Usuarios, Reportes, Mandatos, Validar impronta, Revocatorias (decidir las solicitudes que llegan del gestor) y Configuración (modo Quipux, ventana de revocatoria, feature flags).",
        ],
        callouts: [
          {
            variant: "info",
            text: "Un organismo sin código integrador cargado no puede radicar en la secretaría por Quipux; el aviso aparece en su fila.",
          },
          {
            variant: "info",
            text: "La pestaña de webhooks del organismo es legado: solo se abre por URL directa (no tiene entrada en el hub).",
          },
        ],
      },
      {
        id: "causales",
        title: "4. Tránsito → Causales de rechazo",
        paragraphs: [
          "Catálogo de causales que el organismo marca al rechazar («¿Qué falló?»). Cada causal tiene código, descripción y familia (matrícula inicial o traspaso: no son intercambiables). Se crean, editan y activan o desactivan; una causal inactiva deja de ofrecerse sin borrar el historial.",
        ],
        media: [
          {
            kind: "image",
            src: "/manual/screenshots/admin-causales.png",
            alt: "Consola de causales de rechazo con las familias de matrícula inicial y traspaso",
            caption: "Causales de rechazo: lo que el organismo elige al rechazar un trámite.",
          },
        ],
      },
      {
        id: "integraciones-externas",
        title: "5. Integraciones externas de la compañía",
        paragraphs: [
          "En la ficha de la compañía, la pestaña «Integraciones externas» (solo Super Admin; los demás perfiles no la ven) administra la conexión que permite al portal público de Persona Natural hablar con FLIT 2 en nombre de esa compañía. La conexión solo puede consultar información, no modificarla. Cada compañía puede tener una sola conexión de Persona Natural activa a la vez.",
          "La tabla muestra, por cada conexión: el nombre y su identificador, el tipo (Persona Natural), la finalidad, el alcance (Solo lectura), el estado, el último acceso y la fecha en que se creó. Tiene filtros por Conexión y Estado, y paginación con «Filas por página». Los estados son: Activa; Bloqueada (indica hasta cuándo, o «Bloqueada hasta que se desbloquee» cuando la bloqueó una persona del equipo FLIT; también puede bloquearse sola por intentos fallidos) y Desactivada. Si una conexión activa aparece con la marca «Debe cambiar la clave», hay que generarle una clave nueva.",
        ],
        bullets: [
          "Crear: botón «Crear conexión». Escribe el nombre y, si quieres, la finalidad (los dos son opcionales: sin nombre se usa uno por defecto) y confirma con «Crear conexión». El botón aparece desactivado, con su explicación al lado, si la compañía ya tiene una conexión activa: primero hay que desactivarla.",
          "Ver la clave: al crear (y al generar una clave nueva) se abre una ventana con el identificador de la conexión y la clave secreta, y el botón «Copiar clave». La clave se muestra una sola vez: no se puede volver a ver ni recuperar. Cópiala, entrégala al servicio del portal por un canal seguro y cierra con «Ya la copié, cerrar». Si la pierdes, tendrás que generar una nueva.",
          "Generar una clave nueva: ícono de la llave en la fila → «Generar clave nueva». Se crea una clave nueva y se muestra una sola vez. Por defecto la clave anterior sigue sirviendo 24 horas para que el portal alcance a cambiarla; si sospechas que se filtró, marca «Dejar sin efecto la clave anterior ahora mismo». No se puede generar una clave nueva en una conexión bloqueada: primero desbloquéala.",
          "Bloquear y desbloquear: el candado de la fila. Bloquear impide que la conexión obtenga accesos nuevos hasta que alguien la desbloquee; los accesos que ya tenía siguen valiendo hasta que vencen (máximo 30 minutos). Desbloquear le permite volver a pedir accesos de inmediato con su clave vigente.",
          "Desactivar: ícono de apagar → «Desactivar». Es definitivo: la conexión no podrá obtener accesos ni volver a activarse; para reconectar el portal hay que crear una nueva. Una conexión desactivada ya no ofrece acciones.",
        ],
        callouts: [
          {
            variant: "warning",
            text: "Cada acción pide confirmación. Si el sistema no la puede aplicar (por ejemplo, otra persona ya la desactivó), el motivo se muestra en la misma ventana y no se cierra; recarga el listado para ver el estado actual.",
          },
          {
            variant: "info",
            text: "Si el listado no carga verás «No se pudieron cargar las conexiones» con la opción de reintentar; si la compañía aún no tiene conexiones, la pestaña te invita a crear la de Persona Natural con «Crear conexión».",
          },
        ],
        media: [
          {
            kind: "diagram",
            id: "flujo-rotar-secreto-integracion",
            alt: "Flujo para generar una clave nueva de una conexión externa: se confirma, se decide si la clave anterior queda sin efecto al instante o a las 24 horas, y se copia la clave nueva, que se muestra una sola vez, para entregarla al servicio del portal",
            caption: "Generar una clave nueva: la nueva se ve una sola vez.",
          },
        ],
      },
    ],
  },
  {
    slug: "4-superadmin/2-documental-e-improntas",
    title: "Gestión documental e improntas",
    audience: "Super Admin",
    sectionId: "superadmin",
    keywords: [
      "gestion documental",
      "catalogo de documentos",
      "configuracion documental",
      "overrides ot",
      "matriz resuelta",
      "improntas",
      "certificado de improntas",
      "generar impronta",
      "historial de improntas",
    ],
    summary:
      "El catálogo de documentos y su configuración por tipo de trámite, y la generación del Certificado de Improntas Digitales.",
    blocks: [
      {
        id: "documental",
        title: "1. Documental",
        paragraphs: [
          "Administradores → Documental. Administra el catálogo de tipos de documento (nombre, formatos admitidos: PDF, JPG, PNG, WEBP). Puedes crear tipos nuevos y eliminar los que ya no uses; si un tipo está en uso en algún trámite, la eliminación se bloquea con un aviso, para no dejar huecos en trámites existentes.",
          "Por tipo de trámite, la configuración documental tiene tres vistas: Documentos (lo que exige el catálogo), Overrides OT (lo que cada organismo añade o quita) y Matriz resuelta (lo que efectivamente verá el gestor, ya combinado, por organismo). Es la fuente de los requisitos que el asistente muestra en el paso de adjuntos.",
        ],
        media: [
          {
            kind: "image",
            src: "/manual/screenshots/admin-documental.png",
            alt: "Consola de gestión documental con el catálogo de tipos de documento y sus formatos admitidos",
            caption: "Documental: el catálogo del que salen los requisitos de cada trámite.",
          },
        ],
      },
      {
        id: "improntas",
        title: "2. Improntas",
        paragraphs: [
          "Generación de improntas: emite el Certificado de Improntas Digitales del vehículo (Res. 17145/2023 Mintransporte) y descárgalo. Historial de improntas: consulta las generadas para tu inquilino, filtrables por placa y rango de fechas. La firma digital de cada impronta la verifica el organismo desde su pestaña «Validar impronta».",
        ],
        media: [
          {
            kind: "image",
            src: "/manual/screenshots/admin-improntas.png",
            alt: "Consola de generación de improntas con los datos del vehículo y la organización solicitante",
            caption: "Improntas: generar el certificado y consultar el historial.",
          },
        ],
      },
    ],
  },
  {
    slug: "4-superadmin/3-plataforma",
    title: "Plataforma: tipos de trámite, mandatos, FUR, notificaciones, Confirmación RUNT y banners",
    audience: "Super Admin",
    sectionId: "superadmin",
    keywords: [
      "plataforma",
      "tipos de tramite",
      "capacidades",
      "recorrido",
      "familia",
      "identidad del tipo",
      "alta de tramite",
      "retirar tipo de tramite",
      "mandatos plantilla",
      "contrato de mandato",
      "fur simulador",
      "notificaciones",
      "plantillas de correo",
      "buzon de pruebas",
      "envio de prueba",
      "confirmacion runt",
      "banners",
      "carrusel",
    ],
    summary: "Parametrización global que define qué trámites existen, cómo se recorren y cómo se comunica la plataforma.",
    blocks: [
      {
        id: "tipos",
        title: "1. Tipos de trámite",
        paragraphs: [
          "Catálogo de trámites y su parametrización: qué recorrido sigue cada uno, qué exige y qué documentos pide. «Nuevo tipo de trámite» da de alta uno; cada tipo tiene 4 pestañas:",
        ],
        bullets: [
          "Identidad: familia (MATRÍCULAS, TRASPASO, OTROS), código, nombre, el copy que ve el gestor al elegirlo y el interruptor operable / no operable (no operable = no aparece en el selector).",
          "Capacidades: captura de actores (y si la parte compradora admite varias personas), consulta del vehículo, datos comerciales, identidad («las partes validan identidad antes de radicar»), checklist de documentos, firma y FUR («el expediente se firma antes de radicarse»), decisión de prenda (puerta que bloquea o no) y generación de impronta.",
          "Recorrido: el orden de pasos del asistente para ese tipo.",
          "Documentos: qué exige el tipo; el detalle vive en Documental.",
        ],
        callouts: [
          {
            variant: "warning",
            text: "«Retirar» un tipo de trámite lo saca del selector del gestor de inmediato, pero no borra el historial de los trámites que ya lo usaron; pídelo con confirmación explícita.",
          },
        ],
        media: [
          {
            kind: "image",
            src: "/manual/screenshots/admin-tipos-tramite.png",
            alt: "Catálogo de tipos de trámite con sus familias y el acceso a las pestañas de parametrización",
            caption: "Tipos de trámite: el catálogo que define qué puede radicar cada compañía.",
          },
        ],
      },
      {
        id: "mandatos",
        title: "2. Mandatos",
        paragraphs: [
          "La pantalla se organiza en tres pestañas: «Formatos de contrato», «Configuración por organismo» y «Simulador». En las tablas, cada fila agrupa sus opciones en un único botón «Acciones». Plantillas de Contrato Privado de Mandato por organismo. «Configurar mandato» fija la redacción que aplica el OT (plantilla, mandatario institucional / unión temporal, NIT, ciudad de cámara, sigla). «Configurar mandatario» define por compañía el tipo de mandato y el mandatario por defecto. El tipo dice quién ocupa el bloque del mandatario en el contrato: Mandatario de la compañía (firma el mandatario que la compañía registró, sea persona natural o jurídica; es el tipo por defecto), Institucional (organismo) (el organismo o su unión temporal actúa como mandatario) o Abierto (sin mandatario) (nombre, documento y firma quedan en blanco para llenarse a mano). La columna «Tipo de mandato» tiene una ayuda con esta explicación. En la tabla de organismos, la columna «Tipos por compañía» muestra Mandatario de la compañía (el tipo de un organismo nuevo y de toda compañía sin regla propia) y, si las hay, cuántas compañías están en Institucional o Abierto. En «Configurar mandatario», la tabla de compañías muestra la columna «Tipo de mandato» (Persona natural, Persona jurídica o Mandato abierto); una compañía sin regla propia aparece como Persona natural con la marca «Default». En Plataforma puedes cambiar el tipo de una compañía con el lápiz de su fila: Persona jurídica pide el nombre de la entidad (con NIT, ciudad de cámara y sigla opcionales), y Mandato abierto pide confirmación porque la compañía dejará de exigir firma de mandatario en ese organismo. En la sección «Formatos de contrato» una tabla lista cada formato con su nombre, tipo de mandato, versión vigente y fecha de la última edición (con paginación y «Filas por página»); no se pueden crear ni eliminar formatos. El lápiz de cada fila abre el editor: cambia el nombre y el tipo de mandato (el tipo es el valor por defecto del formato; la regla por compañía y organismo sigue mandando) y edita el texto de la plantilla con los botones de variables permitidas (por ejemplo {{placa}} o {{mandante_nombre}}). «Vista previa» abre un PDF de muestra del borrador sin publicarlo; si el texto usa una variable que no existe, se muestra la lista de variables inválidas y no se guarda. «Publicar» pide confirmación: el cambio aplica a los trámites nuevos, los contratos ya emitidos no cambian y requiere validación del PO y de jurídico. El historial muestra número, autor y fecha de cada versión y deja ver el texto de una anterior, sin restaurarla. Si un formato tiene la plantilla editada, su menú «Acciones» ofrece «Restablecer formato»: tras confirmar, el formato vuelve al texto original de FLIT para los mandatos que se generen desde ahora (la versión vigente pasa a «De fábrica»); el nombre, el tipo y el historial de versiones se conservan y los mandatos ya generados no cambian. La Automática no lo ofrece porque usa la plantilla de cada organismo. Si otra persona editó el mismo formato verás un aviso, la fila se recarga y tu texto se conserva. Esta sección solo la ve el Super Admin; el Admin OT y el Admin de Compañía no tienen la entrada de menú y la URL directa muestra acceso denegado. Si otra persona modificó la regla mientras editabas, verás un aviso, la fila se recarga con el tipo actual y no pierdes lo escrito. «Volver al default» devuelve la compañía a Persona natural (Default). La lista de redacciones del selector (y las tarjetas «Formatos de contrato») sale del catálogo del sistema y muestra el nombre vigente de cada formato; si la lista no carga verás un aviso con «Reintentar» y no se ofrecen opciones, y si un organismo guarda un formato que ya no existe se muestra su código sin romper la pantalla. Al cambiar la redacción en el selector, los campos de la entidad (nombre, NIT, ciudad de cámara y sigla) aparecen o se ocultan al instante, según la redacción elegida; el nombre es obligatorio cuando se muestran. El tipo por defecto de un organismo nuevo es Persona natural. En «Configuración por organismo», «Restablecer default» abre una confirmación que lista lo que se perderá (la redacción elegida, el mandatario general del OT y la plantilla propia, solo los que existan) y aclara que las reglas por compañía no se eliminan. Incluye simulador del contrato.",
        ],
      },
      {
        id: "fur-notif",
        title: "3. FUR, Notificaciones y Configuración de IA",
        paragraphs: [
          "FUR: simula cómo se construye el Formulario Único de Registro con datos sintéticos, para validar plantillas sin un trámite real.",
          "Notificaciones: banco de plantillas de correo por canal y compañía (lectura), con vista previa que respeta el tema de la red (marca blanca en Marca Blanca). Antes de mandar una prueba, configura el buzón de pruebas (un correo propio para recibirlas); sin él, el botón de envío de prueba te pide configurarlo primero. Con el buzón listo, «Enviar prueba» manda el correo real a esa bandeja para revisar Seguridad, Trámites o Analítica antes de que lo reciba un cliente.",
          "Configuración de IA: define por herramienta (OCR y DR. FLIT) el proveedor activo y el orden de prioridad entre Anthropic, OpenAI, Kimi y DeepSeek. Solo ves si la API key está configurada en el entorno y una máscara; nunca el secreto. Puedes activar un proveedor con confirmación, guardar prioridad y opciones, revisar el historial de cambios y marcar como leídas las alertas de failover automático. Esta sección solo la ve el Super Admin.",
        ],
      },
      {
        id: "runt-banners",
        title: "4. Confirmación RUNT y Banners",
        paragraphs: [
          "Confirmación RUNT: consulta periódica al RUNT para confirmar que los trámites aprobados en FLIT quedaron registrados. Pestaña Configuración (global, permiso de administrar) y pestaña Historial (corridas e intentos por trámite: qué se consultó, qué respondió el RUNT y por qué se marcó SÍ o NO).",
          "Banners: configura y programa el contenido del carrusel informativo que ven los usuarios, sin intervención técnica.",
        ],
        media: [
          {
            kind: "image",
            src: "/manual/screenshots/admin-banners.png",
            alt: "Consola de banners promocionales con la programación del carrusel del dashboard",
            caption: "Banners: lo que rota en el carrusel del Inicio de todos los usuarios.",
          },
        ],
      },
    ],
  },
  {
    slug: "4-superadmin/4-integraciones-y-procesos",
    title: "Integraciones (Quipux, ICT, Log QX), procesos periódicos y migración",
    audience: "Super Admin",
    sectionId: "superadmin",
    keywords: [
      "quipux",
      "integracion quipux",
      "interruptor maestro",
      "log qx",
      "ict",
      "log ict",
      "alertas ict",
      "trazabilidad ict",
      "reportes ict",
      "novedades",
      "atascados",
      "webhooks ict",
      "procesos periodicos",
      "jobs",
      "cadencia",
      "migracion v1",
      "sistema anterior",
      "un tramite",
      "carga masiva migracion",
    ],
    summary: "Cómo se observan y se configuran las integraciones con secretarías y terceros, los procesos automáticos y la migración desde FLIT 1.",
    blocks: [
      {
        id: "quipux",
        title: "1. Quipux y Log QX",
        paragraphs: [
          "Integración Quipux: un interruptor maestro prende o apaga toda la radicación automática; puedes guardar la configuración incompleta, pero mientras falten campos obligatorios los procesos no radicarán. Debajo del interruptor: la conexión (URLs de login, registro de documento y validación de estado, usuario y contraseña, código de consumidor), el almacenamiento S3 donde se publica el PDF consolidado que Quipux lee (bucket, prefijo, región y credenciales AWS), la entidad que radica (tipo y número de documento de FLIT ante la secretaría) y la cadencia y límites de los workers (intervalos de registro y consulta, tamaño de lote, máximo de intentos y de consultas, timeout).",
          "Log QX (píldora Integraciones): bandeja de trámites con integración Quipux, filtrable por estado, fecha, placa o documento; abre cualquiera para ver la trazabilidad completa de sus envíos. Requiere el permiso de lectura de Log QX.",
        ],
      },
      {
        id: "ict",
        title: "2. ICT (Integración con Terceros)",
        paragraphs: [],
        bullets: [
          "Log ICT: dos pestañas, Logs (peticiones ya enmascaradas por autenticación, transacción, webhook o fuente externa) y Alertas ICT (métricas y eventos de la integración).",
          "Trazabilidad ICT: qué pasó con cada trámite que entró por la integración; busca por número, placa o VIN y abre su recorrido completo.",
          "Reportes ICT: 5 pestañas — Novedades, Atascados, Jobs (rendimiento por proceso, solo Super Admin), Webhooks y Consultas (con envío programado).",
        ],
      },
      {
        id: "jobs",
        title: "3. Procesos periódicos",
        paragraphs: [
          "Catálogo unificado de los procesos automáticos de la plataforma, filtrable por Todos / ICT / Quipux: estado de cada uno y su cadencia, configurada una sola vez por módulo. Cadencia ICT: ventana horaria (Bogotá), intervalos, lotes y concurrencia; los cambios aplican en el siguiente ciclo, sin reinicio.",
        ],
        media: [
          {
            kind: "image",
            src: "/manual/screenshots/admin-jobs.png",
            alt: "Catálogo de procesos periódicos con el filtro por módulo y la cadencia de cada job",
            caption: "Procesos periódicos: los automatismos de la plataforma y su cadencia.",
          },
        ],
      },
      {
        id: "migracion",
        title: "4. Migración V1 → V2",
        paragraphs: [
          "Trae trámites del sistema anterior en dos pestañas: «Un trámite» (uno por uno) y «Carga masiva» (con reporte de resultado por fila). Reintentar es seguro: un trámite ya migrado no se duplica.",
        ],
        callouts: [
          {
            variant: "info",
            text: "Esta consola se abre con el enlace directo /admin/migracion: es deliberadamente invisible, sin entrada en el dock ni en ningún menú, porque es una herramienta temporal de una migración concreta.",
          },
        ],
      },
    ],
  },
  {
    slug: "4-superadmin/5-rbac-y-auditoria",
    title: "RBAC, usuarios y auditoría",
    audience: "Super Admin",
    sectionId: "superadmin",
    keywords: [
      "usuario ya creado",
      "correo duplicado",
      "rbac",
      "roles",
      "permisos",
      "modulos",
      "modulos y permisos",
      "roles del sistema",
      "roles de compania",
      "roles de organismo",
      "auditoria",
      "rastro",
      "seguridad",
      "usuarios flit",
      "perfil flit",
      "usuarios eliminados",
      "restaurar usuario",
      "eliminar usuario",
      "permisos por modulo",
      "permisos individuales",
      "heredado del rol",
      "auditoria de permisos",
    ],
    summary: "Cómo se definen módulos, permisos y roles, quién los recibe y dónde queda el rastro.",
    blocks: [
      {
        id: "rbac",
        title: "1. RBAC Admin",
        paragraphs: [
          "Gestiona módulos, permisos y roles del sistema, en 2 pestañas: Módulos y Permisos (las entradas navegables del sistema, con sus acciones) y Roles del sistema (que agrupan permisos y se separan en dos sub-pestañas: «Compañía» y «Organismo de Tránsito»). En cada fila, el botón «Acciones» reúne lo que puedes hacer: en un módulo, «Agregar permiso», desactivarlo o activarlo y eliminarlo; en un rol, «Editar permisos», desactivarlo o activarlo y eliminarlo (un rol de sistema no se puede eliminar). El dock de cada usuario se filtra por los módulos accesibles de su rol; la API vuelve a validar cada permiso.",
        ],
        callouts: [
          {
            variant: "warning",
            text: "Quitar un permiso a un rol afecta de inmediato a todos sus usuarios. El Super Admin no pasa por RBAC: tiene bypass total.",
          },
          {
            variant: "info",
            text: "El editor de un rol es una sola lista, en este orden: «Datos del rol» (solo al crear: tipo de entidad, nombre y descripción), «Módulos» y «Tipos de servicio». No se escribe ningún código: la plataforma asigna uno interno a partir del nombre. «Crear rol» y «Guardar permisos» guardan todo; si falta el nombre, ves el aviso debajo del campo y el cursor queda allí. Sin tipos de servicio en el catálogo solo ves la lista de módulos.",
          },
          {
            variant: "info",
            text: "En «Tipos de servicio», «Trámites: Matrículas», «Trámites: Traspasos» y «Trámites: Otros trámites» muestran cada uno el par «Ver» / «Crear». «Crear» aparece deshabilitado mientras «Ver» esté sin marcar y, si desmarcas «Ver», «Crear» se desmarca también. Si el sistema devuelve «Para habilitar «Crear» primero debe estar habilitado «Ver» en ese tipo de servicio.», marca «Ver» y guarda de nuevo.",
          },
        ],
        media: [
          {
            kind: "image",
            src: "/manual/screenshots/admin-rbac.png",
            alt: "Consola RBAC con las pestañas de módulos y permisos y de roles del sistema",
            caption: "RBAC: quién puede ver y hacer qué, por rol.",
          },
        ],
      },
      {
        id: "usuarios",
        title: "2. Usuarios",
        paragraphs: [
          "El módulo Usuarios para el Super Admin es global: eliges el destino de la invitación (compañía, organismo o perfil FLIT interno) y los roles que aplican a ese destino. Al invitar desde la pestaña Usuarios del hub de un organismo, el rol es obligatorio: no hay rol por defecto y, hasta que lo eliges, «Enviar invitación» queda deshabilitado (incluido el primer Administrador OT). Un perfil FLIT no pertenece a una compañía ni a un organismo y admite un solo rol.",
          "Cada correo corresponde a un solo usuario en toda la plataforma, sin importar la compañía ni las mayúsculas (Juan@empresa.com y juan@empresa.com son el mismo). El correo se guarda siempre en minúsculas. Si ya existe un usuario con ese correo, o ya hay una invitación pendiente para él en cualquier compañía, la invitación no se crea, ves «El usuario ya se encuentra previamente creado» y el campo de correo queda marcado en rojo; corrige el correo para continuar.",
          "«Eliminar usuario» es un borrado lógico: el usuario sale del listado activo y pierde el acceso, pero queda visible en la pestaña Eliminados, donde «Restaurar» lo devuelve a activo con sus roles previos. Cada usuario tiene además su propio historial de auditoría: quién lo invitó, qué roles cambiaron y cuándo.",
          "Las acciones de cada usuario (historial, permisos por módulo, editar, restablecer contraseña, suspender, desactivar y eliminar) están en el botón «Acciones» de su fila; las que quitan acceso van al final. Reenviar o reactivar una invitación sigue siendo el ícono de la fila.",
          "Permisos por módulo: en la fila del usuario abre «Acciones» y elige «Permisos por módulo»; ves cada módulo y cada acción con su origen: «Heredado del rol» (lo que dicta su rol), «Individual» (un permiso que se le dio o quitó solo a él) o «Parcial» (módulo con acciones de ambos orígenes). Puedes habilitar o denegar un módulo completo o una acción, y «Volver al rol» deja el permiso como lo dicta el rol. Cada permiso individual dice quién lo otorgó y cuándo. Solo aparecen los permisos que la plataforma ya exige. La ficha es una sola lista: primero «Módulos» y después «Tipos de servicio» (si la persona no tiene tipos de servicio, solo ves la lista). Ver y Crear por tipo de servicio: en «Tipos de servicio», «Trámites: Matrículas», «Trámites: Traspasos» y «Trámites: Otros trámites» tienen cada uno un par «Ver» / «Crear». «Crear» solo se puede habilitar si «Ver» está habilitado, y al apagar «Ver» también se apaga «Crear». Cada interruptor muestra si viene «Heredado del rol» o es «Individual»; «Crear» queda deshabilitado mientras el «Ver» de la persona (heredado o individual) esté inactivo, y si «Volver al rol» deja a la persona sin «Ver», su «Crear» individual también se retira. Los permisos que un administrador de la compañía o del organismo no podría otorgar llevan la etiqueta «Solo SA» con un candado: son exclusivos del Super Admin. Al habilitar el permiso de revelar datos personales aparece un aviso de la Ley 1581 de 2012: solo se guarda si marcas «Confirmo» y pulsas «Otorgar»; si cancelas, no cambia nada, y la confirmación queda en la auditoría. Ese permiso no se otorga con «Habilitar módulo»; se hace uno a uno. Tras cada cambio guardado ves un aviso verde que te dice cuándo entra en vigor (la próxima vez que la persona inicie sesión, con el máximo de espera); si algo falla, ves un aviso de error. Si el usuario es Super Admin, o su invitación sigue pendiente, la sección no permite cambios y te explica por qué.",
          "Cambio de rol con permisos individuales: al cambiar el rol de un usuario desde «Editar» (en Usuarios, en la ficha de la compañía o en el hub del organismo), si tiene permisos individuales el sistema te avisa cuántos se eliminarán al cambiar su rol y el rol solo cambia si confirmas con «Cambiar rol». «Mantener rol actual» cancela el cambio sin tocar nada.",
        ],
      },
      {
        id: "auditoria",
        title: "3. Auditoría",
        paragraphs: [
          "Rastro global de operaciones administrativas y de seguridad: usuarios, roles, permisos y autenticación, además del módulo Trámites (creación, aceptación de términos, accesos de red) y el módulo Mandatarios (baja, reactivación, eliminación, retiro del default y reasignación de trámites). Filtra por módulo, actor, entidad y fecha. Es de solo lectura y no se puede alterar (append-only).",
          "Auditoría de permisos individuales: en Usuarios, la pestaña «Auditoría de permisos» lista cada cambio de permisos por usuario con su fecha y hora (DD/MM/AAAA HH:mm), el administrador que lo hizo, el usuario afectado, la compañía, el permiso, la acción (otorgó, denegó, volvió al rol) y el valor antes y después; los intentos rechazados quedan registrados con su motivo. Filtra por usuario, compañía y administrador; si ningún registro coincide, la tabla te lo avisa. La lista se pagina con «Filas por página».",
        ],
      },
    ],
  },
  {
    slug: "4-superadmin/6-dr-flit-global",
    title: "DR. FLIT para el equipo FLIT",
    audience: "Super Admin",
    sectionId: "superadmin",
    keywords: [
      "dr flit superadmin",
      "buscar en todas las companias",
      "soporte interno",
      "asistente global",
      "casos de soporte",
      "tablero de soporte",
      "tope de mensajes",
    ],
    summary:
      "Qué cambia en el asistente cuando quien pregunta es Super Admin: alcance global, enlace directo a los casos y operación del chat con IA.",
    blocks: [
      {
        id: "alcance",
        title: "1. Búsqueda global",
        paragraphs: [
          "Como Super Admin, DR. FLIT busca en todas las compañías: por placa, VIN, radicado o cliente. Cada tarjeta muestra la compañía dueña del trámite para que sepas dónde estás mirando. El historial por placa también es global.",
        ],
      },
      {
        id: "ayuda",
        title: "2. Ayuda con inteligencia artificial",
        paragraphs: [
          "El chat responde en lenguaje natural con base en toda la documentación (Gestor, Organismo, Administración de compañía y Super Admin), citando los artículos usados. Sirve para responder por un cliente: escribe la duda tal como te la plantearon y abre el artículo que corresponde a su perfil.",
          "Aplican las mismas reglas que a todos: autorización de tratamiento de datos la primera vez, tope diario de mensajes con aviso previo, y respuesta del buscador del manual cuando la inteligencia artificial no está disponible.",
        ],
      },
      {
        id: "casos",
        title: "3. Casos de soporte: qué ve el equipo FLIT",
        paragraphs: [
          "Los casos radicados desde el chat llegan al tablero de soporte del equipo FLIT con la descripción, el resultado esperado, la frecuencia, la prioridad y los adjuntos del usuario, listos para triage. A los usuarios se les confirma solo el número de caso; como Super Admin, tú además ves el enlace directo al caso en el tablero.",
        ],
        callouts: [
          {
            variant: "info",
            text: "El tope diario de mensajes del chat y el modelo de inteligencia artificial se configuran por ambiente desde la operación de la plataforma; si el equipo necesita ajustarlos, es un cambio de configuración, no de código.",
          },
        ],
      },
    ],
  },
  {
    slug: "4-superadmin/7-plataforma-persona-natural",
    title: "Plataforma: Persona Natural (trámites, organismos, tarifas, ajustes y contenido del portal)",
    audience: "Super Admin",
    sectionId: "superadmin",
    keywords: [
      "persona natural",
      "portal persona natural",
      "portal del ciudadano",
      "estado de la configuracion",
      "que falta para ofrecer un tramite",
      "tarifas",
      "tarifa vigente",
      "nueva tarifa",
      "cambiar valores de una tarifa",
      "historial de tarifas",
      "derechos de transito",
      "servicio digital",
      "cotizaciones",
      "organismos habilitados",
      "ofrecer en el portal",
      "dejar de ofrecer",
      "tramites que ofrece el portal",
      "falta tarifa",
      "activo en flit",
      "ya no esta activo en flit",
      "tramite no disponible",
      "ajustes del portal",
      "duracion de la sesion",
      "limite de consultas",
      "captcha",
      "soporte del portal",
      "preguntas frecuentes",
      "banners del portal",
      "enlaces legales",
      "politica de datos",
      "terminos y condiciones",
      "ver como quedaria",
      "publicar cambios",
      "cambios sin publicar",
      "publicaciones anteriores",
    ],
    summary:
      "Cómo el Super Admin deja listo el portal de Persona Natural: qué trámites se ofrecen y en qué organismos, cuánto cuestan, los ajustes del portal y los textos que ve el ciudadano.",
    blocks: [
      {
        id: "entrada",
        title: "1. Dónde está y qué falta configurar",
        paragraphs: [
          "Plataforma → «Persona Natural». Es la pantalla desde la que el equipo FLIT decide qué trámites puede hacer una persona natural desde el portal público, en qué organismos de tránsito y cuánto cuestan. Solo la ve el Super Admin con el permiso de administrar Persona Natural: los demás perfiles no tienen la entrada en el menú y, si abren la dirección directa, ven el mensaje «No tienes permiso para administrar el portal de Persona Natural.».",
          "Arriba está el «Estado de la configuración». Dice que para ofrecer un trámite al ciudadano necesitas tres cosas, y muestra cada una como «Hecho» o «Pendiente» con un botón que te lleva directo a donde se resuelve: 1. «Un tipo de trámite» (botón «Elegir trámites»), 2. «Un organismo de tránsito» (botón «Elegir organismos») y 3. «Una tarifa vigente» (botón «Configurar tarifas»). Debajo, una frase resume lo que el ciudadano ve hoy, por ejemplo «Hoy el ciudadano puede iniciar 2 tipos de trámite en 5 organismos de tránsito», o qué falta si todavía no puede iniciar ninguno. Si hay organismos habilitados sin tarifa, un aviso lo dice. El estado se actualiza solo cuando cambias algo.",
          "Debajo hay cinco pestañas, en el orden de los pasos: «Trámites» (paso 1), «Organismos» (paso 2), «Tarifas» (paso 3), «Ajustes del portal» y «Contenido». Un trámite que no está listo o un organismo que ya no está activo en FLIT no cuentan como «Hecho». Cada una carga cuando entras en ella y muestra los cuatro estados de siempre: cargando, vacío, error con «Reintentar» y con datos. Para conectar el portal con una compañía se usa, además, la pestaña «Integraciones externas» de la ficha de la compañía (ver «Compañías, organismos de tránsito y causales»).",
        ],
      },
      {
        id: "organismos-tramites",
        title: "2. Trámites y organismos",
        paragraphs: [
          "Aquí decides qué se ofrece en el portal, en dos pestañas separadas. Una regla manda sobre todo lo demás, y la pestaña «Trámites» la recuerda arriba: un trámite aparece cuando está activado, el organismo está habilitado y existe una tarifa vigente para esa combinación. Si falta algo, el ciudadano no lo ve.",
          "«Trámites» (paso 1, «Trámites que ofrece el portal») lista los trámites activos y publicados en FLIT 2. Cada uno muestra su categoría, su disponibilidad («Listo para ofrecer», «Aún no disponible» o «Ya no está activo en FLIT»), el interruptor «Ofrecer en el portal», si «Lo ve el ciudadano» (Sí, No o «Falta tarifa») y en cuántos organismos tiene tarifa. Un trámite no disponible no se puede activar: su interruptor queda bloqueado y se lee el motivo. Uno que ya no está activo en FLIT sigue en la lista solo para que puedas dejar de ofrecerlo.",
          "«Organismos» (paso 2, «Organismos de tránsito») lista solo los organismos de tránsito activos en FLIT. Cada uno muestra su municipio y departamento, el interruptor «Ofrecer en el portal» y cuántos trámites tiene con tarifa. Si un organismo habilitado deja de estar activo en FLIT, sigue en la lista marcado «Ya no está activo en FLIT» y solo puedes dejar de ofrecerlo. Tiene un buscador por nombre, municipio o departamento y filtros por Organismo, Departamento y «Ofrecer en el portal». Ambas tablas tienen paginación con «Filas por página».",
        ],
        bullets: [
          "Ofrecer un trámite u organismo: usa el interruptor de la fila. Verás un aviso como «… ahora se ofrece en el portal.».",
          "Dejar de ofrecerlo: apaga el interruptor. Antes se abre una confirmación que explica que el ciudadano dejará de verlo hasta que lo vuelvas a activar (las tarifas ya configuradas se conservan); confirma con «Dejar de ofrecer» o cancela y no cambia nada.",
          "Aviso «Falta tarifa»: el trámite o el organismo está activado pero no tiene tarifa vigente, así que el ciudadano no puede cotizar. El botón «Crear tarifa» de ese aviso te lleva a la pestaña «Tarifas».",
        ],
        callouts: [
          {
            variant: "tip",
            text: "Habilitar un organismo no basta: también necesita una tarifa vigente. Si no ves un trámite en el portal, revisa primero las tres condiciones de la regla o mira el «Estado de la configuración».",
          },
        ],
      },
      {
        id: "tarifas",
        title: "3. Tarifas",
        paragraphs: [
          "Lista cuánto paga el ciudadano por cada trámite en el portal, agrupado por organismo y trámite. Cada fila muestra el organismo (o «Todos los organismos» si el valor aplica a todos), el trámite, los Derechos de tránsito, el Servicio digital FLIT, el Envío, otros cargos, desde cuándo rige y su estado: Vigente, Próxima (aún no rige) o Anterior (ya no rige). Los valores se ven en pesos, con puntos de miles. Tiene filtros por Organismo, Trámite, «Vigente desde» y Estado, y por defecto no muestra las anteriores: marca «Mostrar también las tarifas anteriores (ya no rigen)» para ver todo.",
          "Una tarifa de un organismo tiene prioridad sobre el valor general del trámite. Los cargos que se pueden registrar son Derechos de tránsito, Servicio digital, Cambio de color, Conversión de combustible, Cambio de carrocería y Envío del documento.",
        ],
        bullets: [
          "Crear una tarifa nueva: botón «Nueva tarifa». Elige el trámite y el organismo (o «Todos los organismos (valor general)»), indica desde qué fecha rige («Rige desde»; debajo ves «Desde el … el portal cotiza con estos valores») y escribe el valor en pesos de al menos un cargo; al salir del campo se escribe con puntos de miles. Los Derechos de tránsito y el Servicio digital siempre se registran para un trámite concreto; «Todos los trámites (solo cargos adicionales)» sirve solo para los demás cargos.",
          "Cambiar los valores de una tarifa: en la fila, el lápiz «Cambiar valores de…». No se edita la tarifa anterior: se registra una nueva con otra fecha. Cada campo muestra el valor de hoy y solo viajan los que cambies. La tarifa anterior deja de regir el día antes del inicio de la nueva. Las tarifas anteriores ya no ofrecen cambiar valores.",
          "Fecha de inicio: puede ser hoy o una fecha futura; no se admite una fecha pasada.",
          "Ver el historial: ícono de historial en la fila («Ver historial de…»). Muestra, por cargo, los valores anteriores con su vigencia, estado y la fecha en que se registraron.",
        ],
        callouts: [
          {
            variant: "info",
            text: "Las cotizaciones que ya se emitieron no cambian: una tarifa nueva solo aplica desde su fecha de inicio.",
          },
          {
            variant: "warning",
            title: "Mensajes frecuentes",
            text: "«La fecha de inicio no puede ser anterior a hoy.» Elige hoy o una fecha futura. «Ya hay una tarifa para este trámite y organismo que empieza ese mismo día o después. Elige una fecha de inicio posterior.» Cambia la fecha de inicio. «Cambia al menos un valor para registrar el cambio.» No modificaste ningún valor. «Los derechos de tránsito y el servicio digital se registran siempre para un trámite concreto: elige el trámite.»",
          },
        ],
      },
      {
        id: "ajustes",
        title: "4. Ajustes del portal",
        paragraphs: [
          "Ajustes generales de cómo se comporta el portal para el ciudadano, ordenados en tarjetas. Si dejas un campo vacío, el portal usa su valor habitual; debajo de cada campo se explica qué significa y el rango permitido, y la unidad (minutos, días, consultas) se ve dentro del campo.",
          "Tres tarjetas: «Tiempos y límites» (Duración de la sesión, Días que se guarda un trámite sin enviar, Consultas por persona al día y Consultas de todo el portal al día), «Verificación anti-robots» (el captcha puede quedar en «Lo habitual del portal», «Activada» o «Desactivada») y «Contacto de soporte»: correo, teléfono y horario de atención (opcionales), pensados para que el ciudadano pida ayuda.",
          "Abajo hay una barra con el estado («Hay cambios sin guardar» o «No hay cambios por guardar») y la fecha de la última actualización. Para guardar usa «Guardar ajustes»; el botón queda inactivo mientras no haya cambios. Si hay cambios, «Descartar cambios» devuelve el formulario a lo guardado. Si un valor está fuera de rango o el correo no es válido, el mensaje aparece bajo el campo y no se guarda nada.",
        ],
        callouts: [
          {
            variant: "warning",
            title: "Si otra persona editó al mismo tiempo",
            text: "Verás el aviso «Alguien más cambió estos ajustes». Lo que escribiste se conserva en pantalla, pero para guardarlo debes pulsar «Cargar valores actuales» y volver a aplicar tus cambios.",
          },
        ],
      },
      {
        id: "contenido",
        title: "5. Contenido del portal",
        paragraphs: [
          "Administra lo que el ciudadano lee en el portal sin intervención técnica, en tres secciones: «Preguntas frecuentes», «Banners» y «Enlaces legales y de contacto». Lo que cambias aquí no lo ve el ciudadano hasta que lo publiques. El panel «Publicación del contenido» muestra «Lo que ve hoy el ciudadano» (el contenido por defecto del portal, o el que publicaste con su fecha y hora), dice «Hay cambios sin publicar» o «Todo está publicado» y trae «Ver cómo quedaría», «Publicaciones anteriores» y «Publicar cambios». Cada sección es una tarjeta con un contador (por ejemplo «3 preguntas · 2 visibles») y su botón de agregar.",
        ],
        bullets: [
          "Preguntas frecuentes: «Nueva pregunta» pide la pregunta y la respuesta (texto simple, sin formato ni código) y si se muestra en el portal. En cada fila puedes editar, subir o bajar para cambiar el orden y usar el interruptor para mostrar u ocultar (ocultar pide confirmación). El formulario cuenta los caracteres (300 la pregunta, 2000 la respuesta) y muestra cómo se verá en el portal. Estado: Visible u Oculta. Filtra por Pregunta y Estado.",
          "Banners: «Nuevo banner» pide el título, la imagen (JPG, PNG o WEBP de hasta 2 MB), una descripción de la imagen para quienes no pueden verla, un enlace opcional y, si quieres, «Mostrar desde» y «Mostrar hasta». Cada banner se puede editar, mover de orden y mostrar u ocultar con su interruptor (ocultar pide confirmación). Su estado es Vigente, Programado (aún no empieza), Vencido (ya terminó) u Oculto. La descripción de la imagen es para personas con discapacidad visual que usan lector de pantalla.",
          "Enlaces del portal: la Política de tratamiento de datos, los Términos y condiciones, el Contacto de ventas y el Video explicativo. Cada enlace dice «Configurado» o «Falta configurar». Edita cada uno con el lápiz (la dirección debe empezar por http:// o https://); en los dos enlaces legales, si no hay documento oficial déjalo vacío y el portal conserva su aviso actual. Los demás aceptan una página web, un correo o un teléfono según el caso.",
          "Ver cómo quedaría: muestra cómo lo vería el ciudadano con tus cambios, sin publicarlos todavía.",
          "Publicar cambios: pide confirmación y dice cuántas preguntas y banners visibles verá el ciudadano desde ahora. Avisa que lo que publicas reemplaza las preguntas y los banners que el portal muestra por defecto y que una publicación no se puede editar: para cambiar algo, haces el cambio y publicas otra vez. Confirma con «Sí, publicar». El botón está inactivo si no hay cambios por publicar.",
          "Publicaciones anteriores: lista cada publicación con su fecha y cuántas preguntas y banners tenía, de la más reciente a la más antigua.",
        ],
        callouts: [
          {
            variant: "info",
            text: "Cada cambio de una pregunta, banner o enlace dice «… guardado. El ciudadano lo verá cuando publiques». Nada llega al ciudadano hasta que pulses «Publicar cambios».",
          },
        ],
        media: [
          {
            kind: "diagram",
            id: "flujo-publicar-contenido-persona-natural",
            alt: "Flujo de publicación del contenido del portal: se editan las preguntas, banners y enlaces, se revisa cómo quedaría, si está bien se publica y el ciudadano lo ve, y si no se vuelve a editar",
            caption: "De los cambios a lo publicado: el ciudadano solo ve lo que se publicó.",
          },
        ],
      },
    ],
  },
];
