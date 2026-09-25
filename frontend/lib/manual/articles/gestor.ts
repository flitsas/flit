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
      "terminos y condiciones",
      "carga masiva",
      "excel",
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
          "Pulsa «Nuevo trámite». Antes de continuar debes aceptar los Términos y Condiciones (casilla con enlace al documento vigente); la aceptación queda registrada con el trámite.",
          "El sistema crea una instancia en estado Borrador y te lleva al asistente; el tipo de trámite se elige dentro del paso 1.",
          "Completa cada paso que el servidor expone: actores, datos del vehículo, consultas externas (RUNT, SIMIT, RUES…), adjuntos, validación de identidad si aplica.",
          "Revisa los bloqueos en la barra del asistente. Solo cuando estén resueltos podrás firmar o radicar.",
        ],
        callouts: [
          {
            variant: "info",
            title: "Carga masiva",
            text: "Junto a «Nuevo trámite» está «Carga masiva»: sube un Excel con varios trámites y el sistema los crea como borradores por lotes. El resultado por fila (creado / con error) se muestra al terminar.",
          },
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
          "Un traspaso u otro trámite con placa pasa a Entregado (en revisión del organismo) y de ahí a Aprobado, Rechazado o En subsanación.",
          "Una matrícula inicial puede tomar la ruta de placa: Preasignación → Asignado → Entregado. Consulta «Matrícula inicial: ruta de placa» para saber qué te corresponde en cada estado.",
          "Haz seguimiento desde el listado (búsqueda por radicado, placa o VIN) o pregúntale a DR. FLIT.",
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
    title: "Seguimiento, búsqueda y estados del trámite",
    audience: "Gestor",
    sectionId: "gestor",
    keywords: [
      "seguimiento",
      "estados",
      "borrador",
      "radicado",
      "buscar tramite",
      "busqueda",
      "filtros",
      "consultas",
      "periodo",
      "prioritario",
      "exportar",
      "rechazado",
      "aprobado",
      "subsanacion",
      "entregado",
      "anulado",
    ],
    summary:
      "Cómo encontrar un trámite (radicado, placa, VIN, filtros), qué significa cada estado y qué hacer en cada uno.",
    blocks: [
      {
        id: "buscar",
        title: "1. Buscar en el listado",
        paragraphs: [
          "En Trámites, la barra «Buscar radicado (FT1-0000012), placa, VIN...» busca en el servidor sobre todo el universo de tu compañía, no solo sobre la página visible. El radicado casa exacto (con o sin prefijo: FT1-0000012 o solo 12); placa, VIN, nombre o documento de las partes, organismo y compañía casan por texto parcial.",
          "«Periodo» acota por fecha de radicación (Hoy, Últimos 7/30/90 días, Mes actual, Mes anterior o Rango propio). «+ Filtro» abre las Consultas: eliges un campo del catálogo, un operador («es», «no es», «contiene», «está vacío», «tiene dato») y los valores. Puedes pegar una columna de Excel con varias placas o radicados: un valor por línea.",
        ],
        bullets: [
          "Las tarjetas por estado sobre la tabla filtran con un clic y cuentan el universo completo, no la página. Solo aparecen los estados de la pestaña activa: en Traspaso y Otros trámites no hay Preasignación ni Asignado.",
          "«Búsqueda rápida», debajo de las tarjetas, trae consultas frecuentes con un clic: en subsanación, rechazados desde preasignación, más de 5 o 10 días en gestión, sin firmas, mis trámites (los que tienes a tu cargo hoy), faltantes por aprobar, sin documento y pausados. No cambia lo que tengas en «+ Filtro».",
          "Los números de las tarjetas se actualizan solos cada minuto; la tabla no se mueve. Si cambió la tarjeta que estás viendo, aparece «Hay cambios — Actualizar».",
          "La estrella marca un trámite como prioritario; el filtro «solo prioritarios» los aísla y, sin orden explícito, van primero.",
          "«Exportar» genera un Excel con TODAS las filas que cumplen los filtros actuales, no solo las de pantalla.",
          "El selector de columnas te deja elegir qué ver; la elección se guarda por usuario.",
        ],
      },
      {
        id: "estados",
        title: "2. Estados de negocio y qué hacer",
        paragraphs: [
          "El chip de estado es el mismo en el listado, el detalle, la línea de tiempo y los correos.",
        ],
        bullets: [
          "Borrador: editable; complétalo y radícalo desde el detalle. Puedes anularlo si no va a continuar.",
          "Preparado: datos completos, listo para radicar.",
          "Preasignación (solo matrícula inicial en ruta larga): radicado sin placa; el organismo debe asignarla. Aún no está en su cola de decisión.",
          "Asignado: el organismo ya asignó la placa. Te toca gestionar SOAT e impuestos y pulsar «Enviar al OT» (acción de la fila).",
          "Entregado: en revisión del organismo de tránsito.",
          "En subsanación: el organismo pide correcciones; corrige lo indicado desde el detalle y reenvía.",
          "Aprobado: decisión final favorable. Solo el Administrador de la compañía puede pedir revocatoria dentro de la ventana permitida.",
          "Rechazado: decisión desfavorable; revisa el motivo en el detalle. El distintivo «Rechazado preasignación» indica que el organismo rechazó antes de asignar la placa.",
          "Revocado: el organismo aprobó una solicitud de revocatoria; libera placa y VIN y deja histórica la documentación.",
          "Anulado: cerrado por la compañía antes de la decisión del organismo.",
        ],
        callouts: [
          {
            variant: "info",
            text: "Las acciones del menú de cada fila cambian con el estado: «Enviar al OT» solo aparece en Asignado; «Ver historial de la placa» solo si tienes el módulo Historial por placa.",
          },
        ],
      },
      {
        id: "detalle",
        title: "3. El detalle del trámite",
        paragraphs: [
          "Al abrir una fila entras al detalle: el asistente (si sigue en borrador), la línea de tiempo de estados con fecha y hora, los documentos del expediente (también desde «Ver documentos» en la fila, sin entrar al asistente) y las observaciones del organismo cuando las hay.",
          "Todas las fechas de la plataforma se muestran en hora de Colombia con el formato DD/MM/YYYY HH:mm; las vigencias (SOAT, RTM, identidad) van sin hora.",
        ],
      },
      {
        id: "drflit",
        title: "4. Búsqueda con DR. FLIT",
        paragraphs: [
          "Abre el chat flotante y elige buscar por placa, VIN, trámite (radicado) o cliente (nombre o documento). Cada resultado muestra radicado, fecha de radicación, estado con una pista de qué sigue, y abre el detalle. Al buscar por placa también te ofrece el historial completo de esa placa.",
        ],
      },
    ],
  },
  {
    slug: "1-gestor/6-ayuda-dr-flit",
    title: "Ayuda con DR. FLIT (Gestor)",
    audience: "Gestor",
    sectionId: "gestor",
    keywords: ["dr flit", "chat", "necesito ayuda", "asistente", "documentacion", "soporte"],
    summary: "Búsquedas operativas, ayuda contextual y canales de soporte desde el asistente.",
    blocks: [
      {
        id: "abrir",
        title: "1. Cómo abrir DR. FLIT",
        paragraphs: [
          "El botón flotante del asistente está disponible en toda la aplicación cuando estás autenticado. Pulsa para abrir el panel; puedes seguir usando la pantalla mientras está abierto. La conversación se conserva al cambiar de módulo hasta que pulses «Terminar chat».",
        ],
      },
      {
        id: "opciones",
        title: "2. Qué puedes hacer",
        paragraphs: ["El menú tiene dos sesiones: Gestión y Ayuda."],
        bullets: [
          "Gestión → Buscar por placa: trámites de esa placa y acceso al historial completo.",
          "Gestión → Buscar por VIN.",
          "Gestión → Buscar por trámite: escribe el radicado (FT1-0000012 o solo 12).",
          "Gestión → Buscar por cliente: nombre o documento; luego eliges ver sus trámites o su validación de identidad.",
          "Ayuda → Necesito ayuda: escribe tu duda en lenguaje natural; si hay artículo en este manual, verás chips para abrirlo. Si estás en un módulo con documentación, te lo sugiere de entrada.",
          "Ayuda → Normativa: la Resolución 20233040017145 de 2023 (Ministerio de Transporte), la norma que avala los trámites virtuales: resumen por temas y PDF completo.",
          "Ayuda → Soporte: correo y formulario oficial para radicar un caso.",
        ],
        callouts: [
          {
            variant: "info",
            text: "DR. FLIT busca dentro de tu alcance: tu compañía o, si eres cabeza de red y elegiste «Toda la red» en Trámites, también tus clientes. Y solo sugiere artículos que aplican a tu perfil.",
          },
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
          "«cómo solicito una revocatoria»",
          "«qué significa asignado»",
        ],
        callouts: [
          {
            variant: "tip",
            text: "DR. FLIT enlaza documentación; no sustituye al soporte para incidentes de producción o caídas de RUNT. Para eso usa Ayuda → Soporte.",
          },
        ],
      },
    ],
  },
  {
    slug: "1-gestor/7-ruta-placa",
    title: "Matrícula inicial: ruta de placa (corta y larga)",
    audience: "Gestor",
    sectionId: "gestor",
    keywords: [
      "ruta de placa",
      "ruta corta",
      "ruta larga",
      "preasignacion",
      "asignado",
      "enviar al ot",
      "digito de preferencia",
      "placa runt",
      "sin placa",
      "matricula inicial",
      "soat",
      "impuestos",
    ],
    summary:
      "Quién decide la ruta de una matrícula inicial, qué estados atraviesa y qué te corresponde hacer en cada uno.",
    blocks: [
      {
        id: "quien-decide",
        title: "1. La ruta la decide el RUNT",
        paragraphs: [
          "Al consultar el vehículo en el paso 1, FLIT lee el historial del RUNT. Si el vehículo ya tiene placa preasignada (registrado), el trámite va por Ruta Corta: la placa y el organismo vienen del RUNT y se muestran en solo lectura; no eliges secretaría ni dígito.",
          "Si el vehículo no tiene placa, va por Ruta Larga: eliges la secretaría y debes indicar un dígito de preferencia de placa (0-9) o «sin preferencia». El dígito llega al organismo como guía; no garantiza la placa.",
        ],
        callouts: [
          {
            variant: "warning",
            text: "Si el organismo que trae el RUNT no está habilitado para tu compañía, el sistema lo indica con el nombre del organismo y no deja continuar hasta resolverlo con tu administrador.",
          },
        ],
      },
      {
        id: "estados",
        title: "2. Estados de la ruta larga",
        paragraphs: [],
        bullets: [
          "Preasignación: radicaste sin placa. El organismo tiene el trámite en su cola de «asignar placa». Tú esperas.",
          "Asignado: el organismo asignó la placa (recibes correo). Gestiona SOAT e impuestos y, desde la fila del listado o el detalle, pulsa «Enviar al OT» confirmando los checks. Si la compañía permite continuar sin SOAT vigente, verás una advertencia pero el envío procede.",
          "Entregado: ya está en la cola de decisión del organismo, como cualquier otro trámite.",
          "Rechazado con distintivo «Rechazado preasignación»: el organismo rechazó antes de asignar placa; el motivo está en el detalle. Puedes filtrar estos casos con el atajo «Rechazado desde preasignación» de la búsqueda rápida.",
        ],
      },
      {
        id: "cambios",
        title: "3. Qué ya no se hace",
        paragraphs: [
          "El gestor ya no elige placas del inventario del organismo en el paso de firma. Un borrador antiguo con placa elegida a mano la muestra y solo permite quitarla (vuelve a la Ruta Larga).",
        ],
      },
    ],
  },
  {
    slug: "1-gestor/8-identidad",
    title: "Identidad: validaciones biométricas",
    audience: "Gestor",
    sectionId: "gestor",
    keywords: [
      "identidad",
      "validaciones",
      "validacion de identidad",
      "biometria",
      "biometrica",
      "cedula",
      "vigencia",
      "enlace",
      "atascadas",
      "prevalidacion",
      "score",
    ],
    summary:
      "Qué muestra el módulo Identidad, cómo buscar una persona, leer estados y vigencias y desatascar una validación.",
    blocks: [
      {
        id: "que-es",
        title: "1. Qué es",
        paragraphs: [
          "El módulo Identidad (píldora «Identidad» del dock) lista todas las validaciones biométricas de tu compañía: las que nacen dentro de un trámite (por cada parte que firma) y las prevalidaciones sueltas que creaste sin trámite.",
          "Cada fila muestra la persona, su documento completo (tipo y número), correo, estado, score, si el enlace de captura sigue vigente, la vigencia de la validación y el trámite asociado si lo hay.",
        ],
      },
      {
        id: "buscar",
        title: "2. Buscar y filtrar",
        paragraphs: [],
        bullets: [
          "Busca por nombre completo o número de cédula.",
          "Filtra por estado (Aprobadas, En proceso, Rechazadas) desde las tarjetas superiores.",
          "«Vence en ≤ N días» encuentra validaciones cuya vigencia está por expirar: renuévalas antes de reutilizarlas en un trámite.",
          "Desde DR. FLIT: Buscar por cliente → «Ver validación de identidad».",
        ],
      },
      {
        id: "acciones",
        title: "3. Acciones",
        paragraphs: [],
        bullets: [
          "«Nueva» crea una prevalidación de persona natural: se envía un enlace de captura al correo indicado y el resultado queda disponible para trámites posteriores.",
          "Abre una fila para ver el detalle: proceso del proveedor, reintentos y línea de tiempo del registro.",
          "«Ver alertas de validación» muestra las validaciones atascadas (sin respuesta del proveedor) agrupadas por persona, con opción de reintentar todas.",
        ],
        callouts: [
          {
            variant: "info",
            text: "Una validación aprobada tiene vigencia. Al acercarse la fecha, el sistema la marca; una validación vencida no sirve para firmar y hay que repetirla.",
          },
        ],
      },
    ],
  },
  {
    slug: "1-gestor/9-historial-placa",
    title: "Historial por placa",
    audience: "Gestor",
    sectionId: "gestor",
    keywords: [
      "historial por placa",
      "historial",
      "placa",
      "que le ha pasado a esta placa",
      "tramites de una placa",
      "cronologico",
    ],
    summary: "Todos los trámites que ha tenido una placa dentro de FLIT, en orden cronológico.",
    blocks: [
      {
        id: "que-es",
        title: "1. Para qué sirve",
        paragraphs: [
          "Responde «qué le ha pasado a esta placa dentro de FLIT»: escribe la placa y obtienes la lista de sus trámites, del más reciente al más antiguo, con tipo, estado, fechas, gestor, organismo, comprador y vendedor.",
          "Para tu compañía ves solo tus trámites; el Super Admin ve la placa en todas las compañías.",
        ],
      },
      {
        id: "como-llegar",
        title: "2. Cómo llegar",
        paragraphs: [],
        bullets: [
          "Dock → Trámites → «Historial por placa» (si tu rol tiene el módulo).",
          "Desde una fila del listado de trámites: menú de acciones → «Ver historial de la placa».",
          "Desde DR. FLIT: tras buscar por placa, pulsa «Ver historial completo de la placa».",
        ],
        callouts: [
          {
            variant: "tip",
            text: "Una placa sin trámites no es un error: la vista muestra un estado vacío con la placa consultada.",
          },
        ],
      },
    ],
  },
  {
    slug: "1-gestor/10-revocatorias",
    title: "Solicitar la revocatoria de un trámite aprobado",
    audience: "Gestor",
    sectionId: "gestor",
    keywords: [
      "revocatoria",
      "revocatorias",
      "revocar",
      "solicitar revocatoria",
      "ventana de revocatoria",
      "revocado",
      "aprobado",
      "soporte pdf",
    ],
    summary:
      "Quién puede pedir la revocatoria de un trámite aprobado, con qué condiciones, cómo se solicita y cómo se sigue.",
    blocks: [
      {
        id: "quien",
        title: "1. Quién y cuándo",
        paragraphs: [
          "Solo el Administrador de la compañía puede solicitar la revocatoria (el operador ve el botón pero no puede accionarlo). El trámite debe estar Aprobado, haber sido creado en FLIT y estar dentro de la ventana de revocatoria que configura cada organismo; vencida la ventana, el botón se deshabilita con ese motivo.",
        ],
      },
      {
        id: "como",
        title: "2. Cómo solicitarla",
        paragraphs: [],
        bullets: [
          "En el detalle del trámite pulsa «Solicitar revocatoria».",
          "Paso 1: lee la advertencia. La solicitud no se puede retirar una vez enviada.",
          "Paso 2: explica el motivo, adjunta el documento de soporte en PDF y confirma las dos casillas (información correcta; entiendo que no se puede retirar).",
          "«Enviar solicitud». El trámite sigue Aprobado, pero recibe el distintivo «Revocatoria solicitada».",
        ],
        callouts: [
          {
            variant: "warning",
            text: "Esta acción no tiene reversa. Si el organismo la aprueba, el trámite pasa a Revocado: se liberan placa y VIN y la documentación queda histórica.",
          },
        ],
      },
      {
        id: "seguimiento",
        title: "3. Seguimiento",
        paragraphs: [
          "El sub-estado se ve como badge en el listado y el detalle: Revocatoria solicitada → Revocatoria en revisión → Revocatoria aprobada o rechazada. Cada hito envía correo a la compañía.",
          "La vista «Revocatorias» (botón en el listado de Trámites, solo para el Administrador) agrupa los trámites con solicitud en cualquier sub-estado, con filtros por fecha, organismo y estado.",
        ],
      },
    ],
  },
  {
    slug: "1-gestor/11-reportes",
    title: "Reportes y analíticas",
    audience: "Gestor",
    sectionId: "gestor",
    keywords: [
      "reportes",
      "analiticas",
      "reportes detallados",
      "indicadores",
      "kpi",
      "productividad",
      "consultas personalizadas",
      "exportar excel",
      "tiempos",
      "organismo",
    ],
    summary: "Qué responde cada pestaña de Reportes, qué añade Reportes Detallados y cómo exportar.",
    blocks: [
      {
        id: "reportes",
        title: "1. Reportes y Analíticas",
        paragraphs: ["Píldora «Reportes» del dock. Elige el rango de fechas y navega por pestañas:"],
        bullets: [
          "Resumen general: volumen por familia (Matrículas, Traspasos, Otros) y tiempos: el de tu equipo (creación → entrega) y el del organismo (entrega → decisión).",
          "Operación / Trámites: trámites creados por mes y categoría, y embudo de estados.",
          "Organismo de Tránsito: entregados por organismo y por tipo de trámite.",
          "Productividad: rendimiento por gestor.",
          "Uso del aplicativo: actividad de los usuarios en la plataforma.",
          "Consultas personalizadas: arma tu propio cruce con la gramática de Consultas (campo, operador, valores) y guárdalo.",
        ],
      },
      {
        id: "detallados",
        title: "2. Reportes Detallados",
        paragraphs: [
          "Segmenta trámite a trámite por persona (documento o nombre), tipo, categoría, estado, organismo, radicado, si tiene transformación y si es leasing, dentro del rango elegido (por defecto los últimos 30 días). Incluye indicadores de cabecera y una grilla exportable.",
        ],
      },
      {
        id: "alcance",
        title: "3. Alcance y exportación",
        paragraphs: [
          "Los reportes miran tu compañía. Si eres Administrador de una cabeza de red (concesión o marca blanca) y eliges «Toda la red» o un cliente en el selector de alcance, la analítica cambia con él; algunas exportaciones están disponibles solo para la compañía propia.",
          "Toda exportación a Excel usa el mismo formato de fecha de la interfaz (DD/MM/YYYY HH:mm, hora de Colombia).",
        ],
      },
    ],
  },
  {
    slug: "1-gestor/12-usuarios",
    title: "Usuarios de la compañía",
    audience: "Gestor",
    sectionId: "gestor",
    keywords: [
      "usuarios",
      "invitar usuario",
      "invitacion",
      "perfil",
      "roles",
      "restablecer contrasena",
      "reenviar invitacion",
      "eliminar usuario",
      "acceso",
    ],
    summary: "Cómo invitar, editar, reactivar y retirar usuarios de tu compañía, y qué queda auditado.",
    blocks: [
      {
        id: "invitar",
        title: "1. Invitar un usuario",
        paragraphs: [
          "Píldora «Usuarios» del dock (visible para el Administrador de la compañía). «Invitar usuario»: nombre, correo, perfil y roles que aplican a tu compañía. La persona recibe un enlace de activación por correo; hasta que lo use, la fila queda en «Onboarding» y puedes reenviar o cancelar la invitación.",
        ],
        callouts: [
          {
            variant: "info",
            text: "Los roles y permisos del sistema se definen en el módulo RBAC del Super Admin; aquí solo asignas los que existen para tu perfil.",
          },
        ],
      },
      {
        id: "gestionar",
        title: "2. Gestionar usuarios existentes",
        paragraphs: [],
        bullets: [
          "Editar: cambia nombre, perfil o roles.",
          "Restablecer contraseña: envía instrucciones de cambio al correo del usuario (requiere el permiso correspondiente o ser Administrador).",
          "Eliminar usuario: retira el acceso; queda en la lista de eliminados con fecha.",
          "Último ingreso: te dice quién no ha vuelto a entrar.",
        ],
      },
      {
        id: "auditoria",
        title: "3. Auditoría",
        paragraphs: [
          "La pestaña Auditoría muestra quién cambió qué y cuándo (últimos 50 eventos): invitaciones, cambios de rol, restablecimientos y eliminaciones.",
        ],
      },
    ],
  },
];
