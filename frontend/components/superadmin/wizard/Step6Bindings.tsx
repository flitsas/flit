export function Step6Bindings() {
  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-base font-bold mb-1">Bindings API</h2>
        <p className="text-xs opacity-60">
          Mapeo de campos a fuentes de datos externas.
        </p>
      </div>

      <div
        className="rounded-2xl p-6 border flex flex-col items-center gap-3"
        style={{ borderStyle: 'dashed' }}
      >
        <span
          className="px-3 py-1 rounded-full text-[10px] font-bold text-white"
          style={{ background: '#557EFF' }}
        >
          Próximamente
        </span>
        <p className="text-xs text-center opacity-60 max-w-xs">
          La configuración de bindings a RUNT, SIMIT y RUES estará disponible en
          la siguiente iteración del motor. Feature #10128.
        </p>
      </div>
    </div>
  );
}
