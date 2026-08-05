"use client";

import { X } from "lucide-react";
import { useEffect, useRef, type RefObject } from "react";
import type { DrFlitChatState } from "./dr-flit-conversation";
import type { DrFlitClientBranch, DrFlitIntentId } from "./dr-flit-intents";
import { DR_FLIT_ASSETS } from "./dr-flit-assets";
import { DrFlitBackToSearch } from "./DrFlitBackToSearch";
import { DrFlitClientBranchChoices } from "./DrFlitClientBranch";
import { DrFlitComposer } from "./DrFlitComposer";
import { DrFlitMessageBubble } from "./DrFlitMessageBubble";
import { DrFlitSuggestions } from "./DrFlitSuggestions";
import { DrFlitTramiteResults } from "./DrFlitTramiteResults";
import { DrFlitValidacionResults } from "./DrFlitValidacionResults";
import { DrFlitValidacionesLink } from "./DrFlitValidacionesLink";

export function DrFlitChatPanel({
  open,
  panelId,
  state,
  onClose,
  onSelectIntent,
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
  onSelectIntent: (id: DrFlitIntentId) => void;
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
    state.showSuggestions,
    state.showClientBranch,
    state.tramiteResults,
    state.validacionResults,
    state.validacionesHref,
    state.showBackToSearch,
    state.isTyping,
  ]);

  if (!open) return null;

  const composerEnabled =
    state.phase === "awaiting_value" || state.phase === "idle";

  return (
    <div className="dr-flit fixed inset-0 z-50" role="presentation">
      <button
        type="button"
        aria-label="Cerrar asistente"
        className="absolute inset-0 border-0"
        style={{
          background: "var(--dr-flit-overlay)",
          backdropFilter: "blur(4px)",
        }}
        onClick={onClose}
      />

      <div
        ref={panelRef}
        id={panelId}
        role="dialog"
        aria-modal="true"
        aria-labelledby={`${panelId}-title`}
        className="dr-flit-panel-enter absolute inset-y-0 right-0 flex w-full max-w-md flex-col overflow-hidden"
        style={{
          background: "var(--dr-flit-panel-bg)",
          borderTopLeftRadius: "var(--dr-flit-radius-widget)",
          borderBottomLeftRadius: "var(--dr-flit-radius-widget)",
          boxShadow: "var(--dr-flit-shadow-panel)",
          fontFamily: "var(--dr-flit-font)",
        }}
      >
        <header
          className="flex shrink-0 items-center gap-3 px-4 py-3.5"
          style={{ background: "var(--dr-flit-gradient-header)" }}
        >
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

          {state.showSuggestions && !state.isTyping && (
            <div className="pt-1">
              <DrFlitSuggestions onSelect={onSelectIntent} />
            </div>
          )}

          {state.showBackToSearch && !state.isTyping && (
            <div className="pt-1">
              <DrFlitBackToSearch onBack={onBackToSearch} />
            </div>
          )}
        </div>

        <DrFlitComposer
          onSend={onSend}
          inputRef={inputRef}
          disabled={state.isTyping || !composerEnabled}
        />
      </div>
    </div>
  );
}
