"use client";

import "./dr-flit-tokens.css";
import { DrFlitChatPanel } from "./DrFlitChatPanel";
import { DrFlitFab } from "./DrFlitFab";
import { useDrFlitChat } from "./useDrFlitChat";

export function DrFlitAssistant({
  displayName,
  routeScope,
  historialPlacaEnabled,
  canSearchValidaciones,
}: {
  displayName?: string | null;
  /** HU #12711 — `true` si el usuario ve el módulo de Validación de Identidad en el menú. */
  canSearchValidaciones?: boolean;
  /** Identificador de módulo/ruta; al cambiar se cierra el panel (sin limpiar chat). */
  routeScope?: string;
  /** HU-C — el usuario tiene el módulo «Historial por placa» (por defecto sí). */
  historialPlacaEnabled?: boolean;
}) {
  const chat = useDrFlitChat(displayName, routeScope, { historialPlacaEnabled });

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
        onEndChat={chat.endChat}
        onSelectIntent={chat.selectIntent}
        onSelectHelpOption={chat.selectHelpOption}
        onSelectClientBranch={chat.selectClientBranch}
        onBackToSearch={chat.backToSearch}
        onSend={chat.sendText}
        onNavigate={chat.navigate}
        canSearchValidaciones={canSearchValidaciones}
        panelRef={chat.panelRef}
        closeButtonRef={chat.closeButtonRef}
        inputRef={chat.inputRef}
      />
    </>
  );
}
