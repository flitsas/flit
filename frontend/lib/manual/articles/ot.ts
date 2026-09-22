import type { ManualArticle } from "../types";

export const OT_ARTICLES: ManualArticle[] = [
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
    ],
    summary: "Revisar, filtrar y decidir trámites radicados por compañías cliente; asignar, corregir y liberar placas.",
    blocks: [
      {
        id: "acceso",
        title: "1. Cómo entrar y qué muestra",
        paragraphs: [
          "Con rol de administrador del organismo, «Trámites» en el dock abre la bandeja del hub. Solo ves trámites que las compañías ya te entregaron: Borrador, Preparado y Anulado no llegan aquí.",
          "La cabecera cuenta el universo completo por clase de trabajo: Preasignación (radicados sin placa), Asignados (con placa; el gestor gestiona SOAT e impuestos), Por decidir (entregados), Aprobados, Solicitudes de revocatoria, Rechazados (desde entregado o desde preasignación) y Revocados. Pulsar una tarjeta filtra la tabla. La bandeja abre en Por decidir: es tu trabajo pendiente.",
        ],
        bullets: [
          "Búsqueda libre «Buscar radicado (FT1-0000012), placa, VIN...»: el radicado casa exacto; placa, VIN, nombre y documento de las partes y razón social de la compañía, por texto parcial.",
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
          "Preasignación → «Asignar placa»: elige una placa del rango del organismo o registra una fuera de rango. El dígito de preferencia que indicó el gestor aparece como guía. Al asignar, el trámite pasa a Asignado y la compañía recibe correo.",
          "Asignado → «Corregir placa»: una única corrección dentro de la hora siguiente a la asignación. Vencida la hora, la acción se deshabilita.",
          "Asignado → «Liberar placa» (con motivo): el trámite vuelve a Preasignación y la placa queda disponible. Queda en el historial.",
          "El gestor, en Asignado, gestiona SOAT e impuestos y «Envía al OT»: el trámite entra a Por decidir.",
        ],
      },
      {
        id: "decision",
        title: "3. Revisión y decisión",
        paragraphs: [
          "Abre un trámite para ver el expediente: vehículo, propietario, actores, documentos del gestor («Ver documentos»), consolidado («Ver consolidado») y el historial. El panel «Pendientes antes de decidir» lista lo que falta (por ejemplo SOAT vigente cuando la regla aplica).",
        ],
        bullets: [
          "Aprobar: opcionalmente adjunta la Licencia de Tránsito (PDF). El sistema la analiza con OCR y te dice si parece una LT y si la placa/VIN leídos coinciden con el trámite; el análisis nunca bloquea. Si hay varios mandatarios posibles, eliges uno. La LT también se puede adjuntar después desde la fila aprobada («Adjuntar LT»).",
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
      },
    ],
  },
  {
    slug: "2-ot/2-preasignacion",
    title: "Preasignación de placas",
    audience: "Organismo de Tránsito",
    sectionId: "ot",
    keywords: [
      "preasignacion",
      "placas",
      "rangos",
      "plate-ranges",
      "asignar placa",
      "matricula placa",
      "inventario de placas",
      "rango de placas",
    ],
    summary: "Administrar rangos de placas y preasignación para compañías.",
    blocks: [
      {
        id: "concepto",
        title: "1. Qué es la preasignación",
        paragraphs: [
          "Permite al OT definir rangos o bloques de placas que las compañías pueden consumir en trámites de matrícula (u otros flujos habilitados).",
          "Reduce fricción operativa al evitar asignación manual caso a caso cuando hay convenio de rangos.",
        ],
      },
      {
        id: "donde",
        title: "2. Dónde configurarlo",
        paragraphs: [
          "Dock → Preasignación → segmento plate-ranges del hub OT.",
        ],
        bullets: [
          "Crea o edita rangos según política del organismo.",
          "Asocia rangos a compañías o convenios cuando aplique.",
          "Monitorea consumo para no agotar un rango sin reponer.",
        ],
      },
      {
        id: "gestor",
        title: "3. Relación con el Gestor",
        paragraphs: [
          "El gestor no elige placas del inventario: radica la matrícula inicial en ruta larga con un dígito de preferencia (o sin preferencia) y el trámite llega a tu cola de Preasignación. Tú asignas la placa desde la bandeja, del rango o fuera de rango; el rango define la oferta disponible y su consumo se refleja aquí.",
        ],
      },
    ],
  },
  {
    slug: "2-ot/3-reportes",
    title: "Reportes del Organismo",
    audience: "Organismo de Tránsito",
    sectionId: "ot",
    keywords: ["reportes ot", "indicadores", "estadisticas organismo", "exportar", "kpi ot"],
    summary: "Indicadores operativos del organismo desde el hub OT.",
    blocks: [
      {
        id: "acceso",
        title: "1. Acceso",
        paragraphs: [
          "Dock → Reportes → /admin/transit-offices/{id}/reportes. Distinto del módulo SPA Reportes (que el OT omite a propósito para no duplicar menú).",
        ],
      },
      {
        id: "contenido",
        title: "2. Qué puedes consultar",
        paragraphs: [],
        bullets: [
          "Volumen de trámites por periodo y estado.",
          "Desempeño por compañía radicadora (según permisos).",
          "Exportes PDF/Excel si están habilitados en tu instancia.",
        ],
      },
      {
        id: "uso",
        title: "3. Buenas prácticas",
        paragraphs: [
          "Usa los mismos filtros de fecha que usarías en auditorías internas. Cruza con la bandeja si un número no cuadra.",
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
    ],
    summary: "Invitar y administrar cuentas del perfil OT.",
    blocks: [
      {
        id: "alcance",
        title: "1. Alcance",
        paragraphs: [
          "Gestiona usuarios de tu Organismo de Tránsito: invitaciones, asignación de rol ot_admin u otros roles OT definidos, reset de acceso según política.",
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
        ],
      },
      {
        id: "seguridad",
        title: "3. Seguridad",
        paragraphs: [
          "Suspende cuentas de funcionarios que ya no operen el sistema. El SuperAdmin puede restaurar usuarios eliminados; el OT no ve pestaña Eliminados.",
        ],
      },
    ],
  },
  {
    slug: "2-ot/5-reglas",
    title: "Reglas del Organismo",
    audience: "Organismo de Tránsito",
    sectionId: "ot",
    keywords: ["reglas", "rules", "politicas ot", "configuracion ot", "conformacion"],
    summary: "Reglas operativas que gobiernan cómo el OT procesa trámites.",
    blocks: [
      {
        id: "que-son",
        title: "1. Qué son las Reglas",
        paragraphs: [
          "Parametrizan criterios de conformación, validaciones automáticas y comportamiento del hub frente a cada tipo de trámite.",
          "Impactan qué ve el revisor OT al abrir un expediente y qué acciones están permitidas.",
        ],
      },
      {
        id: "editar",
        title: "2. Cuándo editarlas",
        paragraphs: [],
        bullets: [
          "Cambio normativo o de procedimiento interno del OT.",
          "Nuevo convenio con compañías que exige criterio distinto.",
          "Corrección de falsos positivos en validaciones automáticas.",
        ],
        callouts: [
          {
            variant: "warning",
            text: "Cambios en Reglas pueden afectar trámites en curso. Coordina con operación antes de desplegar ajustes amplios.",
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
    ],
    summary: "Parametrización documental: tags, orden y precedencia en el OT.",
    blocks: [
      {
        id: "uso",
        title: "1. Para qué sirve",
        paragraphs: [
          "Define cómo el organismo espera ver clasificados los soportes en el expediente: etiquetas, orden en consolidado y precedencia respecto al catálogo global.",
        ],
      },
      {
        id: "acciones",
        title: "2. Acciones típicas",
        paragraphs: [],
        bullets: [
          "Configurar tags documentales por tenant/OT.",
          "Ajustar precedencia cuando un mismo tipo existe en catálogo global y override local.",
          "Alinear con Requisitos para que gestores vean lista coherente al radicar.",
        ],
      },
    ],
  },
  {
    slug: "2-ot/7-requisitos",
    title: "Requisitos documentales (OT)",
    audience: "Organismo de Tránsito",
    sectionId: "ot",
    keywords: [
      "requisitos",
      "requirements",
      "documentos requeridos ot",
      "overrides",
      "obligatorio",
      "opcional",
    ],
    summary: "Exigir, relajar u ordenar documentos por tipo de trámite en tu OT.",
    blocks: [
      {
        id: "concepto",
        title: "1. Catálogo vs override",
        paragraphs: [
          "FLIT tiene requisitos base por procedure_type (global, SuperAdmin). Tu OT puede aplicar overrides: marcar un documento obligatorio u opcional solo para tu organismo.",
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
          "Los gestores de compañía verán los requisitos resultantes en el wizard al crear instancias contra tu OT. Cambios retroactivos no suelen alterar instancias ya radicadas.",
        ],
      },
    ],
  },
  {
    slug: "2-ot/8-ayuda-dr-flit",
    title: "Ayuda con DR. FLIT (OT)",
    audience: "Organismo de Tránsito",
    sectionId: "ot",
    keywords: ["dr flit ot", "necesito ayuda", "manual ot", "buscar placa ot", "buscar radicado", "asistente"],
    summary: "Asistente para localizar trámites de tu bandeja y abrir la documentación del organismo.",
    blocks: [
      {
        id: "busqueda",
        title: "1. Búsquedas operativas",
        paragraphs: [
          "Como administrador del organismo puedes buscar en DR. FLIT por placa, VIN, trámite (radicado, con o sin prefijo) o cliente (nombre, documento o razón social). Los resultados salen de tu bandeja: solo trámites que las compañías ya te entregaron, con la compañía radicadora en cada tarjeta y una pista de qué sigue según el estado.",
        ],
      },
      {
        id: "ayuda",
        title: "2. Necesito ayuda",
        paragraphs: [],
        bullets: [
          "Escribe consultas como «asignar placa», «decidir revocatoria», «validar impronta» o «requisitos documentales».",
          "Si estás en una pestaña del hub con artículo propio, DR. FLIT te lo sugiere antes de que escribas.",
          "Solo verás artículos del Organismo y los transversales; la documentación del Gestor no aplica a tu perfil.",
          "Ayuda → Normativa abre la Resolución 20233040017145 de 2023: los requisitos que puede exigir tu organismo son los del Título 5 y no otros (art. 5.1.1).",
          "Si no hay coincidencia, usa el enlace al Centro de Ayuda completo o Ayuda → Soporte.",
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
          "Tarjeta «Solicitudes de revocatoria» en la cabecera de la bandeja: cuenta los aprobados con una solicitud esperando decisión y filtra la tabla al pulsarla.",
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
      "firma fisica",
      "empresas que radican",
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
          "Mandatario general del organismo: el firmante por defecto cuando la compañía no tiene uno propio. Puedes registrarlo y editarlo aquí (nombre, tipo y número de documento, tipo de firma).",
          "Mandatarios del organismo: todas las personas habilitadas para firmar mandatos ante tu OT, con su tipo de firma (estampada desde el baúl o física).",
          "Empresas que radican en este organismo: busca por razón social o NIT y revisa qué mandatario aplica por defecto a cada una.",
        ],
        callouts: [
          {
            variant: "info",
            text: "La redacción del contrato (plantilla, unión temporal, NIT, sigla) y el tipo de mandato por compañía los define el equipo FLIT en Plataforma → Mandatos; aquí ves y ajustas los firmantes.",
          },
        ],
      },
      {
        id: "aprobacion",
        title: "3. Al aprobar un trámite",
        paragraphs: [
          "Si hay varios mandatarios posibles y el sistema no puede decidir solo, al aprobar verás «Elegir mandatario del mandato». El mandatario debe tener validación de identidad vigente antes de firmar; si no la tiene, la compañía se la envía desde su pestaña «Mandatarios».",
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
      "verificar impronta",
    ],
    summary: "Consultar las improntas firmadas de una placa y verificar que una firma digital corresponde a la impronta.",
    blocks: [
      {
        id: "consultar",
        title: "1. Consultar improntas firmadas por placa",
        paragraphs: [
          "Administración → Validar impronta. Escribe la placa y pulsa Consultar: verás las improntas firmadas digitalmente para esa placa, con su hash de documento, la fecha de firma y si fue reemplazada después de firmarse (en ese caso la firma que ves es la histórica). Desde cada fila puedes abrir el PDF de la impronta.",
        ],
      },
      {
        id: "validar",
        title: "2. Validar una firma digital",
        paragraphs: [
          "Pega la firma digital (Base64) que te entregaron y pulsa «Validar firma digital». El resultado es inequívoco: «La firma corresponde a esta impronta» o «La firma no corresponde a esta impronta». Cada validación queda en la bitácora de la impronta, con resultado y fecha.",
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
    audience: "Organismo de Tránsito",
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
    ],
    summary: "Modo de operación (FLIT o Quipux), ventana de revocatoria en días hábiles y ajustes operativos.",
    blocks: [
      {
        id: "modo",
        title: "1. Modo de operación",
        paragraphs: [
          "Administración → Configuración. Un organismo puede operar su consola en FLIT (aprueba y rechaza aquí) o en solo lectura porque decide dentro de Quipux. El modo Quipux no afecta la radicación de las compañías: solo dónde se toma la decisión. El cambio aplica en caliente, sin reinicio.",
        ],
      },
      {
        id: "ventana",
        title: "2. Ventana de revocatoria",
        paragraphs: [
          "Días hábiles, contados desde la aprobación, durante los cuales una compañía puede solicitar la revocatoria de un trámite aprobado. Déjala vacía para no limitar. Vencida la ventana, el botón «Solicitar revocatoria» del gestor se deshabilita con ese motivo.",
        ],
      },
      {
        id: "flags",
        title: "3. Feature flags operativos",
        paragraphs: [
          "Interruptores de comportamiento del organismo (por ejemplo, exigencias en la decisión). Se activan y desactivan aquí y quedan registrados; consulta con el equipo FLIT antes de cambiar uno que no reconozcas.",
        ],
      },
    ],
  },
];
