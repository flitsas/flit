/**
 * Pantalla de envío (réplica del estado «verificando» de Kyverum, sin verificación automática): spinner
 * azul claro y una línea de texto. El spinner solo gira si el usuario no pidió reducir el movimiento.
 */
export function EnviandoInfo() {
  return (
    <div role="status" aria-live="polite" data-testid="enviando-info" className="mt-8 flex flex-col items-center gap-5 text-center">
      <span
        aria-hidden="true"
        className="size-12 rounded-full border-4 border-flit-brand/20 border-t-flit-brand motion-safe:animate-spin"
      />
      <p className="text-lg text-flit-primary">Enviando tu información… suele tomar unos segundos.</p>
    </div>
  );
}
