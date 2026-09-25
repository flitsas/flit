import {
  getArticleBySlug,
  NORMATIVA_RESOLUCION_SLUG,
  searchManualArticles,
  type ManualArticle,
  type ManualAudience,
  type ManualSource,
} from "@/lib/manual/catalog";
import {
  buildClientBranchPrompt,
  buildContextHelpPrompt,
  buildGreeting,
  buildHelpIntro,
  buildHelpValuePrompt,
  buildNormativaIntro,
  buildQuickManualIntro,
  buildSearchError,
  buildSupportIntro,
  buildTramitesIntro,
  buildValidacionesIntro,
  buildValuePrompt,
  DR_FLIT_FREE_TEXT_HINT,
  DR_FLIT_MANUAL_HOME_HREF,
  getHelpOptionById,
  getIntentById,
  type DrFlitClientBranch,
  type DrFlitHelpOptionId,
  type DrFlitIntent,
  type DrFlitIntentId,
  type DrFlitSession,
} from "./dr-flit-intents";
import type {
  DrFlitHelpResult,
  DrFlitTramiteResult,
  DrFlitValidacionResult,
} from "./dr-flit-types";
import type {
  DrFlitChatResponse,
  DrFlitChatTurn,
  DrFlitChatUsage,
  DrFlitCitation,
  DrFlitSupportCaseCreated,
  DrFlitSupportCaseDraft,
} from "./dr-flit-chat-types";
import { validateSupportDraft } from "./dr-flit-support-case";

export type DrFlitMessageRole = "bot" | "user";

export interface DrFlitMessage {
  id: string;
  role: DrFlitMessageRole;
  text: string;
}

export type DrFlitPhase =
  | "idle"
  | "awaiting_value"
  | "awaiting_client_branch"
  | "awaiting_help_query"
  | "loading"
  | "showing_tramites"
  | "showing_validaciones"
  | "showing_help"
  | "showing_support"
  | "error"
  // ── Épica #12718 (ADR-0060 §11) — chat con LLM ──
  /** Esperando la respuesta de POST /dr-flit/chat. */
  | "chat_loading"
  /** Respuesta del LLM (status ok), con o sin citas del manual. */
  | "showing_chat_reply"
  // ── Feature #12917 — caso de soporte ──
  /** Formulario del caso abierto (prellenado). */
  | "collecting_support_case"
  /** Resumen + «Confirmar y radicar caso»: único punto que llama a POST /support-cases. */
  | "confirming_support_case"
  /** POST /support-cases en curso. */
  | "submitting_support_case"
  /** «Tu caso #N quedó radicado». */
  | "support_case_created"
  /** El sistema de soporte no respondió: canales estáticos y reintento sin perder el formulario. */
  | "support_case_error";

export interface DrFlitChatState {
  messages: DrFlitMessage[];
  session: DrFlitSession;
  phase: DrFlitPhase;
  pendingIntent: DrFlitIntentId | null;
  queryValue: string | null;
  showSessionMenu: boolean;
  showSupportInfo: boolean;
  showBackToSearch: boolean;
  showClientBranch: boolean;
  tramiteResults: DrFlitTramiteResult[] | null;
  validacionResults: DrFlitValidacionResult[] | null;
  validacionesHref: string | null;
  /** HU-C — atajo a «Historial por placa» tras una búsqueda por placa con resultados. */
  historialPlacaHref?: string | null;
  helpResults: DrFlitHelpResult[] | null;
  manualHomeHref: string | null;
  isTyping: boolean;
  pendingClientBranch: DrFlitClientBranch | null;
  /**
   * HU #12926/#12928 — uso del tope diario que devolvió la última respuesta del chat. Opcional: las
   * conversaciones guardadas en sessionStorage antes de la épica no lo traen.
   */
  chatUsage?: DrFlitChatUsage | null;
  /** Feature #12917 — formulario del caso en curso (solo ids de adjuntos, nunca binarios). */
  supportDraft?: DrFlitSupportCaseDraft | null;
  /** Feature #12917 — resultado de la radicación. */
  supportResult?: DrFlitSupportCaseCreated | null;
  /** Feature #12917 — motivo del último fallo al radicar. */
  supportError?: string | null;
}

