import type { ManualArticle } from "../types";

export const GESTOR_ARTICLES: ManualArticle[] = [
  {
    slug: "1-gestor/1-inicio",
    title: "Inicio (Dashboard)",
    audience: "Gestor",
    sectionId: "gestor",
    keywords: [
      "dashboard",
      "inicio",
      "fab",
      "resumen",
      "gestor",
      "indicadores",
      "kpi",
      "banner",
      "banners",
      "carrusel",
      "comparendos",
      "resoluciones",
      "alcance de red",
      "toda la red",
    ],
    summary: "Qué ves al entrar como Gestor, para qué sirve el Dashboard y cómo orientarte.",
    blocks: [
      {
        id: "que-es",
        title: "1. Qué es el Inicio",
        paragraphs: [
          "El botón central flotante (FAB) «Inicio FLIT» abre el Dashboard. Es tu punto de partida después de iniciar sesión.",
          "Arriba encontrarás un carrusel: un slide de bienvenida fijo y, detrás, los banners que la plataforma tenga activos para tu compañía (novedades, avisos, campañas). Tú no los administras; si no hay ninguno activo, el carrusel solo muestra el slide de bienvenida.",
          "Debajo verás el resumen operativo de tu compañía: volumen de trámites, distribución por estado y seguimiento operativo. No aparece como píldora del dock inferior; siempre está disponible desde el FAB.",
          "El volumen se resume en cinco tarjetas: Total trámites, Matrículas, Traspasos, Otros Trámites (cualquier trámite que no sea matrícula ni traspaso) y Completados.",
        ],
        media: [
          {
            kind: "image",
            src: "/manual/screenshots/dashboard-gestor.png",
            alt: "Dashboard de FLIT con los indicadores de trámites, la distribución por estado, las validaciones biométricas y el seguimiento operativo",
            caption: "El Inicio: indicadores del periodo, distribución por estado y seguimiento operativo.",
          },
        ],
      },
      {
        id: "modulos-activos",
        title: "2. Tarjetas de módulo (Comparendos y Resoluciones)",
        paragraphs: [
          "Si tu compañía activó los módulos Comparendos y/o Resoluciones (configuración de tu Administrador), verás una tarjeta por cada uno con su propio resumen. Si ninguno está activo, esas tarjetas no aparecen.",
        ],
      },
      {
        id: "cuando-usar",
        title: "3. Cuándo usarlo",
        paragraphs: [],
        bullets: [
          "Antes de abrir Trámites, para tener contexto del día o la semana.",
          "Para verificar que entraste con la compañía correcta (su nombre aparece en la barra superior).",
          "Como atajo de regreso desde cualquier módulo: un clic en el FAB te devuelve al inicio.",
        ],
      },
      {
        id: "alcance-red",
        title: "4. Selector de alcance de red (solo cabezas de red)",
        paragraphs: [
          "Si tu compañía es cabeza de una red (Concesión o Marca Blanca), verás un selector para elegir entre «Mi compañía» y «Toda la red» (o un cliente puntual). Con la red elegida, los indicadores del Inicio cambian a los agregados de tus clientes; tu preferencia se recuerda la próxima vez que entres.",
        ],
      },
      {
        id: "permisos",
        title: "5. Permisos y visibilidad",
        paragraphs: [
          "Si tu rol no tiene permiso para ver el Inicio, el FAB sigue visible pero algunos widgets pueden estar vacíos o no cargar datos.",
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
      "quien firmara el mandato",
      "sin mandatario configurado",
      "no se puede radicar mandatario",
    ],
    summary: "Guía paso a paso para iniciar un trámite desde el módulo Trámites.",
    blocks: [
      {
        id: "antes",
        title: "1. Antes de empezar",
        paragraphs: [
          "Confirma que tu compañía tiene habilitado el tipo de trámite (lo define su configuración operativa). Si la matrícula inicial está apagada, no verás esa modalidad al crear.",
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
        media: [
          {
            kind: "diagram",
            id: "flujo-crear-tramite",
            alt: "Diagrama del flujo de un trámite: elegir tipo, completar actores y vehículo, cargar documentos, resolver identidad y firmas, radicar y esperar la decisión del organismo, con retorno a subsanar si es rechazado",
            caption: "El recorrido completo de un trámite, de Borrador a la decisión del organismo.",
          },
        ],
        callouts: [
          {
            variant: "info",
            title: "Carga masiva",
            text: "Junto a «Exportar» hay un botón «Carga masiva» para crear varios trámites de una sola vez desde un Excel. Consulta el artículo «Carga masiva de trámites» para el paso a paso completo.",
          },
        ],
      },
      {
        id: "wizard",
        title: "3. Cómo funciona el asistente",
        paragraphs: [
          "Los pasos, campos y reglas que ves en el asistente los define el sistema según el tipo de trámite, el organismo de tránsito y lo que ya completaste; no son siempre los mismos ni algo que el gestor pueda reordenar.",
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
        id: "firmante-mandato",
        title: "4. Quién firmará el mandato",
        paragraphs: [
          "En el último paso del asistente (Resumen) ves, en solo lectura, quién firmará el contrato de mandato: «Firmará: nombre / forma de firma» (Firma del baúl o Biometría). Tú no eliges al mandatario: lo define el organismo de tránsito, o el Administrador de tu compañía desde su pestaña «Mandatarios».",
          "Si tu compañía no tiene mandatario propio pero hay uno asociado a ella desde otra compañía, verás «Firmará: nombre / forma de firma» con la nota «Mandatario asociado». Solo se muestra quién firma: nunca el nombre de la otra compañía ni su documento de identidad.",
          "Si no hay un mandatario válido, el Resumen te lo avisa con el motivo (por ejemplo, sin mandatario registrado, fuera de vigencia o con la firma o biometría vencida) y a quién pedirle que lo configure.",
        ],
        bullets: [
          "Alerta roja «Sin mandatario configurado — no se puede radicar»: el botón «Finalizar y enviar trámite» queda deshabilitado hasta que el organismo o tu Administrador lo configure; al resolverlo, reabre el Resumen.",
          "Alerta amarilla «Sin mandatario configurado»: es solo una advertencia; puedes radicar, pero conviene pedir que lo configuren.",
          "Si el Resumen no muestra ni indicador ni alerta (mandato abierto o institucional, sin organismo elegido, o no se pudo consultar), puedes seguir; el sistema revisa el mandatario al radicar y, si falta, te explica el motivo.",
        ],
      },
      {
        id: "despues",
        title: "5. Después de radicar",
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
          "Los documentos que te pide el sistema salen del catálogo del tipo de trámite y pueden tener ajustes propios de cada Organismo de Tránsito.",
          "En el paso Documentos del asistente, la sección de adjuntos lista los obligatorios y opcionales antes de permitir radicación. Lo que no aparece ahí no debería pedírtelo el sistema para ese trámite.",
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
          "Usa el paso Documentos del asistente para subir cada archivo. Formatos típicos: PDF, JPG, PNG.",
          "Un nombre descriptivo ayuda al revisor del organismo. Evita fotos borrosas o PDFs protegidos con contraseña.",
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
          "Falta un obligatorio → el asistente bloquea el avance hasta que lo completes.",
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
        media: [
          {
            kind: "diagram",
            id: "flujo-prevalidacion",
            alt: "Diagrama del flujo de una prevalidación: crearla, enviar el enlace al cliente, captura de fotos, y según el resultado usarla en trámites, reenviarla o gestionarla desde atascadas",
            caption: "El recorrido de una prevalidación, del envío del enlace al resultado.",
          },
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
      "exportar a excel",
      "exportar tramites",
      "descargar excel",
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
    keywords: [
      "dr flit",
      "chat",
      "necesito ayuda",
      "asistente",
      "documentacion",
      "soporte",
      "inteligencia artificial",
      "caso de soporte",
      "autorizacion de datos",
      "tope de mensajes",
    ],
    summary:
      "Escríbele con tus palabras: responde dudas citando este manual, busca trámites y radica casos de soporte sin salir de la plataforma.",
    blocks: [
      {
        id: "abrir",
        title: "1. Cómo abrir DR. FLIT y hablarle",
        paragraphs: [
          "El botón flotante del asistente está disponible en toda la aplicación cuando estás autenticado. Al abrirlo verás un saludo que te invita a escribir tu duda con tus propias palabras — no necesitas elegir ningún menú: escribe «¿cómo creo un traspaso?» o «me sale un error al firmar» y DR. FLIT entiende qué necesitas.",
          "Debajo del saludo tienes atajos: los chips de Gestión (Placa, VIN, Trámite, Cliente) para búsquedas directas, y la lista de Ayuda (Necesito ayuda, Normativa, Soporte).",
          "La conversación se conserva al cambiar de módulo y al cerrar el panel con la X. Solo «Terminar chat» la borra.",
        ],
      },
      {
        id: "autorizacion",
        title: "2. Autorización de datos (una sola vez)",
        paragraphs: [
          "La primera vez que uses el chat con inteligencia artificial o vayas a radicar un caso de soporte, DR. FLIT te pide autorizar el tratamiento de tus datos (Ley 1581 de 2012). Pulsa «Ver detalle» si quieres leer el texto completo y «Acepto y continuar» para seguir con lo que estabas haciendo.",
        ],
        bullets: [
          "Si eliges «Ahora no», nada se envía y puedes seguir usando Gestión, Necesito ayuda y Normativa sin inteligencia artificial; la conversación no se borra.",
          "Solo se pide una vez: aceptada la versión vigente, no vuelve a aparecer aunque termines el chat o entres desde otro navegador.",
          "No escribas datos personales (cédulas, teléfonos) en el chat; para el caso de soporte hay un formulario aparte que los protege.",
        ],
      },
      {
        id: "dudas",
        title: "3. Dudas: respuestas con base en este manual",
        paragraphs: [
          "Escribe tu pregunta y DR. FLIT responde con base en el manual funcional, citando al final los artículos de donde salió la respuesta — tarjetas con enlace directo para abrir la documentación completa. Si la pregunta no es clara, te hace una pregunta de seguimiento; si el manual no tiene la respuesta, te lo dice y te ofrece escalar a soporte.",
        ],
        bullets: [
          "«cómo creo un trámite de traspaso»",
          "«qué documentos necesito para matrícula»",
          "«cómo envío prevalidación»",
          "«qué significa asignado»",
        ],
        callouts: [
          {
            variant: "info",
            text: "El chat con inteligencia artificial tiene un tope diario de mensajes. DR. FLIT te avisa cuando te acercas y, si lo alcanzas, el menú completo sigue funcionando; al día siguiente el chat se reactiva solo.",
          },
          {
            variant: "tip",
            text: "Si el asistente no está disponible, no te quedas sin ayuda: responde el buscador del manual con una «respuesta rápida» y los enlaces a los artículos.",
          },
        ],
      },
      {
        id: "gestion",
        title: "4. Buscar trámites desde el chat",
        paragraphs: [
          "Pídelo con tus palabras — «busca la placa ABC123», «muéstrame el trámite 12» — y DR. FLIT te lleva directo a la búsqueda de Gestión sin que repitas nada. También puedes usar los chips: Placa, VIN, Trámite (radicado con o sin prefijo) o Cliente (nombre o documento).",
        ],
        callouts: [
          {
            variant: "info",
            text: "DR. FLIT busca dentro de tu alcance: tu compañía o, si eres cabeza de red y elegiste «Toda la red» en Trámites, también tus clientes. Y solo sugiere artículos que aplican a tu perfil.",
          },
        ],
      },
      {
        id: "soporte",
        title: "5. Radicar un caso de soporte sin salir del chat",
        paragraphs: [
          "Cuéntale el problema («me sale un error al firmar un trámite») y DR. FLIT abre el formulario del caso con tus datos ya precargados: correo, compañía y fecha. Tú completas el título, el detalle del error, el resultado que esperabas, con qué frecuencia pasa y la prioridad; si quieres, adjuntas imágenes o PDF.",
        ],
        bullets: [
          "Antes de enviar verás un resumen completo; nada se radica hasta que pulses «Confirmar y radicar caso».",
          "Al confirmar recibes tu número: «Tu caso #N quedó radicado». El equipo de soporte te responde al correo registrado.",
          "Si el sistema de soporte no responde en ese momento, verás los canales de contacto (correo de soporte) y podrás «Reintentar» o «Editar el caso» sin perder nada de lo que escribiste.",
        ],
        callouts: [
          {
            variant: "tip",
            text: "Para incidentes de producción o caídas de RUNT, el caso de soporte es el camino: llega directo al tablero del equipo FLIT con tu descripción y adjuntos.",
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
      "red",
      "toda la red",
      "solo lectura",
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
          "Abre una fila para ver el detalle en un panel lateral (drawer): proceso ante el proveedor, reintentos y línea de tiempo del registro.",
          "«Ver alertas de validación» abre el panel de validaciones atascadas (sin respuesta del proveedor), agrupadas por persona, con opción de reintentar todas de una vez o una por una.",
        ],
        callouts: [
          {
            variant: "info",
            text: "Una validación aprobada tiene vigencia. Al acercarse la fecha, el sistema la marca; una validación vencida no sirve para firmar y hay que repetirla.",
          },
        ],
      },
      {
        id: "red",
        title: "4. Si tu compañía es cabeza de red",
        paragraphs: [
          "Con el selector de alcance en «Toda la red» o sobre un cliente puntual, ves también las validaciones de tus hijas: esas filas quedan en solo lectura (puedes abrir el detalle, pero no reenviar, editar ni reintentar desde ahí). Crear una prevalidación nueva solo aplica sobre tu propia compañía.",
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
        media: [
          {
            kind: "image",
            src: "/manual/screenshots/historial-placa.png",
            alt: "Módulo Historial por placa con el campo de consulta y el estado inicial esperando una placa",
            caption: "Historial por placa: escribe la placa y consulta toda su vida en FLIT.",
          },
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
        media: [
          {
            kind: "diagram",
            id: "flujo-revocatoria",
            alt: "Diagrama del flujo de una revocatoria: la compañía la solicita con motivo, el organismo decide, y el trámite queda Revocado con placa liberada o sigue Aprobado si la rechazan",
            caption: "La revocatoria de punta a punta: de la solicitud a la decisión del organismo.",
          },
        ],
      },
      {
        id: "seguimiento",
        title: "3. Seguimiento",
        paragraphs: [
          "El sub-estado se ve como badge en el listado y el detalle: Revocatoria solicitada → Revocatoria en revisión → Revocatoria aprobada o rechazada. Cada hito envía correo a la compañía.",
          "La vista «Revocatorias» agrupa todos los trámites de tu compañía con solicitud en cualquier sub-estado, con filtros por fecha, organismo y estado. Hoy se abre escribiendo la dirección /tramites/revocatorias directamente en el navegador; el listado de Trámites todavía no tiene un botón que te lleve ahí.",
        ],
      },
      {
        id: "despues",
        title: "4. Qué pasa después de enviarla",
        paragraphs: [
          "La solicitud queda «En revisión» hasta que el organismo de tránsito la decide desde su propia bandeja: no hay una fecha fija de respuesta, depende de cada organismo.",
        ],
        bullets: [
          "Si el organismo la aprueba: el trámite pasa a Revocado, se liberan la placa y el VIN para un trámite nuevo, y toda la documentación queda histórica (consultable, pero ya no editable).",
          "Si el organismo la rechaza: el trámite vuelve a Aprobado tal como estaba; el motivo del rechazo queda visible en el detalle.",
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
      "programacion",
      "programaciones",
      "alertas",
      "informes programados",
      "historial de alertas",
    ],
    summary: "Qué responde cada pestaña de Reportes y cómo programar informes y alertas.",
    blocks: [
      {
        id: "reportes",
        title: "1. Reportes y Analíticas",
        paragraphs: [
          "Píldora «Reportes» del dock. Elige el rango de fechas y navega por pestañas; cada una aparece solo si tu rol tiene el permiso correspondiente (algunas compañías no habilitan todas):",
        ],
        bullets: [
          "Resumen general: volumen por familia (Matrículas, Traspasos, Otros) y tiempos: el de tu equipo (creación → entrega) y el del organismo (entrega → decisión).",
          "Operación / Trámites: trámites creados por mes y categoría, y embudo de estados.",
          "Organismo de Tránsito: entregados por organismo y por tipo de trámite.",
          "Uso del aplicativo: actividad de los usuarios en la plataforma.",
          "Productividad: rendimiento por gestor.",
          "Consultas personalizadas: arma tu propio cruce con la gramática de Consultas (campo, operador, valores) y guárdalo.",
        ],
        callouts: [
          {
            variant: "info",
            text: "Si no ves ninguna pestaña, pide a tu Administrador que te asigne acceso a alguna de Reportes.",
          },
        ],
        media: [
          {
            kind: "image",
            src: "/manual/screenshots/reportes.png",
            alt: "Módulo Reportes de FLIT con sus pestañas y el rango de fechas seleccionado",
            caption: "Reportes: cada pestaña responde una pregunta distinta del negocio.",
          },
        ],
      },
      {
        id: "programacion",
        title: "2. Programación y alertas",
        paragraphs: [
          "El botón «Programación y alertas» (visible con el permiso correspondiente) abre un panel con dos secciones:",
        ],
        bullets: [
          "Informes programados: elige un tipo de reporte, la frecuencia (diaria, semanal o mensual), el formato (Excel o PDF) y hasta 10 correos destinatarios; el sistema te lo envía automáticamente.",
          "Alertas: define una condición sobre un indicador (por ejemplo, tasa de rechazo o trámites atascados) y un umbral; cuando se cruza, se notifica a los correos que configures. La misma sección guarda el historial de disparos anteriores.",
        ],
      },
      {
        id: "detallados",
        title: "3. Reportes Detallados (otro módulo)",
        paragraphs: [
          "«Reportes Detallados» es una píldora aparte del dock, no una pestaña de Reportes. Sirve para segmentar trámite a trámite por persona, transformación, leasing u organismo. Consulta el artículo «Reportes Detallados» para el detalle.",
        ],
      },
      {
        id: "alcance",
        title: "4. Alcance y exportación",
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
      "roles y permisos",
      "clientes ict",
      "suspender",
      "desactivar",
      "bloquear",
    ],
    summary: "Las pestañas del módulo Usuarios, cómo invitar y gestionar personas, y qué es Clientes ICT.",
    blocks: [
      {
        id: "pestanas",
        title: "1. Las pestañas del módulo",
        paragraphs: [
          "Píldora «Usuarios» del dock (visible para el Administrador de la compañía). Según tu perfil y permisos verás hasta tres pestañas:",
        ],
        bullets: [
          "Usuarios: invitar, editar y gestionar el acceso de las personas de tu compañía.",
          "Roles y permisos: consulta qué puede hacer cada rol. Para tu perfil es de solo lectura; los roles del sistema los crea y edita el equipo FLIT (Super Admin).",
          "Clientes ICT: solo si tu compañía integra con terceros y tienes el permiso correspondiente (ver bloque 4).",
        ],
        media: [
          {
            kind: "image",
            src: "/manual/screenshots/usuarios.png",
            alt: "Módulo Usuarios de FLIT con la lista del equipo, sus roles, estado y acciones",
            caption: "Usuarios: el acceso de tu equipo, con invitación y gestión por fila.",
          },
        ],
      },
      {
        id: "invitar",
        title: "2. Invitar un usuario",
        paragraphs: [
          "En la pestaña Usuarios, «Invitar usuario»: nombre, correo, perfil y roles que aplican a tu compañía. La persona recibe un enlace de activación por correo; hasta que lo use, la fila queda en «Onboarding» y puedes reenviar o cancelar la invitación.",
        ],
      },
      {
        id: "gestionar",
        title: "3. Gestionar usuarios existentes",
        paragraphs: [
          "Como Administrador de la compañía puedes editar, restablecer contraseña, y suspender o desactivar un usuario. Eliminar un usuario de forma definitiva (y restaurarlo desde Eliminados) es exclusivo del equipo FLIT.",
        ],
        bullets: [
          "Editar: cambia nombre, perfil o roles.",
          "Restablecer contraseña: envía instrucciones de cambio al correo del usuario.",
          "Suspender: bloquea el acceso temporalmente; puedes reactivarlo cuando corresponda.",
          "Desactivar: bloquea el acceso de forma indefinida.",
          "Último ingreso: te dice quién no ha vuelto a entrar.",
          "En la tabla, «Perfil» dice a qué tipo de cuenta pertenece la persona (Gestor, OT o FLIT) y «Rol» qué puede hacer. El administrador se distingue con «Gestor · Admin» (u «OT · Admin» en un organismo); «Fecha» es el día en que se creó el usuario.",
        ],
      },
      {
        id: "roles",
        title: "4. Roles y permisos (solo lectura)",
        paragraphs: [
          "Consulta los roles disponibles para tu compañía y qué incluye cada uno. Crear, editar o desactivar roles del sistema es exclusivo del Super Admin; si necesitas un ajuste, contáctalo.",
        ],
      },
      {
        id: "ict",
        title: "5. Clientes ICT (si tu compañía integra con terceros)",
        paragraphs: [
          "Son credenciales que usan integraciones externas para registrar pre-trámites en tu nombre. Cada cliente tiene un usuario y un secreto que el sistema genera y muestra una sola vez (no se puede recuperar después); si lo pierdes, usa «Regenerar secreto».",
        ],
      },
    ],
  },
  {
    slug: "1-gestor/13-carga-masiva",
    title: "Carga masiva de trámites",
    audience: "Gestor",
    sectionId: "gestor",
    keywords: [
      "carga masiva",
      "excel",
      "plantilla",
      "lote",
      "lotes",
      "subir archivo",
      "crear varios tramites",
      "borradores",
      "resultado por fila",
      "creacion bloqueada",
    ],
    summary: "Cómo crear varios trámites a la vez desde un Excel, leer el resultado y corregir errores.",
    blocks: [
      {
        id: "donde",
        title: "1. Dónde está",
        paragraphs: [
          "En Trámites, junto al botón «Exportar», está «Carga masiva». Se abre un modal con dos pestañas: «Cargar archivo» y «Resultados» (para ver en qué quedó un lote que subiste antes).",
        ],
        callouts: [
          {
            variant: "warning",
            text: "El botón aparece deshabilitado con el motivo «La compañía tiene bloqueada la creación de trámites» si tu Administrador bloqueó la creación de trámites para la compañía. Mientras eso siga así, tampoco podrás crear trámites uno por uno.",
          },
        ],
      },
      {
        id: "plantilla",
        title: "2. Descarga la plantilla",
        paragraphs: [],
        bullets: [
          "Elige el tipo de trámite (Matrícula o Traspaso, según lo que ofrezca tu compañía) y pulsa «Descargar plantilla».",
          "El Excel trae una hoja de instrucciones y listas desplegables para los campos que las necesitan.",
          "No cambies ni muevas las columnas de la primera fila: son las que el sistema usa para validar el archivo al subirlo.",
          "Puedes cargar hasta el máximo de filas por archivo que indica el propio modal.",
        ],
      },
      {
        id: "subir",
        title: "3. Diligencia y sube el archivo",
        paragraphs: [
          "Completa una fila por trámite con los datos de vehículo y actores, y guarda el archivo en formato .xlsx (no cambies la extensión).",
          "Selecciona el archivo y pulsa «Procesar archivo». No necesitas esperar en la pantalla: el sistema encola el lote y lo procesa en segundo plano, fila por fila (cada fila hace sus propias consultas, así que un lote grande tarda). Puedes cerrar la ventana y seguir trabajando; el resultado completo aparece luego en la pestaña Resultados.",
        ],
        media: [
          {
            kind: "diagram",
            id: "flujo-carga-masiva",
            alt: "Diagrama del flujo de carga masiva: descargar plantilla, diligenciar, subir, revisar el resultado por fila y corregir solo las filas con error",
            caption: "El ciclo de la carga masiva: plantilla, lote, resultado y corrección.",
          },
        ],
      },
      {
        id: "resultados",
        title: "4. Resultado por fila y cómo corregir",
        paragraphs: [
          "En la pestaña Resultados eliges el lote (por tipo, fecha y nombre de archivo) y ves, mientras el lote sigue en proceso, cuántas filas van completadas del total.",
          "Cuando termina, verás el conteo de creados y no creados, y una tabla con cada fila: el vehículo (placa o VIN), el resultado y, si no se creó, el motivo exacto (por ejemplo: datos de contacto incompletos, vehículo no encontrado en el RUNT, porcentajes de propiedad que no suman 100, o ya existe un trámite en proceso para ese vehículo en tu empresa).",
          "Cada fila creada te lleva directo al trámite (Borrador) con un clic; ya puedes seguirlo desde ahí. Las filas con error no crean nada: corrige el dato en tu Excel y vuelve a cargar solo esas filas en un lote nuevo.",
        ],
      },
      {
        id: "casos",
        title: "5. Casos frecuentes",
        paragraphs: [],
        bullets: [
          "Subiste un archivo con otra extensión (por ejemplo .xls o .csv): el sistema no lo acepta; guárdalo de nuevo como .xlsx.",
          "Cambiaste el orden o el nombre de una columna: el archivo puede fallar por completo al subirlo. Descarga la plantilla otra vez y copia tus datos ahí.",
          "Una fila de traspaso trae varios actores y el motivo dice a cuál de ellos le falta el dato (el detalle entre paréntesis identifica el documento de la persona).",
          "El lote lleva mucho tiempo «en proceso»: no hace falta quedarte esperando; cierra la ventana y vuelve más tarde a la pestaña Resultados.",
        ],
      },
    ],
  },
  {
    slug: "1-gestor/14-reportes-detallados",
    title: "Reportes Detallados",
    audience: "Gestor",
    sectionId: "gestor",
    keywords: [
      "reportes detallados",
      "segmentacion",
      "persona",
      "transformacion",
      "leasing",
      "organismo",
      "exportar",
      "grilla",
      "consulta detallada",
    ],
    summary: "Qué es Reportes Detallados, cuándo usarlo en vez de Reportes y cómo entrar.",
    blocks: [
      {
        id: "que-es",
        title: "1. Qué es",
        paragraphs: [
          "Reportes Detallados es un módulo aparte (píldora propia del dock, no una pestaña de Reportes) para consultar tus trámites trámite a trámite, en una grilla filtrable y exportable, en vez de ver solo totales agregados.",
          "Puedes segmentar por persona (documento o nombre), tipo y categoría de trámite, estado, radicado, organismo de tránsito, si el trámite tiene una transformación (por ejemplo, cambio de características del vehículo) y si es un trámite de leasing. Por defecto muestra los últimos 30 días.",
        ],
        callouts: [
          {
            variant: "info",
            text: "Arriba de la grilla hay indicadores de cabecera con los totales del filtro aplicado.",
          },
        ],
      },
      {
        id: "cuando-usar",
        title: "2. Cuándo usarlo frente a Reportes",
        paragraphs: [],
        bullets: [
          "Usa Reportes cuando necesites tendencias, volúmenes y tiempos agregados (por mes, por organismo, por gestor).",
          "Usa Reportes Detallados cuando necesites identificar trámites puntuales que cumplen una condición específica (por ejemplo, todos los trámites de leasing con transformación pendientes en un organismo) para exportarlos y trabajarlos fila por fila.",
        ],
      },
      {
        id: "entrar",
        title: "3. Cómo entrar",
        paragraphs: [
          "Píldora «Reportes Detallados» del dock, visible si tu rol tiene el permiso correspondiente. Si eres Administrador de una cabeza de red, el mismo selector de alcance de Trámites te deja consultar «Toda la red» o un cliente puntual.",
        ],
      },
      {
        id: "consejos",
        title: "4. Consejos",
        paragraphs: [],
        bullets: [
          "Acota primero el rango de fechas: la grilla puede crecer rápido si tu compañía maneja mucho volumen.",
          "Exporta a Excel cuando necesites compartir el detalle fuera de la plataforma; usa el mismo permiso de exportación que el resto de reportes.",
          "Si buscas una persona puntual, el filtro por documento es más preciso que el de nombre.",
        ],
      },
    ],
  },
];
