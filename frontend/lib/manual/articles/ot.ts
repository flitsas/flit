import type { ManualArticle } from "../types";

export const OT_ARTICLES: ManualArticle[] = [
  {
    slug: "2-ot/13-inicio",
    title: "Inicio (Panel del Organismo)",
    audience: "Organismo de Tránsito",
    sectionId: "ot",
    keywords: [
      "inicio ot",
      "panel del organismo",
      "dashboard organismo",
      "cola de trabajo",
      "esperan mi decision",
      "pendientes en total",
      "entregados hoy",
      "tiempo mediano de decision",
      "antiguedad de lo pendiente",
      "por revisar",
      "esperando asignar placa",
      "en espera del cliente",
      "drill-down",
      "que tengo que hacer hoy",
    ],
    summary: "Qué muestra el panel al entrar como organismo: la cola de trabajo, sus indicadores y cómo priorizar el día.",
    blocks: [
      {
        id: "que-ves",
        title: "1. Qué ves al entrar",
        paragraphs: [
          "Con rol de organismo de tránsito, al iniciar sesión entras directo a «Tu cola de trabajo»: no es el panel del gestor, porque aquí no se mide producción de una empresa sino el trabajo pendiente del organismo.",
          "Arriba rota un carrusel de mensajes: el primero siempre es fijo y resume tu cola en palabras («Tienes N trámites esperando tu decisión, M llevan más de 7 días esperando» y tu mediana de decisión de hoy); detrás pueden aparecer los banners activos que publique el equipo FLIT. Los datos son del día calendario de Bogotá.",
        ],
        media: [
          {
            kind: "image",
            src: "/manual/screenshots/ot-inicio.png",
            alt: "Panel del organismo con la cola de trabajo, los cuatro indicadores, las colas por estado y la antigüedad de lo pendiente",
            caption: "El Inicio del organismo: tu cola de trabajo y su antigüedad, de un vistazo.",
          },
        ],
      },
      {
        id: "indicadores",
        title: "2. Los cuatro indicadores del panel",
        paragraphs: [
          "Junto al carrusel, cuatro tarjetas resumen tu situación de un vistazo. Con más de cero trámites detrás, la tarjeta es un enlace: clic y ves la lista exacta que la compone.",
        ],
        bullets: [
          "Esperan mi decisión: entregados que necesitan que tú apruebes o rechaces.",
          "Pendientes en total: incluye también lo que está esperando a un tercero (placa, SOAT, impuestos o al cliente).",
          "Entregados hoy: lo que te llegó en la jornada de hoy.",
          "Tiempo mediano de decisión: de los últimos 30 días. Si aún no has decidido nada en la ventana, dice «Sin decisiones aún» en vez de mostrar un número que no existe.",
        ],
      },
      {
        id: "colas",
        title: "3. En qué está esperando cada trámite",
        paragraphs: [
          "Debajo, «En qué está esperando cada trámite» reparte lo pendiente en tres colas. Solo una es tu turno; las otras dos dependen de alguien más:",
        ],
        bullets: [
          "Por revisar: espera tu decisión. Es la única de las tres donde el atraso es tuyo.",
          "Esperando asignar placa: matrícula inicial en ruta larga, radicada sin placa. Esperas a que le asignes una (o a que el gestor la pida).",
          "En espera del cliente: SOAT, impuestos o un trámite que la empresa pausó. No requiere que tú actúes; solo monitorea.",
        ],
        callouts: [
          {
            variant: "tip",
            text: "Cada cola con trámites es clic-able: abre el detalle exacto de esos trámites, con los mismos filtros con los que se calculó la tarjeta.",
          },
        ],
      },
      {
        id: "antiguedad",
        title: "4. Antigüedad de lo pendiente",
        paragraphs: [
          "«Antigüedad de lo pendiente» reparte el mismo total en cuatro tramos: 0–1 día, 2–3 días, 4–7 días y más de 7 días. El último se resalta cuando tiene algo: es la señal de que algo se está estancando.",
        ],
        bullets: [
          "Un tramo con trámites también abre su detalle al hacer clic.",
          "Un tramo en cero no es un enlace: no hay lista vacía que mostrar.",
        ],
      },
      {
        id: "actividad",
        title: "5. Actividad reciente",
        paragraphs: [
          "Debajo de la cola, dos tarjetas más miran los últimos 14 días: «Actividad» (radicados, aprobados y rechazados por día) y «En qué quedó lo recibido» (una dona con Aprobados, Rechazados —con o sin subsanación abierta—, Sin decisión —en revisión, esperando placa o esperando al cliente—, Revocados y Anulados). Es el pulso reciente, no la cola de ahora mismo.",
        ],
      },
      {
        id: "prioridad",
        title: "6. Cuándo mirarlo y cómo priorizar el día",
        paragraphs: [
          "Entra aquí antes de abrir la bandeja: en un vistazo sabes si hay algo urgente antes de perder tiempo filtrando manualmente.",
        ],
        bullets: [
          "Empieza por «Por revisar»: es la única cola que depende de ti.",
          "Dentro de «Por revisar», prioriza lo que ya aparece en «Más de 7 días» en Antigüedad: es lo que más se está envejeciendo.",
          "«Esperando asignar placa» y «En espera del cliente» no exigen acción inmediata, pero conviene revisarlas si llevan varios días sin moverse.",
        ],
        callouts: [
          {
            variant: "warning",
            title: "Si algo no carga",
            text: "Un fallo de carga se avisa con un mensaje y un botón «Reintentar»; el panel nunca muestra ceros para disimular un error, porque una cola con problemas no debe leerse como una cola sana.",
          },
        ],
      },
    ],
  },
  {
    slug: "2-ot/1-tramites-bandeja",
    title: "Bandeja de trámites (OT)",
    audience: "Organismo de Tránsito",
    sectionId: "ot",
    keywords: [
      "bandeja",
      "tramites ot",
      "client-procedures",
      "revisar tramite",
      "organismo",
      "aprobar",
      "rechazar",
      "asignar placa",
      "liberar placa",
      "corregir placa",
      "licencia de transito",
      "causales",
      "consolidado",
      "por decidir",
      "atajos",
      "busqueda rapida",
      "por aprobar",
      "por preasignar",
      "transformaciones",
    ],
    summary: "Revisar, filtrar y decidir trámites radicados por compañías cliente; asignar, corregir y liberar placas.",
    blocks: [
      {
        id: "acceso",
        title: "1. Cómo entrar y qué muestra",
        paragraphs: [
          "Con rol de administrador del organismo, «Trámites» en el dock abre la bandeja del hub. Solo ves trámites que las compañías ya te entregaron: Borrador, Preparado y Anulado no llegan aquí.",
          "La cabecera cuenta el universo completo por clase de trabajo: Preasignación (radicados sin placa), Asignado (con placa; el gestor gestiona SOAT e impuestos), Entregado (esperando tu decisión), Aprobado, Solicitud de revocatoria, Rechazado (desde entregado o desde preasignación) y Revocado. Las pestañas Todos, Matrículas, Traspaso y Otros trámites acotan la tabla y las tarjetas a esa familia; en Traspaso y Otros no hay Preasignación ni Asignado. Pulsar una tarjeta filtra la tabla. La bandeja abre en Entregado: es tu trabajo pendiente.",
        ],
        bullets: [
          "Búsqueda libre «Buscar radicado (FT1-0000012), placa, VIN...»: el radicado casa exacto; placa, VIN, nombre y documento de las partes y razón social de la compañía, por texto parcial.",
          "«Búsqueda rápida»: los atajos «Por aprobar» (entregados a la espera de tu decisión), «Por preasignar» (radicados sin placa) y «Revocatorias» (aprobados con una solicitud de revocatoria activa) resaltan y filtran de una vez la tarjeta correspondiente, sin tener que ubicarla en la cabecera.",
          "«Periodo» por fecha de radicación y «+ Filtro» con la gramática de Consultas (campo, operador, valores; admite pegar una columna de Excel).",
          "«Exportar» genera un Excel con todas las filas que cumplen los filtros, no solo la página.",
          "Columnas configurables; la elección se guarda por usuario.",
        ],
      },
      {
        id: "placa",
        title: "2. Cola de placa (matrícula inicial en ruta larga)",
        paragraphs: [],
        bullets: [
          "Preasignación → «Asignar placa»: escribe la placa libre que le vas a dar al trámite. El dígito de preferencia que indicó el gestor aparece como guía, sin obligarte a nada. Al asignar, el trámite pasa a Asignado y la compañía recibe correo.",
          "Asignado → «Corregir placa»: una única corrección dentro de la hora siguiente a la asignación. Vencida la hora, la acción se deshabilita.",
          "Asignado → «Liberar placa» (con motivo): el trámite vuelve a Preasignación y la placa queda disponible. Queda en el historial.",
          "El gestor, en Asignado, gestiona SOAT e impuestos y «Envía al OT»: el trámite entra a Entregado.",
        ],
      },
      {
        id: "decision",
        title: "3. Revisión y decisión",
        paragraphs: [
          "Abre un trámite para ver el expediente, organizado en acordeones independientes que puedes tener abiertos a la vez: vehículo, propietario y actores; transformaciones declaradas (si el vehículo cambió color, combustible o carrocería frente a lo que dice RUNT, aquí ves el antes y el después); documentos del gestor («Ver documentos»), consolidado («Ver consolidado») y el historial. El panel «Pendientes antes de decidir» lista lo que falta (por ejemplo SOAT vigente cuando la regla aplica).",
          "El consolidado se mantiene al día solo: si el gestor o el organismo cambian un dato, un documento o la placa, el FUR y el consolidado se vuelven a generar en segundos. No hace falta pedir que lo limpien para ver el cambio.",
        ],
        bullets: [
          "Aprobar: opcionalmente adjunta la Licencia de Tránsito (PDF). El sistema la analiza con OCR y te dice si parece una LT y si la placa/VIN leídos coinciden con el trámite; el análisis nunca bloquea. Si hay varios mandatarios válidos, eliges uno de los que te muestra el sistema. La LT también se puede adjuntar después desde la fila aprobada («Adjuntar LT»).",
          "Rechazar: marca las causales del catálogo que apliquen (dependen de la familia: matrícula o traspaso) y describe qué debe corregirse. El gestor podrá subsanar y reenviar sin volver a borrador.",
          "Decidir revocatoria: solo en aprobados con solicitud activa (ver «Decidir solicitudes de revocatoria»).",
        ],
        callouts: [
          {
            variant: "warning",
            title: "Trazabilidad",
            text: "Toda decisión queda en el historial del trámite con fecha y hora, y dispara el correo correspondiente a la compañía. Si tu organismo opera en Quipux, la consola está en solo lectura: decides allí, no aquí.",
          },
        ],
        media: [
          {
            kind: "diagram",
            id: "flujo-decision-ot",
            alt: "Diagrama del flujo de decisión del organismo: revisar el expediente, asignar placa si aplica, y aprobar o rechazar con causal, con retorno a subsanación si se rechaza",
            caption: "La decisión de punta a punta: revisar, placa si aplica, aprobar o rechazar con causal.",
          },
        ],
      },
    ],
  },
  {
    slug: "2-ot/3-reportes",
    title: "Reportes del Organismo",
    audience: "Organismo de Tránsito",
    sectionId: "ot",
    keywords: [
      "reportes ot",
      "indicadores",
      "estadisticas organismo",
      "exportar",
      "kpi ot",
      "ahora mismo",
      "analisis",
      "informe",
      "revisores",
      "consultas personalizadas",
      "programacion y alertas",
    ],
    summary: "Cinco pestañas para mirar tu operación como organismo: la cola de ahora, por qué rechazas, qué recibiste, qué hizo cada revisor y tu propia consulta.",
    blocks: [
      {
        id: "acceso",
        title: "1. Acceso",
        paragraphs: [
          "Dock → Reportes abre la consola del hub, en /admin/transit-offices/{id}/reportes. Es distinta del módulo Reportes que ve un gestor: aquí el organismo mira hacia las empresas que le radican, no al revés.",
        ],
      },
      {
        id: "pestanas",
        title: "2. Las cinco pestañas",
        paragraphs: [
          "Cada pestaña trae solo los filtros que necesita: la de «Ahora mismo» no tiene selector de fechas porque describe este momento, no un rango.",
        ],
        bullets: [
          "Ahora mismo: qué tienes en la cola en este momento, sin fechas.",
          "Análisis: por qué rechazas y qué calidad de radicación te llega, con rango de fechas.",
          "Informe: qué recibiste en un periodo y en qué acabó, trámite a trámite.",
          "Revisores: qué hizo cada persona del organismo en un periodo (aprobaciones, rechazos, tiempos).",
          "Consultas personalizadas: arma tu propia búsqueda con la gramática de campo/operador/valor, guárdala y expórtala.",
        ],
      },
      {
        id: "programacion",
        title: "3. Programación y alertas",
        paragraphs: [
          "El botón «Programación y alertas», junto a las pestañas, abre un panel con tres partes: Programaciones (envíos periódicos por correo), Alertas (avisos cuando una cifra cruza un umbral) e Historial de alertas. Puedes programar directamente una consulta guardada desde «Consultas personalizadas».",
        ],
      },
      {
        id: "uso",
        title: "4. Buenas prácticas",
        paragraphs: [
          "Usa los mismos filtros de fecha que usarías en auditorías internas. Si un número no cuadra, cruza con la bandeja: los reportes se calculan con los mismos estados que ves ahí.",
        ],
      },
    ],
  },
  {
    slug: "2-ot/4-usuarios",
    title: "Usuarios del Organismo",
    audience: "Organismo de Tránsito",
    sectionId: "ot",
    keywords: [
      "usuarios ot",
      "invitar",
      "rol ot_admin",
      "gestionar usuarios organismo",
      "suspender",
      "acceso denegado",
      "cancelar invitacion",
    ],
    summary: "Invitar y administrar cuentas del perfil OT. Solo el administrador del organismo entra aquí.",
    blocks: [
      {
        id: "alcance",
        title: "1. Alcance",
        paragraphs: [
          "Usuarios, en el dock del hub, es exclusivo del administrador del organismo (rol ot_admin): invita, edita y cancela invitaciones de las cuentas de tu Organismo de Tránsito. Un operador que intente abrir esta pantalla recibe acceso denegado.",
          "No administra usuarios de compañías cliente; eso corresponde al AdminCompany de cada tenant.",
        ],
      },
      {
        id: "invitar",
        title: "2. Invitar un usuario",
        paragraphs: [],
        bullets: [
          "Dock → Usuarios (hub OT).",
          "Completa correo y rol. El invitado recibe enlace para activar cuenta.",
          "Verifica que el correo sea institucional del OT cuando la política lo exija.",
          "Una invitación pendiente se puede cancelar o reenviar desde la misma fila.",
        ],
      },
      {
        id: "seguridad",
        title: "3. Seguridad",
        paragraphs: [
          "Suspender temporalmente, desactivar indefinidamente, eliminar o restaurar una cuenta ya no lo hace el organismo: son acciones exclusivas del equipo FLIT (Super Admin). Tú, como administrador del organismo, invitas, editas datos y cancelas invitaciones pendientes; para lo demás, escala el caso a FLIT.",
        ],
      },
    ],
  },
  {
    slug: "2-ot/5-reglas",
    title: "Reglas del Organismo",
    audience: "Super Admin",
    sectionId: "ot",
    keywords: ["reglas", "rules", "politicas ot", "configuracion ot", "conformacion", "super admin", "exclusivo flit"],
    summary: "Reglas operativas que gobiernan cómo un organismo procesa trámites. Hoy es una pantalla exclusiva del equipo FLIT.",
    blocks: [
      {
        id: "que-son",
        title: "1. Qué son las Reglas",
        paragraphs: [
          "Esta configuración la administra el equipo FLIT (Super Admin); el organismo de tránsito no la edita, solo ve su efecto en su bandeja del hub. Si un usuario del organismo entra por URL directa a esta pantalla, recibe acceso denegado.",
          "Reglas parametriza criterios de conformación, validaciones automáticas y comportamiento del hub frente a cada tipo de trámite, para el organismo cuyo hub estás administrando.",
          "Impacta qué ve el revisor del organismo al abrir un expediente y qué acciones tiene permitidas: un ajuste aquí se nota allá, sin que el organismo toque nada.",
        ],
      },
      {
        id: "editar",
        title: "2. Cuándo editarlas",
        paragraphs: [],
        bullets: [
          "Cambio normativo o de procedimiento interno del organismo.",
          "Nuevo convenio con compañías que exige criterio distinto.",
          "Corrección de falsos positivos en validaciones automáticas.",
        ],
        callouts: [
          {
            variant: "warning",
            text: "Cambios en Reglas pueden afectar trámites en curso del organismo. Coordina con el organismo antes de desplegar ajustes amplios.",
          },
        ],
      },
    ],
  },
  {
    slug: "2-ot/6-documentos",
    title: "Documentos del Organismo",
    audience: "Organismo de Tránsito",
    sectionId: "ot",
    keywords: [
      "documentos ot",
      "parametrizacion documental",
      "tags documentos",
      "orden documentos",
      "precedencia",
      "prelacion",
      "etiquetas",
      "acceso denegado",
    ],
    summary: "Ordena los documentos del expediente. Exclusivo del administrador del organismo; un operador ve acceso denegado.",
    blocks: [
      {
        id: "uso",
        title: "1. Para qué sirve y quién entra",
        paragraphs: [
          "Documentos, en el dock del hub, es exclusivo del administrador del organismo (rol ot_admin): un operador que intente abrirlo recibe acceso denegado.",
          "Define el orden en que ves los soportes dentro del expediente de cada trámite: prelación, es decir, en qué página del consolidado va cada tipo de documento.",
        ],
      },
      {
        id: "prelacion",
        title: "2. Pestaña Prelación",
        paragraphs: [
          "Elige el tipo de trámite y arrastra un documento —o usa las flechas y Enter— hasta la página que quieres que ocupe. El orden nuevo aplica a partir de la próxima generación del expediente; no reordena expedientes ya generados.",
        ],
        callouts: [
          {
            variant: "info",
            text: "Etiquetas y el interruptor del documento de prenda por override ya no aparecen para el administrador del organismo: quedaron exclusivos del equipo FLIT (Super Admin). Tú, como organismo, solo ves y ajustas la Prelación.",
          },
        ],
      },
    ],
  },
  {
    slug: "2-ot/7-requisitos",
    title: "Requisitos documentales (OT)",
    audience: "Super Admin",
    sectionId: "ot",
    keywords: [
      "requisitos",
      "requirements",
      "documentos requeridos ot",
      "overrides",
      "obligatorio",
      "opcional",
      "super admin",
      "exclusivo flit",
    ],
    summary: "Exigir, relajar u ordenar documentos por tipo de trámite para un organismo. Hoy es una pantalla exclusiva del equipo FLIT.",
    blocks: [
      {
        id: "concepto",
        title: "1. Catálogo vs override",
        paragraphs: [
          "Esta configuración la administra el equipo FLIT (Super Admin); el organismo de tránsito no la edita, solo ve su efecto en su bandeja del hub. Un usuario del organismo que entre por URL directa recibe acceso denegado.",
          "FLIT tiene requisitos base por tipo de trámite (catálogo global). Desde aquí aplicas overrides para el organismo cuyo hub administras: marcas un documento obligatorio u opcional solo para ese organismo.",
        ],
      },
      {
        id: "flujo",
        title: "2. Flujo de trabajo",
        paragraphs: [],
        bullets: [
          "Abre Requisitos en el hub OT.",
          "Selecciona tipo de trámite y documento del catálogo.",
          "Define obligatoriedad, orden o excepciones según la UI disponible.",
          "Valida con un trámite de prueba en DEV/QA antes de producción.",
        ],
      },
      {
        id: "impacto",
        title: "3. Impacto en el Gestor",
        paragraphs: [
          "Los gestores de las compañías cliente verán los requisitos resultantes en el wizard al crear instancias contra ese organismo. Cambios retroactivos no suelen alterar instancias ya radicadas.",
        ],
      },
    ],
  },
  {
    slug: "2-ot/8-ayuda-dr-flit",
    title: "Ayuda con DR. FLIT (OT)",
    audience: "Organismo de Tránsito",
    sectionId: "ot",
    keywords: [
      "dr flit ot",
      "necesito ayuda",
      "manual ot",
      "buscar placa ot",
      "buscar radicado",
      "asistente",
      "inteligencia artificial",
      "caso de soporte",
      "autorizacion de datos",
    ],
    summary:
      "Escríbele con tus palabras: responde dudas del hub citando el manual, localiza trámites de tu bandeja y radica casos de soporte.",
    blocks: [
      {
        id: "escribir",
        title: "1. Escríbele con tus palabras",
        paragraphs: [
          "Abre el asistente con el botón flotante y escribe directo: «¿cómo asigno una placa?», «¿cómo decido una revocatoria?». DR. FLIT responde con base en el manual del Organismo y cita al final los artículos de donde salió la respuesta, con enlace para abrirlos. Si tu pregunta no es clara, te pregunta de vuelta; si no tiene la respuesta, te lo dice y te ofrece escalar a soporte.",
          "La primera vez te pedirá autorizar el tratamiento de datos (Ley 1581 de 2012): «Acepto y continuar» para usar la inteligencia artificial, o «Ahora no» para seguir con el menú clásico sin ella. Solo se pide una vez por versión del aviso.",
        ],
        bullets: [
          "Solo verás artículos del Organismo y los transversales; la documentación del Gestor no aplica a tu perfil.",
          "El chat con inteligencia artificial tiene un tope diario de mensajes; al alcanzarlo el menú sigue funcionando y al día siguiente se reactiva solo.",
          "Si el asistente no está disponible, responde el buscador del manual con una «respuesta rápida» y los enlaces.",
        ],
      },
      {
        id: "busqueda",
        title: "2. Búsquedas operativas",
        paragraphs: [
          "Pídelo en el chat («busca la placa ABC123», «muéstrame el radicado 12») o usa los chips de Gestión: Placa, VIN, Trámite o Cliente (nombre, documento o razón social). Los resultados salen de tu bandeja: solo trámites que las compañías ya te entregaron, con la compañía radicadora en cada tarjeta y una pista de qué sigue según el estado.",
        ],
      },
      {
        id: "normativa-soporte",
        title: "3. Normativa y soporte",
        paragraphs: [],
        bullets: [
          "Ayuda → Normativa abre la Resolución 20233040017145 de 2023: los requisitos que puede exigir tu organismo son los del Título 5 y no otros (art. 5.1.1).",
          "Para reportar un problema, cuéntaselo al chat o entra por Ayuda → Soporte: el formulario llega con tu correo, organismo y fecha precargados; completa el detalle, la frecuencia y la prioridad, adjunta evidencias si quieres, revisa el resumen y pulsa «Confirmar y radicar caso». Recibes tu número de caso al instante.",
          "Si el sistema de soporte no responde en ese momento, verás los canales de contacto y podrás reintentar sin perder lo escrito.",
        ],
      },
    ],
  },
  {
    slug: "2-ot/9-revocatorias",
    title: "Decidir solicitudes de revocatoria",
    audience: "Organismo de Tránsito",
    sectionId: "ot",
    keywords: [
      "revocatoria",
      "revocatorias",
      "decidir revocatoria",
      "aprobar revocatoria",
      "rechazar revocatoria",
      "revocado",
      "solicitud de revocatoria",
    ],
    summary: "Cómo ver, revisar y decidir las solicitudes de revocatoria que envían las compañías sobre trámites aprobados.",
    blocks: [
      {
        id: "que-es",
        title: "1. Qué es",
        paragraphs: [
          "Una compañía puede pedir que revoques un trámite que ya aprobaste, dentro de la ventana que tú configuras. El trámite sigue Aprobado mientras decides; solo cambia si apruebas la solicitud. El organismo ya no revoca por su cuenta: la única vía es aprobar una solicitud del gestor.",
        ],
      },
      {
        id: "donde",
        title: "2. Dónde verlas",
        paragraphs: [],
        bullets: [
          "Tarjeta «Solicitud de revocatoria» en la cabecera de la bandeja: cuenta los aprobados con una solicitud esperando decisión y filtra la tabla al pulsarla. El atajo «Revocatorias» de la búsqueda rápida hace lo mismo, sin buscar la tarjeta en la cabecera.",
          "Distintivo «Revocatoria solicitada» / «Revocatoria en revisión» en la fila.",
          "Vista «Revocatorias» del hub: listado de todas las solicitudes (activas y cerradas) con filtros por fecha y estado.",
        ],
      },
      {
        id: "decidir",
        title: "3. Decidir",
        paragraphs: [
          "En la fila, «Decidir revocatoria» abre el cuadro con el motivo del gestor y su documento de soporte (PDF). Elige:",
        ],
        bullets: [
          "Aprobar revocatoria (motivo opcional): el trámite pasa a Revocado, se liberan placa y VIN y la documentación queda histórica.",
          "Rechazar revocatoria (motivo obligatorio): el trámite permanece Aprobado y la solicitud queda cerrada como rechazada.",
        ],
        media: [
          {
            kind: "diagram",
            id: "flujo-revocatoria",
            alt: "Diagrama del flujo de una revocatoria: la compañía la solicita con motivo, el organismo decide, y el trámite queda Revocado con placa liberada o sigue Aprobado si se rechaza",
            caption: "El mismo flujo que ve la compañía: tu decisión es el paso final.",
          },
        ],
        callouts: [
          {
            variant: "warning",
            text: "La decisión es final y queda en el historial del trámite; la compañía recibe correo con el resultado. La ventana de revocatoria se ajusta en Administración → Configuración.",
          },
        ],
      },
    ],
  },
  {
    slug: "2-ot/10-mandatos",
    title: "Mandatos y mandatarios del organismo",
    audience: "Organismo de Tránsito",
    sectionId: "ot",
    keywords: [
      "mandatos",
      "mandatario",
      "mandatarios",
      "contrato de mandato",
      "mandatario general",
      "tipo de firma",
      "vigencia del mandatario",
      "por vencer",
      "empresas que radican",
      "compañías activas",
      "ver firma",
      "vista previa de la firma",
      "registrar mandatario",
    ],
    summary: "Quién firma el contrato de mandato ante tu organismo y cómo se configura desde el hub.",
    blocks: [
      {
        id: "concepto",
        title: "1. Mandante y mandatario",
        paragraphs: [
          "En cada trámite el mandante (vendedor o radicador) otorga poder y el mandatario lo recibe para gestionar ante el organismo por cuenta de la compañía gestora. El mandante sale de los actores del trámite; el mandatario sí se configura.",
        ],
      },
      {
        id: "hub",
        title: "2. Pestaña Mandatos del hub",
        paragraphs: ["Administración → Mandatos muestra tres bloques:"],
        bullets: [
          "Mandatario general del organismo: el firmante por defecto cuando la compañía no tiene uno propio. Puedes registrarlo y editarlo aquí (nombre, tipo y número de documento, forma de firma); registrar personas nuevas es solo del Admin OT.",
          "Mandatarios del organismo: todas las personas habilitadas para firmar mandatos ante tu OT, con su modelo (Persona natural, Persona jurídica o Formato en blanco), su tipo de firma (baúl de firmas o validación de identidad) y una etiqueta de vigencia: verde «Vigente», naranja «Por vencer» (faltan 7 días o menos), rojo «Vencido», rojo «Inactivo» y azul «Aún no vigente» cuando el rango todavía no empieza. La etiqueta siempre lleva el estado escrito, no depende del color. Persona jurídica y Formato en blanco no tienen vigencia: muestran un guion. Con el lápiz editas un mandatario (Admin OT); el Operador OT no ve esa acción. Con los iconos de desactivar (o reactivar) y eliminar das de baja a un mandatario: antes se te muestra qué compañías y defaults quedarían sin mandatario y cuántos trámites radicados sin aprobar se reasignan con la prelación; si no queda nadie, decides tú al aprobar. Eliminar conserva el historial, y reactivar no desplaza al mandatario vigente. El botón «Ver firma» (solo en Persona natural) abre una vista previa de la firma registrada, para comprobar cuál quedará estampada antes de usarla en un mandato. La firma física ya no existe. Un mandatario eliminado no aparece en la lista.",
          "Compañías: aquí ves todas las compañías activas, también las que aún no te han radicado ningún trámite. Busca por nombre o NIT (mínimo 2 caracteres), cambia las «Filas por página» y revisa qué mandatario aplica por defecto a cada una; «Sin definir» significa que aún no tiene. «Sin mandatario» (en naranja) indica que la compañía no tiene ningún mandatario activo en el organismo (ni propio ni general): regístrale uno para que sus trámites puedan radicarse. Solo se muestran nombre y NIT. La bandeja de trámites y las métricas no cambian: siguen mostrando solo las compañías que te radican.",
        ],
        callouts: [
          {
            variant: "info",
            text: "La redacción del contrato (plantilla, unión temporal, NIT, sigla) y el tipo de mandato por compañía los define el equipo FLIT en Plataforma → Mandatos; aquí ves y ajustas los firmantes.",
          },
        ],
      },
      {
        id: "registrar",
        title: "3. Registrar un mandatario",
        paragraphs: [
          "Solo el administrador del organismo (Admin OT) puede registrar mandatarios; el Operador OT no ve el botón.",
        ],
        bullets: [
          "Desde «Compañías», o desde el panel del mandatario general, pulsa «Registrar mandatario». Si eliges el general y hay varias empresas, indica cuál empresa registra a la persona.",
          "Elige el modelo: Persona natural (nombre completo, tipo y número de documento, correo opcional, forma de firma y vigencia), Persona jurídica (nombre y NIT de la entidad) o Formato en blanco (sin datos: el sistema solo entrega el PDF sin firma). El organismo queda fijo: es el tuyo.",
          "En Persona natural elige la forma de firma: Baúl de firmas (se usa la firma vigente que la persona tenga en el baúl de la empresa) o Validación de identidad. Luego la vigencia: Fija, o Rango de fechas con inicio y fin (el fin no puede ser anterior al inicio).",
          "Compañías asociadas (opcional): busca por nombre o NIT (mínimo 2 caracteres), marca las compañías de FLIT a las que también aplica el mandatario y revisa las marcadas debajo de la lista; puedes marcar varias y quitarlas con la «x». Solo se muestran nombre y NIT. Sin marcar ninguna, el mandatario aplica solo a la compañía elegida. Si una compañía no se puede asociar (por ejemplo, está inactiva), el formulario te dice el motivo junto a ella y conservas lo escrito.",
          "Pulsa «Guardar». Verás el aviso «Mandatario registrado» y la persona queda lista para elegirla como general o como default de la empresa.",
        ],
        callouts: [
          {
            variant: "info",
            text: "Desde el organismo no se ve el baúl de firmas de la empresa, por eso no hay selector de firma. Si eliges Baúl de firmas y la persona no tiene firma vigente allí, o si eliges Validación de identidad, el sistema te lo indica al guardar; el mensaje aparece junto al campo y conservas lo escrito.",
          },
          {
            variant: "info",
            text: "Si la empresa ya tiene un mandatario en tu organismo, el formulario te lo avisa y no guarda un segundo. Si te aparece un mensaje de falta de permiso, tu rol no puede registrar mandatarios: pídeselo al Admin OT.",
          },
        ],
      },
      {
        id: "aprobacion",
        title: "4. Al aprobar un trámite",
        paragraphs: [
          "Si hay varios mandatarios posibles y el sistema no puede decidir solo, al aprobar verás «Elegir mandatario del mandato». La lista muestra únicamente a los mandatarios válidos para ese trámite, con su nombre y su forma de firma (Baúl de firmas o Validación de identidad): activos, dentro de su vigencia y con la firma o la validación de identidad al día. Quienes no cumplen no aparecen. Elige uno y pulsa «Aprobar con este mandatario».",
          "Si no hay ningún mandatario válido, el diálogo te lo explica («No hay mandatarios válidos para aprobar este trámite») y no te deja aprobar. Registra uno en «Mandatos y mandatarios» del organismo (lo hace el Admin OT) o renueva la firma o la validación de identidad del existente, y vuelve a aprobar.",
          "Si el mandatario elegido necesita una validación de identidad vigente o su firma en el baúl para firmar, el aviso te lo indica; la validación se gestiona desde el módulo Identidad.",
        ],
      },
    ],
  },
  {
    slug: "2-ot/11-validar-impronta",
    title: "Validar impronta",
    audience: "Organismo de Tránsito",
    sectionId: "ot",
    keywords: [
      "impronta",
      "validar impronta",
      "firma digital",
      "improntas firmadas",
      "hash",
      "bitacora",
      "historial",
      "verificar impronta",
    ],
    summary: "Consultar las improntas firmadas de una placa y verificar que una firma digital corresponde a la impronta.",
    blocks: [
      {
        id: "consultar",
        title: "1. Consultar improntas firmadas por placa",
        paragraphs: [
          "Administración → Validar impronta. Escribe la placa y pulsa Consultar: verás las improntas firmadas digitalmente para esa placa, con su hash de documento, la fecha de firma y si fue reemplazada después de firmarse (en ese caso la firma que ves es la histórica). Desde cada fila puedes abrir el PDF de la impronta con «Ver PDF» o abrir «Historial» para ver la bitácora completa de validaciones hechas sobre esa impronta.",
        ],
      },
      {
        id: "validar",
        title: "2. Validar una firma digital",
        paragraphs: [
          "Con «Validar firma» pega la firma digital (Base64) que te entregaron y pulsa «Validar firma digital». El resultado es inequívoco: «La firma corresponde a esta impronta» o «La firma no corresponde a esta impronta». Cada validación queda en la bitácora de la impronta, con resultado y fecha, visible después desde «Historial».",
        ],
        callouts: [
          {
            variant: "tip",
            text: "El sello de tiempo de la impronta conserva los segundos por exigencia normativa; es la única fecha de la plataforma que los muestra.",
          },
        ],
      },
    ],
  },
  {
    slug: "2-ot/12-configuracion",
    title: "Configuración del organismo",
    audience: "Super Admin",
    sectionId: "ot",
    keywords: [
      "configuracion",
      "configuracion ot",
      "ventana de revocatoria",
      "dias habiles",
      "modo de operacion",
      "quipux",
      "solo lectura",
      "feature flags",
      "super admin",
      "exclusivo flit",
      "llaves operativas",
    ],
    summary: "Modo de operación (FLIT o Quipux), ventana de revocatoria en días hábiles y llaves operativas de un organismo. Hoy es exclusiva del equipo FLIT.",
    blocks: [
      {
        id: "modo",
        title: "1. Modo de operación",
        paragraphs: [
          "Esta configuración la administra el equipo FLIT (Super Admin); el organismo de tránsito no la edita, solo ve su efecto en su bandeja del hub. Un usuario del organismo que entre por URL directa a Configuración recibe acceso denegado.",
          "Administración → Configuración, dentro del hub del organismo que administras. Un organismo puede operar su consola en FLIT (aprueba y rechaza en la bandeja) o en solo lectura porque decide dentro de Quipux: en modo Quipux, el organismo ve su bandeja pero no puede aprobar ni rechazar desde ahí. El modo Quipux no afecta la radicación de las compañías: solo dónde se toma la decisión. El cambio aplica en caliente, sin reinicio.",
        ],
      },
      {
        id: "ventana",
        title: "2. Ventana de revocatoria",
        paragraphs: [
          "Días hábiles, contados desde la aprobación, durante los cuales una compañía puede solicitar la revocatoria de un trámite aprobado ante ese organismo. Déjala vacía para no limitar. Vencida la ventana, el botón «Solicitar revocatoria» del gestor se deshabilita con ese motivo.",
        ],
      },
      {
        id: "flags",
        title: "3. Llaves operativas del organismo",
        paragraphs: [
          "Interruptores de comportamiento propios del organismo (por ejemplo, exigencias en la decisión). Se activan y desactivan aquí y quedan registrados; documenta el motivo antes de cambiar una llave que no reconozcas, porque el organismo la siente en su bandeja sin saber que cambió.",
        ],
      },
    ],
  },
];