let messageSeq = 0;

export function createMessageId(): string {
  messageSeq += 1;
  return `dr-flit-msg-${messageSeq}`;
}

export function resetMessageIdSeq(): void {
  messageSeq = 0;
}

/** Evita colisiones de id al hidratar conversación desde sessionStorage. */
export function syncMessageIdSeqFromState(state: DrFlitChatState): void {
  let max = 0;
  for (const m of state.messages) {
    const n = Number(String(m.id).replace(/^dr-flit-msg-/, ""));
    if (Number.isFinite(n)) max = Math.max(max, n);
  }
  if (max > messageSeq) messageSeq = max;
}

function idleMenuFlags(): Pick<
  DrFlitChatState,
  "showSessionMenu" | "showSupportInfo"
> {
  return {
    showSessionMenu: true,
    showSupportInfo: false,
  };
}

function clearActionState(): Omit<
  DrFlitChatState,
  "messages" | "session" | "phase" | "showSessionMenu" | "showSupportInfo" | "showBackToSearch"
> {
  return {
    pendingIntent: null,
    queryValue: null,
    showClientBranch: false,
    tramiteResults: null,
    validacionResults: null,
    validacionesHref: null,
    historialPlacaHref: null,
    helpResults: null,
    manualHomeHref: null,
    isTyping: false,
    pendingClientBranch: null,
  };
}

export function createInitialState(displayName?: string | null): DrFlitChatState {
  return {
    messages: [
      {
        id: createMessageId(),
        role: "bot",
        text: buildGreeting(displayName),
      },
    ],
    session: "gestion",
    phase: "idle",
    pendingIntent: null,
    queryValue: null,
    showSessionMenu: true,
    showSupportInfo: false,
    showBackToSearch: false,
    showClientBranch: false,
    tramiteResults: null,
    validacionResults: null,
    validacionesHref: null,
    historialPlacaHref: null,
    helpResults: null,
    manualHomeHref: null,
    isTyping: false,
    pendingClientBranch: null,
  };
}

export interface SelectIntentResult {
  next: DrFlitChatState;
  intent: DrFlitIntent;
}

export function applySelectIntent(
  state: DrFlitChatState,
  intentId: DrFlitIntentId,
): SelectIntentResult | null {
  const intent = getIntentById(intentId);
  if (!intent) return null;

  const userMsg: DrFlitMessage = {
    id: createMessageId(),
    role: "user",
    text: intent.label,
  };
  const botMsg: DrFlitMessage = {
    id: createMessageId(),
    role: "bot",
    text: buildValuePrompt(intent),
  };

  return {
    intent,
    next: {
      ...state,
      ...clearActionState(),
      session: "gestion",
      messages: [...state.messages, userMsg, botMsg],
      phase: "awaiting_value",
      pendingIntent: intent.id,
      showSessionMenu: false,
      showSupportInfo: false,
      showBackToSearch: false,
    },
  };
}

function withSource(
  base: DrFlitHelpResult,
  sources: ManualSource[] | undefined,
  primarySource: boolean | undefined,
): DrFlitHelpResult {
  const pdf = sources?.find((s) => s.kind === "pdf") ?? sources?.[0];
  return {
    ...base,
    ...(pdf
      ? {
          sourceHref: pdf.href,
          sourceLabel: pdf.kind === "pdf" ? "Abrir la norma (PDF)" : "Abrir la fuente",
        }
      : {}),
    ...(primarySource ? { primarySource: true } : {}),
  };
}

export function toHelpResult(article: ManualArticle): DrFlitHelpResult {
  return withSource(
    {
      slug: article.slug,
      title: article.title,
      audience: article.audience,
      summary: article.summary,
      href: `/manual/${article.slug}`,
    },
    article.sources,
    article.primarySource,
  );
}

