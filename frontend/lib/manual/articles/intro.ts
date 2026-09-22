import type { ManualArticle } from "../types";

export const INTRO_ARTICLES: ManualArticle[] = [
  {
    slug: "0-introduccion/1-bienvenida",
    title: "Centro de Ayuda FLIT",
    audience: "Todos",
    sectionId: "introduccion",
    keywords: ["bienvenida", "manual", "ayuda", "documentacion", "inicio", "centro", "flit"],
    summary: "Portal de documentación operativa de FLIT para Gestor, Organismo de Tránsito, Administración de compañía y Super Admin.",
    blocks: [
      {
        id: "para-que",
        title: "1. ¿Para qué sirve este manual?",
        paragraphs: [
          "Bienvenido al ecosistema de FLIT. Este portal está diseñado para que resuelvas dudas sobre el uso de la plataforma en segundos, sin depender de una llamada a soporte en cada paso.",
          "Documenta las acciones del Gestor (empresa cliente que radica trámites vehiculares), del Organismo de Tránsito (OT), del Administrador de compañía (consola, red de clientes, marca) y del equipo FLIT (Super Admin: compañías, plataforma, integraciones, RBAC). Cada artículo indica «Aplica para» y DR. FLIT solo sugiere los de tu perfil.",
        ],
      },
      {
        id: "navegar",
        title: "2. Pasos rápidos para navegar",
        paragraphs: [],
        bullets: [
          "Utiliza el menú lateral izquierdo para explorar Introducción, Normativa (la resolución que respalda a FLIT), Gestor, Organismo de Tránsito, Administración de compañía y Super Admin.",
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
          "¿Puede ayudarme DR. FLIT? Sí. En el chat elige «Necesito ayuda»: si estás en un módulo con documentación te la sugiere de entrada; si describes tu duda, te ofrece los artículos que aplican a tu perfil. En Gestión busca trámites por placa, VIN, radicado o cliente.",
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
        id: "administracion",
        title: "3. Perfiles de administración",
        paragraphs: [
          "Además de Gestor y Organismo hay dos perfiles de administración con secciones propias en este manual, visibles según tu rol:",
        ],
        bullets: [
          "Admin de Compañía: consola «Administración» de la compañía, usuarios, revocatorias y, si la compañía es cabeza de red (concesión o marca blanca), la red de clientes y la marca.",
          "Super Admin (equipo FLIT): compañías, organismos, documental de plataforma, improntas, integraciones, RBAC, auditoría y procesos periódicos.",
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
  {
    slug: "0-introduccion/4-fechas-y-horas",
    title: "Fechas, horas y formatos",
    audience: "Todos",
    sectionId: "introduccion",
    keywords: [
      "fecha",
      "hora",
      "formato de fecha",
      "zona horaria",
      "hora de colombia",
      "dd/mm/yyyy",
      "vigencia",
      "exportar",
    ],
    summary: "Cómo se muestran fechas y horas en FLIT y por qué una vigencia no lleva hora.",
    blocks: [
      {
        id: "instantes",
        title: "1. Instantes: DD/MM/YYYY HH:mm en hora de Colombia",
        paragraphs: [
          "Todo lo que representa un momento (radicación, cambio de estado, asignación de placa, firma, línea de tiempo, correos) se muestra como DD/MM/YYYY HH:mm en hora de Colombia, sin importar la zona horaria del navegador. Sin segundos: no aportan a la lectura operativa.",
        ],
      },
      {
        id: "calendario",
        title: "2. Fechas de calendario: sin hora",
        paragraphs: [
          "Las vigencias (SOAT, RTM, identidad, escrituras, baúl de firmas) son fechas de calendario: se muestran como DD/MM/YYYY y no se convierten de zona horaria. Por eso una vigencia nunca aparece «un día antes».",
        ],
      },
      {
        id: "exportables",
        title: "3. Exportables y documentos",
        paragraphs: [
          "Los archivos Excel y los documentos generados usan el mismo formato estándar que la interfaz. Los certificados del consolidado conservan la hora; el sello de tiempo de la impronta conserva los segundos por exigencia normativa.",
        ],
      },
    ],
  },
];
