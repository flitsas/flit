import type { ManualArticle } from "../types";

export const INTRO_ARTICLES: ManualArticle[] = [
  {
    slug: "0-introduccion/1-bienvenida",
    title: "Centro de Ayuda FLIT",
    audience: "Todos",
    sectionId: "introduccion",
    keywords: ["bienvenida", "manual", "ayuda", "documentacion", "inicio", "centro", "flit"],
    summary: "Portal de documentación operativa para Gestor y Organismo de Tránsito.",
    blocks: [
      {
        id: "para-que",
        title: "1. ¿Para qué sirve este manual?",
        paragraphs: [
          "Bienvenido al ecosistema de FLIT. Este portal está diseñado para que resuelvas dudas sobre el uso de la plataforma en segundos, sin depender de una llamada a soporte en cada paso.",
          "Documenta las acciones del Gestor (empresa cliente que radica trámites vehiculares) y del Gestor de Organismo de Tránsito (OT). No incluye consolas exclusivas de SuperAdmin ni la administración de compañía (AdminCompany).",
        ],
      },
      {
        id: "navegar",
        title: "2. Pasos rápidos para navegar",
        paragraphs: [],
        bullets: [
          "Utiliza el menú lateral izquierdo para explorar Introducción, Gestor u Organismo de Tránsito.",
          "Si buscas algo específico (ej. «cómo crear un trámite» o «preasignación de placas»), usa la barra de Búsqueda en la parte superior (⌘ K / Ctrl K).",
          "Si el artículo es largo, usa la tabla de contenidos «En este artículo» a la derecha para saltar a la sección que necesitas.",
        ],
      },
      {
        id: "faq",
        title: "3. Lo que debes saber (FAQ)",
        paragraphs: [],
        bullets: [
          "¿Por qué veo manuales de cosas a las que no tengo acceso? El manual es público para fomentar transparencia. En la aplicación real, el sistema restringe el acceso según tu rol. Por eso, al inicio de cada artículo verás la etiqueta «Aplica para».",
          "¿Encontré un error en el manual? FLIT se actualiza constantemente. Si una pantalla se ve distinta a lo descrito, notifícalo a soporte@flitsas.com para que el equipo actualice el documento.",
          "¿Puede ayudarme DR. FLIT? Sí. En el chat elige «Necesito ayuda», describe tu duda y, si existe artículo, te ofrecemos el enlace directo a esta documentación.",
        ],
      },
    ],
  },
  {
    slug: "0-introduccion/2-como-navegar",
    title: "Cómo navegar el manual",
    audience: "Todos",
    sectionId: "introduccion",
    keywords: ["navegar", "buscar", "sidebar", "menu", "toc", "busqueda", "atalhos"],
    summary: "Menú lateral, búsqueda inteligente y tabla de contenidos.",
    blocks: [
      {
        id: "sidebar",
        title: "1. Menú lateral",
        paragraphs: [
          "Las secciones agrupan artículos por audiencia. Introducción aplica a todos; Gestor documenta al operador de compañía; Organismo de Tránsito documenta al perfil OT (rol ot_admin).",
          "Puedes colapsar cada sección haciendo clic en su título. El artículo activo se resalta con fondo turquesa suave.",
        ],
      },
      {
        id: "search",
        title: "2. Búsqueda",
        paragraphs: [
          "La barra superior indexa títulos, palabras clave y el cuerpo de los artículos. Escribe al menos dos caracteres para ver sugerencias.",
          "Atajo de teclado: ⌘ K (macOS) o Ctrl K (Windows). Esc cierra el panel de resultados.",
        ],
        callouts: [
          {
            variant: "tip",
            title: "Consejo",
            text: "Usa verbos concretos: «crear trámite», «documentos matrícula», «preasignación placas».",
          },
        ],
      },
      {
        id: "toc",
        title: "3. Tabla de contenidos",
        paragraphs: [
          "En pantallas anchas, la columna derecha lista las secciones del artículo actual. Cada enlace hace scroll suave al encabezado correspondiente.",
        ],
      },
    ],
  },
  {
    slug: "0-introduccion/3-perfiles-y-roles",
    title: "Perfiles: Gestor vs Organismo de Tránsito",
    audience: "Todos",
    sectionId: "introduccion",
    keywords: ["perfil", "gestor", "ot", "radicador", "ot_admin", "roles", "diferencia"],
    summary: "Qué perfil usa cada tipo de usuario y qué módulos ve en el dock.",
    blocks: [
      {
        id: "gestor",
        title: "1. Perfil Gestor (empresa cliente)",
        paragraphs: [
          "Corresponde a usuarios de una compañía que radica trámites vehiculares (concesionario, renting, gestoría). En el dock típico de un operador (rol Radicador) verás Inicio, Trámites y Ayuda.",
          "Módulos adicionales (Identidad, Reportes, Usuarios) solo aparecen si tu rol custom tiene los permisos RBAC correspondientes.",
        ],
        bullets: [
          "Ruta principal de trámites: /tramites",
          "Wizard server-driven: el backend define pasos, bloqueos y cuándo puedes radicar.",
          "No tiene acceso al hub del Organismo de Tránsito ni a consolas /admin de plataforma.",
        ],
      },
      {
        id: "ot",
        title: "2. Perfil Organismo de Tránsito",
        paragraphs: [
          "Usuarios con rol ot_admin administran la operación de su organismo: bandeja de trámites de compañías, preasignación, reportes, usuarios OT y parametrización (Reglas, Documentos, Requisitos).",
          "El menú del OT vive en el dock (no duplica módulos SPA homónimos). Las rutas del hub siguen el patrón /admin/transit-offices/{id}/…",
        ],
      },
      {
        id: "fuera",
        title: "3. Qué NO cubre este manual",
        paragraphs: [],
        bullets: [
          "SuperAdmin: compañías globales, documental plataforma, improntas, Quipux, RBAC, auditoría.",
          "AdminCompany: consola «Administración» de la compañía (matrícula inicial habilitada, mandatarios, RL, etc.).",
        ],
        callouts: [
          {
            variant: "info",
            text: "El menú es UX: ocultar un ítem no sustituye la validación de permisos en la API.",
          },
        ],
      },
    ],
  },
];
