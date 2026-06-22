interface ErrorStateProps {
  message: string;
  onRetry: () => void;
}

export function ErrorState({ message, onRetry }: ErrorStateProps) {
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
        <p className="text-sm font-bold">Error al cargar</p>
        <p className="text-xs opacity-60 mt-1 max-w-xs">{message}</p>
      </div>
      <button
        onClick={onRetry}
        className="px-5 py-2.5 rounded-xl text-xs font-semibold border"
        style={{ borderColor: '#557EFF', color: '#557EFF' }}
        aria-label="Reintentar cargar parametrizaciones"
      >
        Reintentar
      </button>
    </div>
  );
}
