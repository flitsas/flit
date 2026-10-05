/** Marcador de los pasos que implementan B6 (Rostro/Anverso/Reverso) y B7 (Firma). */
export function StepPlaceholder({ label, onNext, onBack }: { label: string; onNext: () => void; onBack: () => void }) {
  const btn =
    "min-h-11 rounded-xl px-4 text-base font-semibold focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-flit-brand";
  return (
    <section aria-label={label} className="mt-6 flex flex-col gap-3">
      <h1 className="text-xl font-bold text-flit-primary">{label}</h1>
      <p className="text-base text-muted-foreground">Este paso estará disponible próximamente.</p>
      <div className="flex gap-3">
        <button type="button" onClick={onBack} className={`${btn} border border-flit-brand-ink text-flit-brand-ink`}>
          Atrás
        </button>
        <button type="button" onClick={onNext} className={`${btn} flex-1 bg-flit-brand text-flit-primary`}>
          Continuar
        </button>
      </div>
    </section>
  );
}
