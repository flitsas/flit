// Estados WCAG para la vista de Operación (copy adaptado vs. parametrización).

export function OperacionLoadingState() {
  return (
    <div
      className="flex-1 flex flex-col gap-2"
      aria-busy="true"
      aria-label="Cargando tipos de trámite"
    >
      {Array.from({ length: 3 }).map((_, i) => (
        <div
          key={i}
          className="h-12 rounded-xl animate-pulse"
          style={{ background: 'rgba(223,229,237,0.5)' }}
        />
      ))}
    </div>
  );
}

interface ErrorProps {
  message: string;
  onRetry: () => void;
}

export function OperacionErrorState({ message, onRetry }: ErrorProps) {
  return (
    <div
      className="flex-1 flex flex-col items-center justify-center gap-4 py-16"
      role="alert"
    >
      <div
        className="h-14 w-14 rounded-2xl grid place-items-center"
        style={{ background: 'rgba(255,78,0,0.10)' }}
      >
        <svg
          className="h-7 w-7"
          style={{ color: '#FF4E00' }}
          fill="none"
          stroke="currentColor"
          strokeWidth={1.5}
          viewBox="0 0 24 24"
          aria-hidden="true"
        >
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126ZM12 15.75h.007v.008H12v-.008Z"
          />
        </svg>
      </div>
      <div className="text-center">
        <p className="text-sm font-bold">Error al cargar trámites</p>
        <p className="text-xs opacity-60 mt-1 max-w-xs">{message}</p>
      </div>
      <button
        onClick={onRetry}
        className="px-5 py-2.5 rounded-xl text-xs font-semibold border"
        style={{ borderColor: '#557EFF', color: '#557EFF' }}
        aria-label="Reintentar cargar tipos de trámite"
      >
        Reintentar
      </button>
    </div>
  );
}

export function OperacionEmptyState() {
  return (
    <div className="flex-1 flex flex-col items-center justify-center gap-4 py-16">
      <div
        className="h-16 w-16 rounded-2xl grid place-items-center"
        style={{ background: 'rgba(85,126,255,0.10)' }}
      >
        <svg
          className="h-8 w-8"
          style={{ color: '#557EFF' }}
          fill="none"
          stroke="currentColor"
          strokeWidth={1.5}
          viewBox="0 0 24 24"
          aria-hidden="true"
        >
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            d="M9 12h6m-6 4h6m2 5H7a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5.586a1 1 0 0 1 .707.293l5.414 5.414a1 1 0 0 1 .293.707V19a2 2 0 0 1-2 2Z"
          />
        </svg>
      </div>
      <div className="text-center">
        <p className="text-sm font-bold">Sin trámites publicados</p>
        <p className="text-xs opacity-60 mt-1 max-w-xs">
          Aún no hay tipos de trámite publicados. Publica uno desde
          Parametrización para iniciar operación.
        </p>
      </div>
    </div>
  );
}
