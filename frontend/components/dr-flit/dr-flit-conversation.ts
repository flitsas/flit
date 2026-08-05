import {
  buildClientBranchPrompt,
  buildGreeting,
  buildSearchError,
  buildTramitesIntro,
  buildValidacionesIntro,
  buildValuePrompt,
  DR_FLIT_FREE_TEXT_HINT,
  getIntentById,
  type DrFlitClientBranch,
  type DrFlitIntent,
  type DrFlitIntentId,
} from "./dr-flit-intents";
import type { DrFlitTramiteResult, DrFlitValidacionResult } from "./dr-flit-types";

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
  | "loading"
  | "showing_tramites"
  | "showing_validaciones"
  | "error";

export interface DrFlitChatState {
  messages: DrFlitMessage[];
  phase: DrFlitPhase;
  pendingIntent: DrFlitIntentId | null;
  queryValue: string | null;
  showSuggestions: boolean;
  showBackToSearch: boolean;
  showClientBranch: boolean;
  tramiteResults: DrFlitTramiteResult[] | null;
  validacionResults: DrFlitValidacionResult[] | null;
  validacionesHref: string | null;
  isTyping: boolean;
  /** Rama de cliente pendiente durante loading. */
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

export function createInitialState(displayName?: string | null): DrFlitChatState {
  return {
    messages: [
      {
        id: createMessageId(),
        role: "bot",
        text: buildGreeting(displayName),
      },
    ],
    phase: "idle",
    pendingIntent: null,
    queryValue: null,
    showSuggestions: true,
    showBackToSearch: false,
    showClientBranch: false,
    tramiteResults: null,
    validacionResults: null,
    validacionesHref: null,
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
      messages: [...state.messages, userMsg, botMsg],
      phase: "awaiting_value",
      pendingIntent: intent.id,
      queryValue: null,
      showSuggestions: false,
      showBackToSearch: false,
      showClientBranch: false,
      tramiteResults: null,
      validacionResults: null,
      validacionesHref: null,
      isTyping: false,
      pendingClientBranch: null,
    },
  };
}

/** Usuario envió texto: o pide sugerencia, o rama cliente, o arranca loading de búsqueda. */
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
      showSuggestions: true,
      showBackToSearch: false,
      isTyping: false,
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
      showSuggestions: false,
      showClientBranch: true,
      showBackToSearch: true,
      tramiteResults: null,
      validacionResults: null,
      validacionesHref: null,
      isTyping: false,
      pendingClientBranch: null,
    };
  }

  return {
    ...state,
    messages: [...state.messages, userMsg],
    phase: "loading",
    queryValue: text,
    showSuggestions: false,
    showClientBranch: false,
    showBackToSearch: false,
    tramiteResults: null,
    validacionResults: null,
    validacionesHref: null,
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
    showSuggestions: false,
    showClientBranch: false,
    showBackToSearch: false,
    tramiteResults: null,
    validacionResults: null,
    validacionesHref: null,
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
    pendingIntent: null,
    pendingClientBranch: null,
    showSuggestions: false,
    showClientBranch: false,
    showBackToSearch: true,
    tramiteResults: results,
    validacionResults: null,
    validacionesHref: null,
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
    pendingIntent: null,
    pendingClientBranch: null,
    showSuggestions: false,
    showClientBranch: false,
    showBackToSearch: true,
    tramiteResults: null,
    validacionResults: results,
    validacionesHref: href,
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
    pendingIntent: null,
    pendingClientBranch: null,
    showSuggestions: false,
    showClientBranch: false,
    showBackToSearch: true,
    tramiteResults: null,
    validacionResults: null,
    validacionesHref: null,
    isTyping: false,
  };
}

export function applyBackToSearch(state: DrFlitChatState): DrFlitChatState {
  const botMsg: DrFlitMessage = {
    id: createMessageId(),
    role: "bot",
    text: "Listo. Elige otra forma de búsqueda.",
  };

  return {
    ...state,
    messages: [...state.messages, botMsg],
    phase: "idle",
    pendingIntent: null,
    queryValue: null,
    showSuggestions: true,
    showBackToSearch: false,
    showClientBranch: false,
    tramiteResults: null,
    validacionResults: null,
    validacionesHref: null,
    isTyping: false,
    pendingClientBranch: null,
  };
}

export function queryLabelForIntent(intent: DrFlitIntentId | null): string {
  if (intent === "placa") return "placa";
  if (intent === "vin") return "VIN";
  if (intent === "tramite") return "trámite";
  if (intent === "cliente") return "cliente";
  return "búsqueda";
}
