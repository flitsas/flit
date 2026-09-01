import { searchManualArticles } from "@/lib/manual/catalog";
import {
  buildClientBranchPrompt,
  buildGreeting,
  buildHelpIntro,
  buildHelpValuePrompt,
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

export function applySelectHelpOption(
  state: DrFlitChatState,
  optionId: DrFlitHelpOptionId,
): DrFlitChatState | null {
  const option = getHelpOptionById(optionId);
  if (!option) return null;

  const userMsg: DrFlitMessage = {
    id: createMessageId(),
    role: "user",
    text: option.label,
  };

  if (optionId === "necesito-ayuda") {
    const botMsg: DrFlitMessage = {
      id: createMessageId(),
      role: "bot",
      text: buildHelpValuePrompt(),
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

function applyHelpQuery(state: DrFlitChatState, text: string): DrFlitChatState {
  const hits = searchManualArticles(text, 5);
  const results: DrFlitHelpResult[] = hits.map((h) => ({
    slug: h.slug,
    title: h.title,
    audience: h.audience,
    summary: h.summary,
    href: h.href,
  }));
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
): DrFlitChatState {
  const value = state.queryValue ?? "";
  const botMsg: DrFlitMessage = {
    id: createMessageId(),
    role: "bot",
    text: buildTramitesIntro(queryLabel, value, results.length),
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
  if (intent === "tramite") return "trámite";
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
    state.helpResults != null ||
    state.manualHomeHref != null ||
    state.isTyping
  );
}
