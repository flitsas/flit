export function LoadingState() {
  return (
    <div className="flex-1 flex flex-col gap-2" aria-busy="true" aria-label="Cargando parametrizaciones">
      {Array.from({ length: 4 }).map((_, i) => (
        <div
          key={i}
          className="h-12 rounded-xl animate-pulse"
          style={{ background: 'var(--secondary)' }}
        />
      ))}
    </div>
  );
}