export interface SelectHelpOptions {
  /**
   * HU-G — artículo del módulo donde está el usuario (`resolveContextArticle`). Se ofrece como
   * primer chip antes de que escriba; si escribe, la búsqueda lo reemplaza.
   */
  contextArticle?: ManualArticle | null;
}

export function applySelectHelpOption(
  state: DrFlitChatState,
  optionId: DrFlitHelpOptionId,
  options: SelectHelpOptions = {},
): DrFlitChatState | null {
  const option = getHelpOptionById(optionId);
  if (!option) return null;

  const userMsg: DrFlitMessage = {
    id: createMessageId(),
    role: "user",
    text: option.label,
  };

  if (optionId === "necesito-ayuda") {
    const contextArticle = options.contextArticle ?? null;
    const botMsg: DrFlitMessage = {
      id: createMessageId(),
      role: "bot",
      text: contextArticle
        ? buildContextHelpPrompt(contextArticle.title)
        : buildHelpValuePrompt(),
    };
    return {
      ...state,
      ...clearActionState(),
      session: "ayuda",
      messages: [...state.messages, userMsg, botMsg],
      phase: "awaiting_help_query",
      showSessionMenu: false,
      showSupportInfo: false,
      showBackToSearch: false,
      helpResults: contextArticle ? [toHelpResult(contextArticle)] : null,
    };
  }

  if (optionId === "normativa") {
    // Fuente principal: la norma que avala la plataforma. Se ofrece el resumen por temas del manual
    // y, dentro de la tarjeta, el PDF completo.
    const article = getArticleBySlug(NORMATIVA_RESOLUCION_SLUG);
    const botMsg: DrFlitMessage = {
      id: createMessageId(),
      role: "bot",
      text: buildNormativaIntro(),
    };
    return {
      ...state,
      ...clearActionState(),
      session: "ayuda",
      messages: [...state.messages, userMsg, botMsg],
      phase: "showing_help",
      showSessionMenu: false,
      showSupportInfo: false,
      showBackToSearch: true,
      helpResults: article ? [toHelpResult(article)] : null,
      manualHomeHref: article ? null : DR_FLIT_MANUAL_HOME_HREF,
    };
  }

  const botMsg: DrFlitMessage = {
    id: createMessageId(),
    role: "bot",
    text: buildSupportIntro(),
  };
  return {
    ...state,
    ...clearActionState(),
    session: "ayuda",
    messages: [...state.messages, userMsg, botMsg],
    phase: "showing_support",
    showSessionMenu: false,
    showSupportInfo: true,
    showBackToSearch: true,
  };
}

export interface UserTextOptions {
  /** HU-F — audiencias del manual visibles para el perfil (`visibleAudiences`). Sin ellas, todo. */
  helpAudiences?: readonly ManualAudience[];
  /**
   * HU #12926 — el texto libre fuera de un flujo guiado va al chat con LLM. Apagado (flag
   * `NEXT_PUBLIC_DR_FLIT_CHAT_ENABLED=false`), se conserva el comportamiento previo: pedir que elija
   * una opción del menú.
   */
  chatEnabled?: boolean;
}

function applyHelpQuery(
  state: DrFlitChatState,
  text: string,
  options: UserTextOptions,
): DrFlitChatState {
  const hits = searchManualArticles(text, 5, { audiences: options.helpAudiences });
  const results: DrFlitHelpResult[] = hits.map((h) =>
    withSource(
      { slug: h.slug, title: h.title, audience: h.audience, summary: h.summary, href: h.href },
      h.sources,
      h.primarySource,
    ),
  );
  const botMsg: DrFlitMessage = {
    id: createMessageId(),
    role: "bot",
    text: buildHelpIntro(text, results.length),
  };
  return {
    ...state,
    messages: [...state.messages, botMsg],
    phase: "showing_help",
    session: "ayuda",
    queryValue: text,
    showSessionMenu: false,
    showSupportInfo: false,
    showBackToSearch: true,
    tramiteResults: null,
    validacionResults: null,
    validacionesHref: null,
    historialPlacaHref: null,
    helpResults: results,
    manualHomeHref: results.length === 0 ? DR_FLIT_MANUAL_HOME_HREF : null,
    isTyping: false,
    pendingIntent: null,
    pendingClientBranch: null,
    showClientBranch: false,
  };
}

