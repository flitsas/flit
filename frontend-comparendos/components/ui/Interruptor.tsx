"use client";

/** Interruptor accesible (role=switch) con su etiqueta y una ayuda opcional. */
export function Interruptor({
  activo,
  onCambio,
  etiqueta,
  ayuda,
  deshabilitado,
}: {
  activo: boolean;
  onCambio: (v: boolean) => void;
  etiqueta: string;
  ayuda?: string;
  deshabilitado?: boolean;
}) {
  return (
    <div className={`flex items-start gap-3 ${deshabilitado ? "opacity-60" : ""}`}>
      <button
        type="button"
        role="switch"
        aria-checked={activo}
        aria-label={etiqueta}
        disabled={deshabilitado}
        onClick={() => onCambio(!activo)}
        className={`relative mt-0.5 inline-flex h-5 w-9 shrink-0 rounded-full transition ${activo ? "bg-flit-brand" : "bg-[#C9D2DE] dark:bg-white/20"} disabled:cursor-not-allowed`}
      >
        <span className={`absolute top-0.5 h-4 w-4 rounded-full bg-white shadow transition ${activo ? "left-[18px]" : "left-0.5"}`} />
      </button>
      <span className="flex flex-col">
        <span className="text-xs font-semibold text-flit-primary dark:text-white">{etiqueta}</span>
        {ayuda && <span className="text-[11px] text-[#5B6B82] dark:text-slate-300">{ayuda}</span>}
      </span>
    </div>
  );
}
