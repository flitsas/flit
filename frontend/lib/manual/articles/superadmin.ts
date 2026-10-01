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
    ],
    summary:
      "Alta y ciclo de vida de compañías y organismos, la ficha completa de cada compañía, entrada al hub OT y catálogo de causales de rechazo.",
    blocks: [
      {
        id: "companias",
        title: "1. Compañías",
        paragraphs: [
          "Administradores → Compañías. Catálogo de compañías B2B con filtros, estado y el toggle «incluir OT» para ver también los organismos de tránsito en el mismo listado. «Crear compañía»: razón social, NIT, código único (máx. 32), tipo de compañía (normal, Concesión o Marca Blanca) y, para Marca Blanca, dominio propio. «Activar / Desactivar» cambia el estado con confirmación; una compañía de tipo de sistema no se edita desde aquí.",
          "Abrir una fila lleva a la ficha completa de la compañía, con 7 pestañas:",
        ],
        bullets: [
          "Trámites: familias habilitadas (Matrículas, Traspaso, Otros) con switch de bloqueo por familia y la lista blanca de trámites permitidos.",
          "Configuración Empresa: parámetros de firma (con la firma precargada desde el baúl), validar SOAT ante RUNT, avisos al aprobar o rechazar y sus destinatarios, métodos de recaudo, fuente de comparendos, módulos activos del dashboard (Comparendos, Resoluciones), proveedores de consulta y de avalúo, y la tabla de organismos de tránsito con los bloqueos y restricciones que aplica cada uno. El buscador de esa tabla (por nombre o código, con o sin tildes) recorre todos los organismos, no solo la página que estás viendo.",
          "Documentos: parámetros documentales de la gestora.",
          "Representantes legales.",
          "Mandatarios: el mismo formulario que ve el Administrador de la compañía, con modelo (Persona natural, Persona jurídica o Formato en blanco), forma de firma y vigencia. La lista muestra el modelo y la etiqueta de vigencia (Vigente, Por vencer, Vencido, Inactivo, Aún no vigente; guion en Persona jurídica y Formato en blanco). Ver el artículo «Representantes legales y mandatarios» de Administrador de compañía.",
          "Usuarios.",
          "Historial de Cambios: quién cambió qué y cuándo.",
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
            alt: "Administración de compañías con el toggle de incluir organismos, los filtros de búsqueda y el listado con NIT, razón social, tipo y estado",
            caption: "Administración de compañías: el punto de entrada a la ficha de cada tenant.",
          },
        ],
      },
      {
        id: "companias-red",
        title: "2. Cabezas de red",
        paragraphs: [
          "Si la compañía es cabeza de red (Concesión o Marca Blanca), su ficha suma el panel de hijas: agregar un cliente a la red, activar o inactivar una hija e invitar usuarios para que la administren.",
          "Si además es Marca Blanca, configura la marca y el dominio propio de la red: registrar el dominio, cambiarlo, retirarlo y comprobarlo (validación de propiedad). Estas cuatro acciones son exclusivas del Super Admin; el Administrador de la cabeza de red solo las ve en modo lectura.",
        ],
      },
      {
        id: "organismos",
        title: "3. Tránsito → Organismos",
        paragraphs: [
          "Catálogo de organismos de tránsito con código DIVIPOL, código integrador y departamento; filtra por estado, departamento o integrador. «Activar organismo de tránsito» crea su inquilino y lo deja operable; desde la fila entras a su hub, con 10 pestañas: Trámites (bandeja de decisión), Reglas, Documentos (prelación y etiquetas), Requisitos, Usuarios, Reportes, Mandatos, Validar impronta, Revocatorias (decidir las solicitudes que llegan del gestor) y Configuración (modo Quipux, ventana de revocatoria, feature flags).",
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
          "Plantillas de Contrato Privado de Mandato por organismo. «Configurar mandato» fija la redacción que aplica el OT (plantilla, mandatario institucional / unión temporal, NIT, ciudad de cámara, sigla). «Configurar mandatario» define por compañía el tipo de mandato (Persona natural · Persona jurídica · Mandato abierto) y el mandatario por defecto. En «Configurar mandatario», la tabla de compañías muestra la columna «Tipo de mandato» (Persona natural, Persona jurídica o Mandato abierto); una compañía sin regla propia aparece como Persona natural con la marca «Default». En Plataforma puedes cambiar el tipo de una compañía con el lápiz de su fila: Persona jurídica pide el nombre de la entidad (con NIT, ciudad de cámara y sigla opcionales), y Mandato abierto pide confirmación porque la compañía dejará de exigir firma de mandatario en ese organismo. En la sección «Formatos de contrato» una tabla lista cada formato con su nombre, tipo de mandato, versión vigente y fecha de la última edición (con paginación y «Filas por página»); no se pueden crear ni eliminar formatos. El lápiz de cada fila abre el editor: cambia el nombre y el tipo de mandato (el tipo es el valor por defecto del formato; la regla por compañía y organismo sigue mandando) y edita el texto de la plantilla con los botones de variables permitidas (por ejemplo {{placa}} o {{mandante_nombre}}). «Vista previa» abre un PDF de muestra del borrador sin publicarlo; si el texto usa una variable que no existe, se muestra la lista de variables inválidas y no se guarda. «Publicar» pide confirmación: el cambio aplica a los trámites nuevos, los contratos ya emitidos no cambian y requiere validación del PO y de jurídico. El historial muestra número, autor y fecha de cada versión y deja ver el texto de una anterior, sin restaurarla. Si otra persona editó el mismo formato verás un aviso, la fila se recarga y tu texto se conserva. Esta sección solo la ve el Super Admin; el Admin OT y el Admin de Compañía no tienen la entrada de menú y la URL directa muestra acceso denegado. Si otra persona modificó la regla mientras editabas, verás un aviso, la fila se recarga con el tipo actual y no pierdes lo escrito. «Volver al default» devuelve la compañía a Persona natural (Default). La lista de redacciones del selector (y las tarjetas «Formatos de contrato») sale del catálogo del sistema y muestra el nombre vigente de cada formato; si la lista no carga verás un aviso con «Reintentar» y no se ofrecen opciones, y si un organismo guarda un formato que ya no existe se muestra su código sin romper la pantalla. Al cambiar la redacción en el selector, los campos de la entidad (nombre, NIT, ciudad de cámara y sigla) aparecen o se ocultan al instante, según la redacción elegida; el nombre es obligatorio cuando se muestran. El tipo por defecto de un organismo nuevo es Persona natural. «Restablecer default» abre una confirmación que lista lo que se perderá (la redacción elegida, el mandatario general del OT y la plantilla propia, solo los que existan) y aclara que las reglas por compañía no se eliminan. Incluye simulador del contrato.",
        ],
      },
      {
        id: "fur-notif",
        title: "3. FUR y Notificaciones",
        paragraphs: [
          "FUR: simula cómo se construye el Formulario Único de Registro con datos sintéticos, para validar plantillas sin un trámite real.",
          "Notificaciones: banco de plantillas de correo por canal y compañía (lectura), con vista previa que respeta el tema de la red (marca blanca en Marca Blanca). Antes de mandar una prueba, configura el buzón de pruebas (un correo propio para recibirlas); sin él, el botón de envío de prueba te pide configurarlo primero. Con el buzón listo, «Enviar prueba» manda el correo real a esa bandeja para revisar Seguridad, Trámites o Analítica antes de que lo reciba un cliente.",
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
    ],
    summary: "Cómo se definen módulos, permisos y roles, quién los recibe y dónde queda el rastro.",
    blocks: [
      {
        id: "rbac",
        title: "1. RBAC Admin",
        paragraphs: [
          "Gestiona módulos, permisos y roles del sistema, en 2 pestañas: Módulos y Permisos (las entradas navegables del sistema, con sus acciones) y Roles del sistema (que agrupan permisos y se separan en dos tablas: Roles de Compañía y Roles de Organismo de Tránsito). El dock de cada usuario se filtra por los módulos accesibles de su rol; la API vuelve a validar cada permiso.",
        ],
        callouts: [
          {
            variant: "warning",
            text: "Quitar un permiso a un rol afecta de inmediato a todos sus usuarios. El Super Admin no pasa por RBAC: tiene bypass total.",
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
          "El módulo Usuarios para el Super Admin es global: eliges el destino de la invitación (compañía, organismo o perfil FLIT interno) y los roles que aplican a ese destino. Un perfil FLIT no pertenece a una compañía ni a un organismo y admite un solo rol.",
          "«Eliminar usuario» es un borrado lógico: el usuario sale del listado activo y pierde el acceso, pero queda visible en la pestaña Eliminados, donde «Restaurar» lo devuelve a activo con sus roles previos. Cada usuario tiene además su propio historial de auditoría: quién lo invitó, qué roles cambiaron y cuándo.",
        ],
      },
      {
        id: "auditoria",
        title: "3. Auditoría",
        paragraphs: [
          "Rastro global de operaciones administrativas y de seguridad: usuarios, roles, permisos y autenticación, además del módulo Trámites (creación, aceptación de términos, accesos de red). Filtra por módulo, actor, entidad y fecha. Es de solo lectura y no se puede alterar (append-only).",
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
];