export function applyUserText(
  state: DrFlitChatState,
  rawText: string,
  options: UserTextOptions = {},
): DrFlitChatState {
  const text = rawText.trim();
  if (!text) return state;

  const userMsg: DrFlitMessage = {
    id: createMessageId(),
    role: "user",
    text,
  };

  if (state.phase === "awaiting_help_query") {
    return applyHelpQuery(
      { ...state, messages: [...state.messages, userMsg] },
      text,
      options,
    );
  }

  if ((state.phase !== "awaiting_value" || !state.pendingIntent) && options.chatEnabled) {
    return applyChatSend({ ...state, messages: [...state.messages, userMsg] }, text);
  }

  if (state.phase !== "awaiting_value" || !state.pendingIntent) {
    const botMsg: DrFlitMessage = {
      id: createMessageId(),
      role: "bot",
      text: DR_FLIT_FREE_TEXT_HINT,
    };
    return {
      ...state,
      messages: [...state.messages, userMsg, botMsg],
      phase: "idle",
      showBackToSearch: false,
      isTyping: false,
      ...idleMenuFlags(),
    };
  }

  if (state.pendingIntent === "cliente") {
    const botMsg: DrFlitMessage = {
      id: createMessageId(),
      role: "bot",
      text: buildClientBranchPrompt(text),
    };
    return {
      ...state,
      messages: [...state.messages, userMsg, botMsg],
      phase: "awaiting_client_branch",
      pendingIntent: "cliente",
      queryValue: text,
      showSessionMenu: false,
      showSupportInfo: false,
      showClientBranch: true,
      showBackToSearch: true,
      tramiteResults: null,
      validacionResults: null,
      validacionesHref: null,
      historialPlacaHref: null,
      helpResults: null,
      manualHomeHref: null,
      isTyping: false,
      pendingClientBranch: null,
    };
  }

  return {
    ...state,
    messages: [...state.messages, userMsg],
    phase: "loading",
    queryValue: text,
    showSessionMenu: false,
    showSupportInfo: false,
    showClientBranch: false,
    showBackToSearch: false,
    tramiteResults: null,
    validacionResults: null,
    validacionesHref: null,
    historialPlacaHref: null,
    helpResults: null,
    manualHomeHref: null,
    isTyping: true,
    pendingClientBranch: null,
  };
}

export function applyClientBranch(
  state: DrFlitChatState,
  branch: DrFlitClientBranch,
): DrFlitChatState {
  if (state.phase !== "awaiting_client_branch" || !state.queryValue) {
    return state;
  }

  const branchLabel =
    branch === "tramites" ? "Ver trámites" : "Ver validación de identidad";

  const userMsg: DrFlitMessage = {
    id: createMessageId(),
    role: "user",
    text: branchLabel,
  };

  return {
    ...state,
    messages: [...state.messages, userMsg],
    phase: "loading",
    showSessionMenu: false,
    showSupportInfo: false,
    showClientBranch: false,
    showBackToSearch: false,
    tramiteResults: null,
    validacionResults: null,
    validacionesHref: null,
    historialPlacaHref: null,
    helpResults: null,
    manualHomeHref: null,
    isTyping: true,
    pendingClientBranch: branch,
  };
}

