import type { ManualArticle } from "../types";

export const GESTOR_ARTICLES: ManualArticle[] = [
  {
    slug: "1-gestor/1-inicio",
    title: "Inicio (Dashboard)",
    audience: "Gestor",
    sectionId: "gestor",
    keywords: ["dashboard", "inicio", "fab", "resumen", "gestor", "indicadores", "kpi"],
    summary: "Qué ves al entrar como Gestor, para qué sirve el Dashboard y cómo orientarte.",
    blocks: [
      {
        id: "que-es",
        title: "1. Qué es el Inicio",
        paragraphs: [
          "El botón central flotante (FAB) «Inicio FLIT» abre el Dashboard. Es tu punto de partida después de iniciar sesión.",
          "Muestra un resumen operativo de tu compañía: volumen de trámites, estados recientes u otros indicadores según lo habilitado para tu tenant. No aparece como píldora del dock inferior; siempre está disponible desde el FAB.",
        ],
      },
      {
        id: "cuando-usar",
        title: "2. Cuándo usarlo",
        paragraphs: [],
        bullets: [
          "Antes de abrir Trámites, para tener contexto del día o la semana.",
          "Para verificar que tu sesión y tenant son los correctos (nombre de compañía en la barra superior).",
          "Como atajo de regreso desde cualquier módulo: un clic en el FAB te devuelve al inicio.",
        ],
      },
      {
        id: "permisos",
        title: "3. Permisos y visibilidad",
        paragraphs: [
          "El módulo dashboard en RBAC requiere permiso dashboard.read. Si tu rol no lo tiene, el FAB sigue visible pero algunos widgets pueden estar vacíos o no cargar datos.",
        ],
        callouts: [
          {
            variant: "tip",
            text: "Si eres Radicador (operador típico), tu foco diario será Trámites; el Dashboard es complementario.",
          },
        ],
      },
    ],
  },
  {
    slug: "1-gestor/2-crear-tramite",
    title: "Cómo crear un trámite",
    audience: "Gestor",
    sectionId: "gestor",
    keywords: [
      "crear tramite",
      "nuevo tramite",
      "matricula",
      "traspaso",
      "wizard",
      "radicar",
      "gestor",
      "como creo un tramite",
      "iniciar tramite",
    ],
    summary: "Guía paso a paso para iniciar un trámite desde el módulo Trámites.",
    blocks: [
      {
        id: "antes",
        title: "1. Antes de empezar",
        paragraphs: [
          "Confirma que tu compañía tiene habilitado el tipo de trámite (políticas operativas del tenant). Si la matrícula inicial está apagada, no verás esa modalidad al crear.",
          "Necesitas permiso tramites.create además de tramites.read. Sin create solo puedes consultar trámites existentes.",
        ],
        bullets: [
          "Ten a mano documentos de identidad de las partes (vendedor, comprador, mandante, etc.).",
          "Para traspaso: placa del vehículo. Para matrícula: VIN.",
          "Verifica conexión estable: consultas RUNT/SIMIT pueden bloquear pasos si fallan.",
        ],
      },
      {
        id: "pasos",
        title: "2. Pasos para crear",
        paragraphs: [],
        bullets: [
          "Abre Trámites desde el dock inferior.",
          "Pulsa la acción de nuevo trámite y elige la modalidad (matrícula inicial, traspaso, etc.).",
          "El sistema crea una instancia en estado Borrador y te redirige al wizard.",
          "Completa cada paso que el servidor expone: actores, datos del vehículo, consultas externas, adjuntos, prevalidación biométrica si aplica.",
          "Revisa bloqueos en la barra del wizard (canSubmit / blockers). Solo cuando estén resueltos podrás firmar o radicar.",
        ],
      },
      {
        id: "wizard",
        title: "3. Cómo funciona el wizard",
        paragraphs: [
          "El wizard es server-driven: GET /instances/{id}/wizard devuelve pasos, campos y reglas. El frontend no inventa pasos; solo renderiza lo que el backend autoriza.",
          "Matrícula y traspaso difieren en actores, consultas y documentos. No asumas que un trámite anterior sirve como plantilla exacta.",
        ],
        callouts: [
          {
            variant: "warning",
            title: "Importante",
            text: "No cierres el navegador con datos sin guardar. Los campos se persisten por paso, pero un bloqueo de red puede dejar un paso incompleto.",
          },
        ],
      },
      {
        id: "despues",
        title: "4. Después de radicar",
        paragraphs: [
          "El trámite pasa por estados de negocio (borrador → en revisión → aprobado/rechazado, según flujo). Puedes hacer seguimiento desde el listado o con DR. FLIT.",
        ],
      },
    ],
  },
  {
    slug: "1-gestor/3-documentos-tramite",
    title: "Documentos que necesitas para un trámite",
    audience: "Gestor",
    sectionId: "gestor",
    keywords: [
      "documentos",
      "requisitos documentales",
      "adjuntos",
      "cedula",
      "fur",
      "mandato",
      "que documentos",
      "necesito para",
      "matricula documentos",
      "traspaso documentos",
    ],
    summary: "Cómo conocer los documentos exigidos, dónde cargarlos y buenas prácticas.",
    blocks: [
      {
        id: "donde",
        title: "1. Dónde se definen los requisitos",
        paragraphs: [
          "Los documentos provienen del catálogo global del tipo de trámite (procedure_document_requirements) y pueden tener ajustes por Organismo de Tránsito (overrides).",
          "En el wizard, la sección de adjuntos lista obligatorios y opcionales antes de permitir radicación. Lo que no aparece ahí no debería pedírtelo el sistema para esa instancia.",
        ],
      },
      {
        id: "comunes",
        title: "2. Documentos frecuentes por contexto",
        paragraphs: [],
        bullets: [
          "Identificación: cédula de ciudadanía o documento equivalente de propietario, comprador o apoderado.",
          "Matrícula inicial: factura o documento de origen, SOAT si aplica en el paso, mandato cuando actúa un tercero.",
          "Traspaso: contrato o soporte de compraventa, mandato de traspaso, documentos de prenda si hay gravamen.",
          "FUR: el sistema puede generarlo; revisa casillas según Resolución Mintransporte (Anexo 46).",
        ],
      },
      {
        id: "carga",
        title: "3. Cómo cargar adjuntos",
        paragraphs: [
          "Usa el paso Documentos del wizard. Los archivos se suben vía URLs prefirmadas (S3/file-manager). Formatos típicos: PDF, JPG, PNG.",
          "Nombre descriptivo ayuda al revisor OT. Evita fotos borrosas o PDFs protegidos con contraseña.",
        ],
        callouts: [
          {
            variant: "tip",
            text: "Si el OT añadió un requisito específico en su hub, lo verás reflejado en la instancia aunque no esté en este manual genérico.",
          },
        ],
      },
      {
        id: "errores",
        title: "4. Errores comunes",
        paragraphs: [],
        bullets: [
          "Documento ilegible → vuelve a escanear o fotografiar con buena luz.",
          "Persona equivocada en el mandato → verifica actores antes de adjuntar.",
          "Falta un obligatorio → el wizard muestra blocker hasta completarlo.",
        ],
      },
    ],
  },
  {
    slug: "1-gestor/4-prevalidacion",
    title: "Cómo enviar una prevalidación",
    audience: "Gestor",
    sectionId: "gestor",
    keywords: [
      "prevalidacion",
      "pre validacion",
      "identidad",
      "biometrica",
      "kyverum",
      "enviar prevalidacion",
      "validacion identidad",
    ],
    summary: "Flujo de validación de identidad biométrica antes o durante la radicación.",
    blocks: [
      {
        id: "que-es",
        title: "1. Qué es la prevalidación",
        paragraphs: [
          "Es la verificación de identidad de las partes del trámite (propietario, comprador, mandatario, etc.) mediante proveedor biométrico (Kyverum). Algunos tipos de trámite la exigen como puerta antes de radicar.",
        ],
      },
      {
        id: "flujo",
        title: "2. Flujo general",
        paragraphs: [],
        bullets: [
          "Desde el wizard, en el paso de identidad, se invita a cada parte (enlace o QR según configuración).",
          "La persona completa el flujo biométrico en el dispositivo indicado.",
          "El estado vuelve al trámite: pendiente, aprobado, rechazado o vencido.",
          "Con todas las identidades aprobadas, el wizard puede quitar el bloqueo correspondiente.",
        ],
      },
      {
        id: "estados",
        title: "3. Estados y qué hacer",
        paragraphs: [],
        bullets: [
          "Pendiente: reenvía el enlace o espera a que la persona complete el proceso.",
          "Aprobado: continúa con el wizard normalmente.",
          "Rechazado: revisa el motivo; puede requerir nuevo intento o documentación soporte.",
          "Vencido: genera una nueva invitación si el sistema lo permite.",
        ],
        callouts: [
          {
            variant: "info",
            text: "También puedes consultar validaciones desde el módulo Identidad (validaciones) si tu rol tiene permiso validaciones.read.",
          },
        ],
      },
    ],
  },
  {
    slug: "1-gestor/5-seguimiento",
    title: "Seguimiento y estados del trámite",
    audience: "Gestor",
    sectionId: "gestor",
    keywords: [
      "seguimiento",
      "estados",
      "borrador",
      "radicado",
      "historial",
      "bandeja",
      "rechazado",
      "aprobado",
    ],
    summary: "Cómo ubicar un trámite, interpretar estados y usar el historial.",
    blocks: [
      {
        id: "listado",
        title: "1. Listado de trámites",
        paragraphs: [
          "En /tramites ves las instancias de tu compañía. Puedes filtrar por estado, fecha o criterios disponibles en tu versión.",
          "Al abrir una fila entras al detalle: wizard (si sigue en borrador), timeline de estados, adjuntos y observaciones del OT si las hay.",
        ],
      },
      {
        id: "estados",
        title: "2. Estados de negocio",
        paragraphs: [],
        bullets: [
          "Borrador: aún editable por el gestor; no enviado al OT.",
          "En revisión / radicado: en manos del organismo o en cola de decisión.",
          "Aprobado / rechazado: decisión tomada; revisa causales si fue rechazado.",
          "Estados intermedios pueden variar según parametrización del tipo de trámite.",
        ],
      },
      {
        id: "drflit",
        title: "3. Búsqueda con DR. FLIT",
        paragraphs: [
          "Abre el chat flotante DR. FLIT y elige buscar por placa, VIN, ID de trámite o cliente. Los resultados enlazan directo al detalle cuando tienes permiso.",
        ],
      },
    ],
  },
  {
    slug: "1-gestor/6-ayuda-dr-flit",
    title: "Ayuda con DR. FLIT (Gestor)",
    audience: "Gestor",
    sectionId: "gestor",
    keywords: ["dr flit", "chat", "necesito ayuda", "asistente", "documentacion"],
    summary: "Búsquedas operativas y enlace al manual desde el asistente.",
    blocks: [
      {
        id: "abrir",
        title: "1. Cómo abrir DR. FLIT",
        paragraphs: [
          "El botón flotante del asistente está disponible en la shell principal de la aplicación (cuando estás autenticado). Pulsa para abrir el panel lateral.",
        ],
      },
      {
        id: "opciones",
        title: "2. Qué puedes hacer",
        paragraphs: [],
        bullets: [
          "Buscar por placa, VIN, ID de trámite o cliente (documento/nombre).",
          "Necesito ayuda: escribe tu duda en lenguaje natural; si hay artículo en este manual, verás chips para abrirlo.",
          "Buscar de otra forma: vuelve al menú de sugerencias sin cerrar el chat.",
        ],
      },
      {
        id: "ejemplos",
        title: "3. Ejemplos de preguntas útiles",
        paragraphs: [],
        bullets: [
          "«cómo creo un trámite de traspaso»",
          "«qué documentos necesito para matrícula»",
          "«cómo envío prevalidación»",
        ],
        callouts: [
          {
            variant: "tip",
            text: "DR. FLIT v1 enlaza documentación estática; no sustituye soporte para incidentes de producción o caídas de RUNT.",
          },
        ],
      },
    ],
  },
];
