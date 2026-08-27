"use client";

import "./dr-flit-tokens.css";
import { DrFlitChatPanel } from "./DrFlitChatPanel";
import { DrFlitFab } from "./DrFlitFab";
import { useDrFlitChat } from "./useDrFlitChat";

export function DrFlitAssistant({
  displayName,
}: {
  displayName?: string | null;
}) {
  const chat = useDrFlitChat(displayName);

  return (
    <>
      <div
        className={chat.open ? "invisible pointer-events-none" : undefined}
        aria-hidden={chat.open}
      >
        <DrFlitFab
          open={chat.open}
          onClick={chat.openPanel}
          buttonRef={chat.fabRef}
          controlsId={chat.panelId}
        />
      </div>
      <DrFlitChatPanel
        open={chat.open}
        panelId={chat.panelId}
        state={chat.state}
        onClose={chat.closePanel}
        onSelectIntent={chat.selectIntent}
        onSelectHelpOption={chat.selectHelpOption}
        onSelectClientBranch={chat.selectClientBranch}
        onBackToSearch={chat.backToSearch}
        onSend={chat.sendText}
        onNavigate={chat.navigate}
        panelRef={chat.panelRef}
        closeButtonRef={chat.closeButtonRef}
        inputRef={chat.inputRef}
      />
    </>
  );
}
