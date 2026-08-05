"use client";

import type { RefObject } from "react";
import { DR_FLIT_ASSETS } from "./dr-flit-assets";

export function DrFlitFab({
  open,
  onClick,
  buttonRef,
  controlsId,
}: {
  open: boolean;
  onClick: () => void;
  buttonRef: RefObject<HTMLButtonElement | null>;
  controlsId: string;
}) {
  return (
    <button
      ref={buttonRef}
      type="button"
      onClick={onClick}
      aria-label={open ? "Cerrar DR. FLIT" : "Abrir DR. FLIT"}
      aria-expanded={open}
      aria-controls={controlsId}
      className={[
        "dr-flit fixed z-[45] h-16 w-16 overflow-visible rounded-full bg-transparent p-0",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2",
        "transition-transform hover:scale-[1.04] motion-reduce:transition-none motion-reduce:hover:scale-100",
        "bottom-24 right-4 lg:bottom-6 lg:right-6",
      ].join(" ")}
      style={{
        boxShadow: "var(--dr-flit-shadow-fab)",
        fontFamily: "var(--dr-flit-font)",
      }}
    >
      <span
        className="absolute -top-0.5 -right-0.5 z-10 h-3 w-3 rounded-full ring-2 ring-white"
        style={{ background: "var(--dr-flit-accent)" }}
        aria-hidden="true"
      />
      <span className="block h-full w-full overflow-hidden rounded-full bg-transparent">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src={DR_FLIT_ASSETS.fab}
          alt=""
          aria-hidden="true"
          className="h-full w-full object-contain"
          draggable={false}
        />
      </span>
    </button>
  );
}