export function applyTramitesSuccess(
  state: DrFlitChatState,
  queryLabel: string,
  results: DrFlitTramiteResult[],
  /** Total del universo filtrado (HU #12104); por defecto, los que llegaron. */
  total: number = results.length,
  /** HU-C — atajo a «Historial por placa»; solo se muestra si hubo resultados. */
  historialPlacaHref: string | null = null,
): DrFlitChatState {
  const value = state.queryValue ?? "";
  const botMsg: DrFlitMessage = {
    id: createMessageId(),
    role: "bot",
    text: buildTramitesIntro(queryLabel, value, results.length, total),
  };
  return {
    ...state,
    messages: [...state.messages, botMsg],
    phase: "showing_tramites",
    session: "gestion",
    pendingIntent: null,
    pendingClientBranch: null,
    showSessionMenu: false,
    showSupportInfo: false,
    showClientBranch: false,
    showBackToSearch: true,
    tramiteResults: results,
    validacionResults: null,
    validacionesHref: null,
    historialPlacaHref: results.length > 0 ? historialPlacaHref : null,
    helpResults: null,
    manualHomeHref: null,
    isTyping: false,
  };
}

export function applyValidacionesSuccess(
  state: DrFlitChatState,
  results: DrFlitValidacionResult[],
): DrFlitChatState {
  const cliente = state.queryValue ?? "";
  const botMsg: DrFlitMessage = {
    id: createMessageId(),
    role: "bot",
    text: buildValidacionesIntro(cliente, results.length),
  };
  const href = `/?m=validaciones&q=${encodeURIComponent(cliente)}`;
  return {
    ...state,
    messages: [...state.messages, botMsg],
    phase: "showing_validaciones",
    session: "gestion",
    pendingIntent: null,
    pendingClientBranch: null,
    showSessionMenu: false,
    showSupportInfo: false,
    showClientBranch: false,
    showBackToSearch: true,
    tramiteResults: null,
    validacionResults: results,
    validacionesHref: href,
    historialPlacaHref: null,
    helpResults: null,
    manualHomeHref: null,
    isTyping: false,
  };
}

export function applySearchFailure(
  state: DrFlitChatState,
  errorMessage: string,
): DrFlitChatState {
  const botMsg: DrFlitMessage = {
    id: createMessageId(),
    role: "bot",
    text: buildSearchError(errorMessage),
  };
  return {
    ...state,
    messages: [...state.messages, botMsg],
    phase: "error",
    session: "gestion",
    pendingIntent: null,
    pendingClientBranch: null,
    showSessionMenu: false,
    showSupportInfo: false,
    showClientBranch: false,
    showBackToSearch: true,
    tramiteResults: null,
    validacionResults: null,
    validacionesHref: null,
    historialPlacaHref: null,
    helpResults: null,
    manualHomeHref: null,
    isTyping: false,
  };
}

export function applyBackToSearch(state: DrFlitChatState): DrFlitChatState {
  const botMsg: DrFlitMessage = {
    id: createMessageId(),
    role: "bot",
    text: "Listo. Elige otra opción de Gestión o Ayuda.",
  };

  return {
    ...state,
    messages: [...state.messages, botMsg],
    phase: "idle",
    ...clearActionState(),
    showBackToSearch: false,
    ...idleMenuFlags(),
  };
}

export function queryLabelForIntent(intent: DrFlitIntentId | null): string {
  if (intent === "placa") return "placa";
  if (intent === "vin") return "VIN";
  if (intent === "tramite") return "radicado";
  if (intent === "cliente") return "cliente";
  return "búsqueda";
}

export function isComposerEnabled(state: DrFlitChatState): boolean {
  return (
    state.phase === "idle" ||
    state.phase === "awaiting_value" ||
    state.phase === "awaiting_help_query" ||
    state.phase === "showing_chat_reply"
  );
}

// ── Épica #12718 (ADR-0060 §11) — chat con LLM ─────────────────────────────────────────────────

/** Máximo de turnos previos que acepta el backend (`history.maxItems`). */
export const DR_FLIT_CHAT_HISTORY_LIMIT = 12;

