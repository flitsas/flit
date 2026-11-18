"use client";

/** Banner persistente al administrar un cliente hijo (HU #12356 AC5). */
export function AdminChildContextBanner({ childName }: { childName: string }) {
  return (
    <div
      className="mb-4 rounded-xl border px-4 py-3 text-xs font-medium"
      style={{
        borderColor: "var(--badge-warning-border)",
        background: "var(--badge-warning-bg)",
        color: "var(--badge-warning-fg)",
      }}
      role="status"
      aria-live="polite"
    >
      Estás administrando <strong>{childName}</strong> — compañía de tu red. Tus propios datos no se
      ven afectados.
    </div>
  );
}
