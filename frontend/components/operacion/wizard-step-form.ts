/**
 * Contrato imperativo de un formulario de paso embebido en el wizard. La shell
 * dispara `save()` desde el footer "Guardar y continuar" (en vez de que cada
 * form tenga su propio botón de guardado). Lo implementan los pasos de captura
 * que persisten antes de avanzar: actores (vendedor/comprador) y comercial.
 */
export interface WizardStepFormHandle {
  /** Valida y guarda; resuelve `true` si persistió, `false` si inválido o falló. */
  save: () => Promise<boolean>;
  /**
   * Bug #11614 — ¿queda captura del usuario sin persistir? El footer no es la única vía de salida
   * de un paso: el stepper superior y "Anterior" también cambian de paso, y como el formulario se
   * desmonta al hacerlo, lo capturado se perdía. La shell consulta esta señal antes de navegar y
   * solo dispara `save()` cuando hay algo pendiente (así una navegación sobre un paso intacto no
   * paga una llamada de red ni choca contra las validaciones del formulario).
   *
   * OBLIGATORIO (no opcional a propósito): si mañana nace otro paso con form embebido, TypeScript
   * lo obliga a declarar su señal de pendiente en vez de reintroducir la pérdida en silencio.
   */
  hasPendingChanges: () => boolean;
}