/** Máximo de caracteres por turno (`maxLength` del contrato). */
export const DR_FLIT_CHAT_TURN_MAX_LENGTH = 2000;

/**
 * HU #12926 — el texto libre entra a `chat_loading`; el efecto de `useDrFlitChat` llama al backend.
 * `state` ya trae el mensaje del usuario al final.
 */
export function applyChatSend(state: DrFlitChatState, text: string): DrFlitChatState {
  return {
    ...state,
    ...clearActionState(),
    phase: "chat_loading",
    queryValue: text,
    showSessionMenu: false,
    showSupportInfo: false,
    showBackToSearch: false,
    isTyping: true,
  };
}

/**
 * HU #12926 AC3 — turnos previos al mensaje en curso, acotados a los últimos
 * {@link DR_FLIT_CHAT_HISTORY_LIMIT}: el payload no crece con la conversación. El último mensaje del
 * usuario (el que se está enviando) no va aquí: viaja como `message`.
 */
export function buildChatHistory(state: DrFlitChatState): DrFlitChatTurn[] {
  const previous = state.messages.slice(0, -1);
  return previous
    .filter((m) => m.text.trim().length > 0)
    .slice(-DR_FLIT_CHAT_HISTORY_LIMIT)
    .map((m) => ({
      role: m.role === "user" ? "user" : "assistant",
      text: m.text.slice(0, DR_FLIT_CHAT_TURN_MAX_LENGTH),
    }));
}

/**
 * Tarjeta del manual a partir de una cita del LLM. El backend ya validó que el slug existe; del
 * catálogo local salen la audiencia y el resumen que la tarjeta muestra.
 */
export function citationToHelpResult(citation: DrFlitCitation): DrFlitHelpResult {
  const article = getArticleBySlug(citation.slug);
  if (article) return toHelpResult(article);
  return {
    slug: citation.slug,
    title: citation.title,
    audience: "Todos",
    summary: "",
    href: citation.href,
    ...(citation.sourceHref ? { sourceHref: citation.sourceHref, sourceLabel: "Abrir la fuente" } : {}),
    ...(citation.primarySource ? { primarySource: true } : {}),
  };
}

/**
 * Respuesta del LLM con `status: ok`, bifurcada por intención (ADR-0060 §11):
 * <ul>
 *   <li>`duda` (HU #12926) — reply + tarjetas de cita;</li>
 *   <li>`gestion` (HU #12927) — a la sesión Gestión de siempre: con sugerencia, directo a pedir el
 *   valor de ese tipo de búsqueda; sin ella, el menú para que el usuario elija;</li>
 *   <li>`no_claro` (HU #12927) — la pregunta de seguimiento del modelo, y se sigue escribiendo;</li>
 *   <li>`soporte` — canales de soporte (el formulario de caso llega con la Feature #12917).</li>
 * </ul>
 */
export function applyChatSuccess(
  state: DrFlitChatState,
  response: DrFlitChatResponse,
  /** HU #12929 — borrador prellenado: con él, «soporte» abre el formulario del caso en el chat. */
  supportDraft?: DrFlitSupportCaseDraft | null,
): DrFlitChatState {
  const botMsg: DrFlitMessage = { id: createMessageId(), role: "bot", text: response.reply };

  if (response.intent === "gestion") {
    return applyChatGestion({ ...state, messages: [...state.messages, botMsg], chatUsage: response.usage }, response);
  }

  if (response.intent === "soporte" && supportDraft) {
    return applyOpenSupportCase(
      { ...state, messages: [...state.messages, botMsg], chatUsage: response.usage },
      supportDraft,
    );
  }

  if (response.intent === "soporte") {
    return {
      ...state,
      ...clearActionState(),
      messages: [...state.messages, botMsg],
      phase: "showing_support",
      session: "ayuda",
      showSessionMenu: false,
      showSupportInfo: true,
      showBackToSearch: true,
      chatUsage: response.usage,
    };
  }

  const helpResults = response.intent === "duda" ? response.citations.map(citationToHelpResult) : [];
  return {
    ...state,
    ...clearActionState(),
    messages: [...state.messages, botMsg],
    phase: "showing_chat_reply",
    session: "ayuda",
    queryValue: state.queryValue,
    showSessionMenu: false,
    showSupportInfo: false,
    showBackToSearch: true,
    helpResults: helpResults.length > 0 ? helpResults : null,
    chatUsage: response.usage,
  };
}

