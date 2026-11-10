"use client";

// No se escribe el error: podría arrastrar datos de un trámite (Habeas Data).
export default function ErrorCaja({ reset }: { error: Error & { digest?: string }; reset: () => void }) {
  return (
    <main className="flex min-h-screen items-center justify-center bg-background px-4">
      <div role="alert" className="max-w-md text-center text-foreground">
        <h1 className="text-xl font-semibold">No se pudo abrir la caja</h1>
        <p className="mt-2 text-sm">Intente de nuevo.</p>
        <button
          type="button"
          onClick={reset}
          className="mt-4 inline-flex h-11 items-center rounded-full px-6 text-sm font-semibold text-white"
          style={{ background: "var(--dashboard-action-background)" }}
        >
          Reintentar
        </button>
      </div>
    </main>
  );
}
