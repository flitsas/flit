interface EmptyStateProps {
  onNew: () => void;
}

export function EmptyState({ onNew }: EmptyStateProps) {
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
            d="M12 9v6m3-3H9m12 0a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z"
          />
        </svg>
      </div>
      <div className="text-center">
        <p className="text-sm font-bold">Sin parametrizaciones</p>
        <p className="text-xs opacity-60 mt-1">
          Crea el primer flujo de trámite para comenzar
        </p>
      </div>
      <button
        onClick={onNew}
        className="px-5 py-2.5 rounded-xl text-xs font-semibold text-white"
        style={{ background: '#557EFF' }}
        aria-label="Crear nuevo flujo de parametrización"
      >
        Nuevo flujo
      </button>
    </div>
  );
}
