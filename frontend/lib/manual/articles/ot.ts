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
    ],
    summary: "Revisar, filtrar y decidir trámites radicados por compañías cliente.",
    blocks: [
      {
        id: "acceso",
        title: "1. Cómo entrar",
        paragraphs: [
          "Con rol ot_admin, abre Trámites en el dock. Te lleva al hub del organismo: /admin/transit-offices/{id}/client-procedures.",
          "El ID del organismo se resuelve al navegar (URL, sessionStorage o perfil OT). No viaja en el JWT como campo visible.",
        ],
      },
      {
        id: "bandeja",
        title: "2. Qué muestra la bandeja",
        paragraphs: [],
        bullets: [
          "Trámites enviados por compañías vinculadas a tu OT.",
          "Estado actual, tipo de trámite, placa/VIN cuando aplique, compañía radicadora.",
          "Filtros por estado, fecha o criterios habilitados en tu despliegue.",
        ],
      },
      {
        id: "decision",
        title: "3. Revisión y decisión",
        paragraphs: [
          "Abre un trámite para ver expediente consolidado, adjuntos del gestor, resultados de consultas y historial.",
          "La acción disponible depende de las reglas del OT y del estado del trámite (aprobar, rechazar con causal, devolver, etc.).",
        ],
        callouts: [
          {
            variant: "warning",
            title: "Trazabilidad",
            text: "Las decisiones quedan registradas en el historial del trámite. Usa causales de rechazo cuando el catálogo las exija.",
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
          "El gestor de compañía ve placas preasignadas en su consola solo si la compañía tiene preasignación activa. El OT es quien define la oferta.",
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
    keywords: ["dr flit ot", "necesito ayuda", "manual ot", "buscar placa ot"],
    summary: "Asistente para localizar trámites y abrir documentación OT.",
    blocks: [
      {
        id: "busqueda",
        title: "1. Búsquedas operativas",
        paragraphs: [
          "Como ot_admin puedes buscar trámites por placa o VIN desde DR. FLIT. Los resultados respetan el alcance de tu organismo.",
        ],
      },
      {
        id: "ayuda",
        title: "2. Necesito ayuda",
        paragraphs: [],
        bullets: [
          "Escribe consultas como «preasignación de placas» o «requisitos documentales».",
          "Abre el artículo sugerido en este manual.",
          "Si no hay match, usa el enlace al Centro de Ayuda completo.",
        ],
      },
    ],
  },
];