/**
 * HU #12928 — margen de UI para avisar que quedan pocos mensajes con el asistente hoy. Es solo
 * presentación (ajustable aquí): el tope real lo decide y lo cuenta el backend (`usage`).
 */
export const DR_FLIT_USAGE_WARNING_MARGIN = 5;

/** Mensajes que quedan hoy según el último `usage`, o `null` si no hay dato. */
export function remainingChatMessages(state: DrFlitChatState): number | null {
  const usage = state.chatUsage;
  if (!usage || usage.dailyLimit <= 0) return null;
  return Math.max(0, usage.dailyLimit - usage.messagesUsedToday);
}

/** HU #12928 AC1 — hay que avisar: quedan pocos pero todavía alguno. */
export function shouldWarnChatUsage(state: DrFlitChatState): boolean {
  const remaining = remainingChatMessages(state);
  return remaining !== null && remaining > 0 && remaining <= DR_FLIT_USAGE_WARNING_MARGIN;
}

/**
 * HU #12928 AC2 — tope diario alcanzado: el mensaje amigable del backend y de vuelta al menú, que
 * funciona completo sin LLM. No se bloquea el compositor: al día siguiente (hora Colombia) el backend
 * vuelve a aceptar mensajes sin que el usuario haga nada (AC3).
 */
export function applyChatRateLimited(
  state: DrFlitChatState,
  response: DrFlitChatResponse,
): DrFlitChatState {
  const botMsg: DrFlitMessage = { id: createMessageId(), role: "bot", text: response.reply };
  return {
    ...state,
    ...clearActionState(),
    messages: [...state.messages, botMsg],
    phase: "idle",
    session: "gestion",
    showBackToSearch: false,
    ...idleMenuFlags(),
    chatUsage: response.usage,
  };
}

/**
 * HU #12927 AC1/AC2 — intención de búsqueda. Reutiliza la sesión Gestión existente sin fase nueva: con
 * `suggestGestionIntent` pide directamente el valor (mismo estado que deja `applySelectIntent`); sin
 * sugerencia, deja el menú para que el usuario elija el tipo de búsqueda.
 */
function applyChatGestion(state: DrFlitChatState, response: DrFlitChatResponse): DrFlitChatState {
  const suggestion = response.suggestGestionIntent;
  const selected = suggestion ? applySelectIntent(state, suggestion) : null;
  if (selected) {
    // applySelectIntent simula el clic del usuario en el menú («Buscar por placa»); aquí el usuario no
    // hizo clic, así que se conserva solo la pregunta del bot por el valor.
    const [, prompt] = selected.next.messages.slice(-2);
    return {
      ...selected.next,
      messages: [...state.messages, ...(prompt ? [prompt] : [])],
      showBackToSearch: true,
      chatUsage: state.chatUsage,
    };
  }

  return {
    ...state,
    ...clearActionState(),
    phase: "idle",
    session: "gestion",
    showSessionMenu: true,
    showSupportInfo: false,
    showBackToSearch: false,
  };
}

/**
 * HU #12926 AC2 — el LLM no respondió (degradado, error HTTP o de red): responde el buscador local
 * del manual, el mismo de siempre, con una nota de que es una respuesta rápida. El menú sigue ahí.
 */
