// Asistente Dr. FLIT SIMULADO (supuesto S4): sin backend, sin IA y sin datos personales. Módulo puro y determinista: dado un
// texto devuelve una respuesta fija por tema. Las respuestas orientan sobre el funcionamiento del portal y NO prometen pago,
// aprobación ni plazos; tampoco contienen texto legal ni tarifas. Las únicas listas (trámites y organismos) las recibe de
// quien lo monta, que las toma del BFF; aquí no se escribe ninguna a mano.

export const SALUDO = "¡Hola! Soy DR. FLIT, tu asistente virtual. ¿En qué puedo ayudarte?";
export const SIN_INFORMACION = "Por ahora no tengo esa información; escribe a soporte.";
export const AVISO_DEMOSTRACION = "Asistente de demostración: no usa inteligencia artificial. No escribas datos personales (cédula, placa, correo, teléfono).";

/** Listas reales del portal (las del selector de la landing). `undefined` mientras cargan o si fallaron. */
export interface ContextoChat {
  tipos?: readonly string[];
  organismos?: readonly string[];
  /** Canal de soporte publicado en la configuración (HU #14073). Sin él no se inventa ninguno. */
  soporte?: { correo?: string | null; telefono?: string | null; horario?: string | null } | null;
}

/** Respuesta por defecto: si hay canal de soporte publicado lo menciona; si no, el texto de siempre. */
export function textoSinInformacion(contexto: ContextoChat = {}): string {
  const s = contexto.soporte;
  const canales = [s?.correo ? `escribe a ${s.correo}` : null, s?.telefono ? `llama al ${s.telefono}` : null].filter((c): c is string => c !== null);
  if (canales.length === 0) return SIN_INFORMACION;
  const horario = s?.horario ? ` (${s.horario})` : "";
  return `Por ahora no tengo esa información; ${canales.join(" o ")}${horario}.`;
}

export interface Tema {
  id: string;
  /** Pregunta que se ofrece como botón. */
  pregunta: string;
  patron: RegExp;
  /** Texto plano (sin markdown); recibe las listas reales. */
  respuesta: (contexto: ContextoChat) => string;
  relacionados: readonly string[];
}

function lista(titulo: string, items: readonly string[] | undefined, faltante: string): string {
  if (!items) return faltante;
  if (items.length === 0) return `${titulo}: por ahora no hay ninguno habilitado.`;
  return `${titulo}: ${items.join(", ")}.`;
}

export const TEMAS: readonly Tema[] = [
  {
    id: "iniciar",
    pregunta: "¿Cómo inicio mi trámite?",
    patron: /inici|empez|comenz|solicit|hacer un tramite|realizar un tramite/,
    respuesta: () =>
      "En «Iniciar mi trámite» eliges el tipo de trámite y el organismo de tránsito, indicas tus datos y aceptas el tratamiento de datos personales. Luego el portal te guía paso a paso.",
    relacionados: ["documentos", "consultar"],
  },
  {
    id: "documentos",
    pregunta: "¿Qué documentos necesito?",
    patron: /document|requisit|papel|soporte|adjunt|necesito llevar/,
    respuesta: () =>
      "Los documentos dependen del tipo de trámite y del organismo de tránsito. Al elegirlos en «Iniciar mi trámite» el portal te muestra los requisitos y luego los cargas en el paso de documentos digitales.",
    relacionados: ["iniciar", "identidad"],
  },
  {
    id: "identidad",
    pregunta: "¿Cómo es la validación de identidad?",
    patron: /biometr|identidad|rostro|foto|cedula|selfie/,
    respuesta: () =>
      "Es uno de los pasos del trámite. Recibes un enlace seguro para tomar una fotografía de tu rostro y de tu documento. Busca buena luz, rostro descubierto y el documento enfocado y sin reflejos.",
    relacionados: ["documentos", "seguridad"],
  },
  {
    id: "tiempo",
    pregunta: "¿Cuánto tarda mi trámite?",
    patron: /cuanto tarda|demora|tiempo|plazo|cuando (me )?(llega|respond|aprueb)|dias|horas|aprob/,
    respuesta: () =>
      "El tiempo lo define cada organismo de tránsito y no puedo asegurarte un plazo ni una aprobación. Puedes ver el estado de tu solicitud en «Consultar mi trámite».",
    relacionados: ["consultar"],
  },
  {
    id: "consultar",
    pregunta: "¿Cómo consulto mi trámite?",
    patron: /consult|estado|seguimiento|radicado|donde va|como va/,
    respuesta: () =>
      "Usa «Consultar mi trámite» en la página principal. Necesitarás la placa y el número de radicado o la cédula con la que lo iniciaste.",
    relacionados: ["tiempo", "iniciar"],
  },
  {
    id: "seguridad",
    pregunta: "¿Mis datos están seguros?",
    patron: /segur|dato|privacidad|habeas|confiden|proteg|personal/,
    respuesta: () =>
      "Para iniciar un trámite pedimos tu autorización para el tratamiento de datos personales. Este chat es una demostración: lo que escribes aquí no se guarda y no debes escribir datos personales.",
    relacionados: ["identidad", "iniciar"],
  },
  {
    id: "tramites",
    pregunta: "¿Qué trámites puedo realizar?",
    patron: /que tramites|tipos? de tramite|tramites (habilitados|disponibles)|que (puedo|se puede) hacer/,
    respuesta: ({ tipos }) => lista("Trámites habilitados", tipos, "No pude cargar la lista de trámites en este momento. Revisa el selector «Tipo de trámite» de la página principal."),
    relacionados: ["iniciar", "organismos"],
  },
  {
    id: "organismos",
    pregunta: "¿En qué organismos puedo radicar?",
    patron: /organismo|donde (puedo )?(radic|hacer)|ciudad|municipio|secretaria|transito de/,
    respuesta: ({ organismos }) => lista("Organismos de tránsito habilitados", organismos, "No pude cargar la lista de organismos en este momento. La encuentras en el panel «Organismos de tránsito integrados»."),
    relacionados: ["tramites", "iniciar"],
  },
];

/** Preguntas que se ofrecen al abrir el chat (las de los temas, en el orden de `TEMAS`). */
export const PREGUNTAS_SUGERIDAS: readonly string[] = TEMAS.map((t) => t.pregunta);

export function normalizar(texto: string): string {
  return texto.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase().trim();
}

/** Tema que mejor responde al texto; el orden de `TEMAS` desempata. `null` si ninguno encaja. */
export function buscarTema(texto: string): Tema | null {
  const limpio = normalizar(texto);
  if (!limpio) return null;
  // Las preguntas ofrecidas como botón se reconocen de forma exacta; evita que «¿Qué trámites puedo realizar?» caiga en «iniciar».
  const exacto = TEMAS.find((t) => normalizar(t.pregunta) === limpio);
  if (exacto) return exacto;
  const especificos = ["tramites", "organismos"];
  const primero = TEMAS.filter((t) => especificos.includes(t.id)).find((t) => t.patron.test(limpio));
  return primero ?? TEMAS.find((t) => t.patron.test(limpio)) ?? null;
}

export interface Respuesta {
  texto: string;
  sugerencias: readonly string[];
}

export function responder(texto: string, contexto: ContextoChat = {}): Respuesta {
  const tema = buscarTema(texto);
  if (!tema) return { texto: textoSinInformacion(contexto), sugerencias: PREGUNTAS_SUGERIDAS.slice(0, 3) };
  const sugerencias = tema.relacionados.map((id) => TEMAS.find((t) => t.id === id)?.pregunta).filter((p): p is string => Boolean(p));
  return { texto: tema.respuesta(contexto), sugerencias };
}
