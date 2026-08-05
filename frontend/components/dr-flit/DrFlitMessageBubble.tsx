"use client";

import type { DrFlitMessage } from "./dr-flit-conversation";
import { DR_FLIT_ASSETS } from "./dr-flit-assets";

function renderRichText(text: string) {
  const parts = text.split(/(\*\*[^*]+\*\*|`[^`]+`)/g);
  return parts.map((part, i) => {
    if (part.startsWith("**") && part.endsWith("**")) {
      return (
        <strong key={i} className="font-semibold">
          {part.slice(2, -2)}
        </strong>
      );
    }
    if (part.startsWith("`") && part.endsWith("`")) {
      return (
        <code
          key={i}
          className="rounded px-1 py-0.5 font-mono text-[12px]"
          style={{ background: "var(--dr-flit-code-bg)" }}
        >
          {part.slice(1, -1)}
        </code>
      );
    }
    return <span key={i}>{part}</span>;
  });
}

export function DrFlitMessageBubble({ message }: { message: DrFlitMessage }) {
  const isUser = message.role === "user";

  return (
    <div
      className={`flex w-full items-end gap-2 ${isUser ? "justify-end" : "justify-start"}`}
      data-role={message.role}
    >
      {!isUser && (
        <img
          src={DR_FLIT_ASSETS.header}
          alt=""
          aria-hidden="true"
          className="mb-0.5 h-8 w-8 shrink-0 rounded-full object-cover"
          draggable={false}
        />
      )}
      <div
        className="max-w-[85%] px-3.5 py-2.5 text-sm leading-snug"
        style={{
          background: isUser
            ? "var(--dr-flit-brand-blue)"
            : "var(--dr-flit-card-bg)",
          color: isUser ? "var(--dr-flit-text-inverse)" : "var(--dr-flit-text)",
          border: isUser ? undefined : "1px solid var(--dr-flit-border)",
          borderRadius: "var(--dr-flit-radius-chat)",
          borderTopLeftRadius: isUser ? "var(--dr-flit-radius-chat)" : "8px",
          borderTopRightRadius: isUser ? "8px" : "var(--dr-flit-radius-chat)",
          boxShadow: isUser ? undefined : "var(--dr-flit-shadow-card)",
        }}
      >
        {renderRichText(message.text)}
      </div>
    </div>
  );
}