export function applyChatDegraded(
  state: DrFlitChatState,
  options: UserTextOptions = {},
  usage: DrFlitChatUsage | null = state.chatUsage ?? null,
): DrFlitChatState {
  const text = state.queryValue ?? "";
  const next = applyHelpQuery(state, text, options);
  const count = next.helpResults?.length ?? 0;
  const intro = next.messages.at(-1);
  return {
    ...next,
    messages: [
      ...next.messages.slice(0, -1),
      { id: intro?.id ?? createMessageId(), role: "bot", text: buildQuickManualIntro(text, count) },
    ],
    chatUsage: usage,
  };
}

/** True si hay una interacción en curso (no el menú principal Gestión/Ayuda). */
export function hasActiveConversation(state: DrFlitChatState): boolean {
  // En el menú raíz la interacción ya se considera cerrada: no mostrar “Terminar chat”.
  if (state.phase === "idle" && state.showSessionMenu && !state.isTyping) {
    return false;
  }
  return (
    state.phase !== "idle" ||
    !state.showSessionMenu ||
    state.showBackToSearch ||
    state.showSupportInfo ||
    state.showClientBranch ||
    state.tramiteResults != null ||
    state.validacionResults != null ||
    state.historialPlacaHref != null ||
    state.helpResults != null ||
    state.manualHomeHref != null ||
    state.isTyping
  );
}

// ── Feature #12917 — caso de soporte (ADR-0060 §7.2 y §11) ─────────────────────────────────────

/**
 * HU #12929 AC1 — abre el formulario del caso dentro del chat con el borrador prellenado. Llega por la
 * intención «soporte» del LLM o por «Generar un caso de soporte» del panel de soporte.
 */
export function applyOpenSupportCase(
  state: DrFlitChatState,
  draft: DrFlitSupportCaseDraft,
): DrFlitChatState {
  const botMsg: DrFlitMessage = {
    id: createMessageId(),
    role: "bot",
    text: "Completa estos datos y te muestro un resumen antes de radicar el caso. Ya llené lo que la plataforma sabe de ti.",
  };
  return {
    ...state,
    ...clearActionState(),
    messages: [...state.messages, botMsg],
    phase: "collecting_support_case",
    session: "ayuda",
    showSessionMenu: false,
    showSupportInfo: false,
    showBackToSearch: false,
    supportDraft: draft,
    supportResult: null,
    supportError: null,
  };
}

/** Cada cambio del formulario queda en el estado (y en sessionStorage): no se pierde al navegar. */
export function applyUpdateSupportDraft(
  state: DrFlitChatState,
  draft: DrFlitSupportCaseDraft,
): DrFlitChatState {
  return { ...state, supportDraft: draft };
}

/**
 * HU #12929 AC2 — «Continuar»: con campos faltantes no avanza (el formulario los marca) y no llama a
 * ningún endpoint; completo, pasa al resumen de confirmación.
 */
export function applyContinueSupportCase(state: DrFlitChatState): DrFlitChatState {
  const draft = state.supportDraft;
  if (!draft || Object.keys(validateSupportDraft(draft)).length > 0) return state;
  return { ...state, phase: "confirming_support_case", supportError: null };
}

/** Volver del resumen al formulario para corregir, sin perder lo escrito. */
export function applyEditSupportCase(state: DrFlitChatState): DrFlitChatState {
  if (!state.supportDraft) return state;
  return { ...state, phase: "collecting_support_case" };
}

/** Cancelar el caso: se descarta el borrador y se vuelve al menú. */
export function applyCancelSupportCase(state: DrFlitChatState): DrFlitChatState {
  const botMsg: DrFlitMessage = {
    id: createMessageId(),
    role: "bot",
    text: "Listo, no radiqué ningún caso. Elige otra opción de Gestión o Ayuda.",
  };
  return {
    ...state,
    ...clearActionState(),
    messages: [...state.messages, botMsg],
    phase: "idle",
    showBackToSearch: false,
    ...idleMenuFlags(),
    supportDraft: null,
    supportResult: null,
    supportError: null,
  };
}
