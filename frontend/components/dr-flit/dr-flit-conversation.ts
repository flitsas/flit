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
  | "error";

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
    state.phase === "awaiting_help_query"
  );
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
