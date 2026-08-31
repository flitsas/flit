"use client";

import { BookOpen, X } from "lucide-react";
import { useEffect, useRef, type RefObject } from "react";
import {
  hasActiveConversation,
  isComposerEnabled,
  type DrFlitChatState,
} from "./dr-flit-conversation";
import {
  DR_FLIT_MANUAL_HOME_HREF,
  type DrFlitClientBranch,
  type DrFlitHelpOptionId,
  type DrFlitIntentId,
} from "./dr-flit-intents";
import { DR_FLIT_ASSETS } from "./dr-flit-assets";
import { DrFlitBackToSearch } from "./DrFlitBackToSearch";
import { DrFlitClientBranchChoices } from "./DrFlitClientBranch";
import { DrFlitComposer } from "./DrFlitComposer";
import { DrFlitEndChatButton } from "./DrFlitEndChatButton";
import { DrFlitHelpResults } from "./DrFlitHelpResults";
import { DrFlitMessageBubble } from "./DrFlitMessageBubble";
import { DrFlitSessionMenu } from "./DrFlitSessionMenu";
import { DrFlitSupportPanel } from "./DrFlitSupportPanel";
import { DrFlitTramiteResults } from "./DrFlitTramiteResults";
import { DrFlitValidacionResults } from "./DrFlitValidacionResults";
import { DrFlitValidacionesLink } from "./DrFlitValidacionesLink";

export function DrFlitChatPanel({
  open,
  panelId,
  state,
  onClose,
  onEndChat,
  onSelectIntent,
  onSelectHelpOption,
  onSelectClientBranch,
  onBackToSearch,
  onSend,
  onNavigate,
  panelRef,
  closeButtonRef,
  inputRef,
}: {
  open: boolean;
  panelId: string;
  state: DrFlitChatState;
  onClose: () => void;
  onEndChat: () => void;
  onSelectIntent: (id: DrFlitIntentId) => void;
  onSelectHelpOption: (id: DrFlitHelpOptionId) => void;
  onSelectClientBranch: (branch: DrFlitClientBranch) => void;
  onBackToSearch: () => void;
  onSend: (text: string) => void;
  onNavigate: (href: string) => void;
  panelRef: RefObject<HTMLDivElement | null>;
  closeButtonRef: RefObject<HTMLButtonElement | null>;
  inputRef: RefObject<HTMLInputElement | null>;
}) {
  const threadRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const el = threadRef.current;
    if (!el) return;
    el.scrollTop = el.scrollHeight;
  }, [
    open,
    state.messages,
    state.session,
    state.showSessionMenu,
    state.showSupportInfo,
    state.showClientBranch,
    state.tramiteResults,
    state.validacionResults,
    state.validacionesHref,
    state.helpResults,
    state.manualHomeHref,
    state.showBackToSearch,
    state.isTyping,
  ]);

  if (!open) return null;

  const composerEnabled = isComposerEnabled(state);
  const canEndChat = hasActiveConversation(state);

  return (
    <div
      ref={panelRef}
      id={panelId}
      role="dialog"
      aria-modal="false"
      aria-labelledby={`${panelId}-title`}
      className="dr-flit dr-flit-panel-enter fixed inset-y-0 right-0 z-50 flex w-full max-w-md flex-col overflow-hidden"
      style={{
        background: "var(--dr-flit-panel-bg)",
        borderTopLeftRadius: "var(--dr-flit-radius-widget)",
        borderBottomLeftRadius: "var(--dr-flit-radius-widget)",
        boxShadow: "var(--dr-flit-shadow-panel)",
        fontFamily: "var(--dr-flit-font)",
      }}
    >
        <header
          className="flex shrink-0 items-center gap-2 px-4 py-3.5"
          style={{ background: "var(--dr-flit-gradient-header)" }}
        >
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src={DR_FLIT_ASSETS.header}
            alt=""
            aria-hidden="true"
            className="h-10 w-10 shrink-0 rounded-full object-cover ring-2 ring-white/40"
            draggable={false}
          />
          <div className="min-w-0 flex-1">
            <h2
              id={`${panelId}-title`}
              className="text-base font-bold text-white leading-tight"
            >
              DR. FLIT
            </h2>
            <p className="text-xs text-white/85 leading-snug">
              Tu asistente inteligente de FLIT
            </p>
          </div>
          <button
            ref={closeButtonRef}
            type="button"
            onClick={onClose}
            aria-label="Cerrar DR. FLIT"
            title="Ocultar panel (conserva la conversación)"
            className="rounded-md p-1.5 text-white hover:bg-white/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white"
          >
            <X className="h-5 w-5" aria-hidden="true" />
          </button>
        </header>

        <div
          ref={threadRef}
          className="flex-1 min-h-0 overflow-y-auto px-4 py-4 space-y-3"
          style={{ background: "var(--dr-flit-panel-bg)" }}
          aria-live="polite"
        >
          {state.messages.map((m) => (
            <DrFlitMessageBubble key={m.id} message={m} />
          ))}

          {state.isTyping && (
            <p
              className="text-xs"
              style={{ color: "var(--dr-flit-text-secondary)" }}
            >
              DR. FLIT está escribiendo…
            </p>
          )}

          {state.showClientBranch && !state.isTyping && (
            <DrFlitClientBranchChoices onSelect={onSelectClientBranch} />
          )}

          {state.tramiteResults && !state.isTyping && (
            <DrFlitTramiteResults
              results={state.tramiteResults}
              onOpen={onNavigate}
            />
          )}

          {state.validacionResults &&
            state.validacionResults.length > 0 &&
            !state.isTyping && (
              <DrFlitValidacionResults
                results={state.validacionResults}
                onOpen={onNavigate}
              />
            )}

          {state.validacionesHref && !state.isTyping && (
            <DrFlitValidacionesLink
              href={state.validacionesHref}
              onOpen={onNavigate}
            />
          )}

          {state.helpResults && state.helpResults.length > 0 && !state.isTyping && (
            <DrFlitHelpResults results={state.helpResults} onOpen={onNavigate} />
          )}

          {state.manualHomeHref && !state.isTyping && (
            <button
              type="button"
              onClick={() => onNavigate(state.manualHomeHref ?? DR_FLIT_MANUAL_HOME_HREF)}
              className="flex w-full items-center gap-3 rounded-[var(--dr-flit-radius-card)] border p-3 text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
              style={{
                borderColor: "var(--dr-flit-border)",
                background: "var(--dr-flit-card-bg)",
                boxShadow: "var(--dr-flit-shadow-card)",
              }}
            >
              <span
                className="grid h-10 w-10 shrink-0 place-items-center rounded-full"
                style={{ background: "var(--dr-flit-icon-tint)" }}
                aria-hidden="true"
              >
                <BookOpen
                  className="h-5 w-5"
                  style={{ color: "var(--dr-flit-brand-blue)" }}
                />
              </span>
              <span className="min-w-0 flex-1">
                <span
                  className="block text-sm font-semibold"
                  style={{ color: "var(--dr-flit-brand-title)" }}
                >
                  Abrir Centro de Ayuda
                </span>
                <span
                  className="mt-0.5 block text-xs"
                  style={{ color: "var(--dr-flit-text-secondary)" }}
                >
                  Explora el manual completo
                </span>
              </span>
            </button>
          )}

          {state.showSupportInfo && !state.isTyping && (
            <DrFlitSupportPanel onOpenCase={onNavigate} />
          )}

          {state.showSessionMenu && !state.isTyping && (
            <DrFlitSessionMenu
              onSelectIntent={onSelectIntent}
              onSelectHelpOption={onSelectHelpOption}
            />
          )}

          {state.showBackToSearch && !state.isTyping && (
            <div className="pt-1">
              <DrFlitBackToSearch onBack={onBackToSearch} />
            </div>
          )}

          {canEndChat && !state.isTyping && (
            <div className="pt-2">
              <DrFlitEndChatButton onEnd={onEndChat} />
              <p
                className="mt-1.5 text-center text-[11px] leading-snug"
                style={{ color: "var(--dr-flit-text-muted)" }}
              >
                Borra esta conversación. La X solo oculta el panel.
              </p>
            </div>
          )}
        </div>

      <DrFlitComposer
        onSend={onSend}
        inputRef={inputRef}
        disabled={state.isTyping || !composerEnabled}
      />
    </div>
  );
}
