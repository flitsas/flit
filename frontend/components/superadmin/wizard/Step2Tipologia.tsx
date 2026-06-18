export function Step2Tipologia() {
  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-base font-bold mb-1">Tipología</h2>
        <p className="text-xs opacity-60">Metadatos adicionales del tipo de trámite.</p>
      </div>

      <div className="space-y-4">
        <div>
          <label htmlFor="pt-description" className="text-xs font-semibold block mb-1.5">
            Descripción
          </label>
          <textarea
            id="pt-description"
            rows={3}
            placeholder="Descripción opcional del flujo..."
            className="w-full px-3 py-2 rounded-xl border bg-white dark:bg-[#0B0F14] text-xs outline-none focus:border-[#557EFF] resize-none"
            style={{ borderColor: '#DFE5ED' }}
          />
        </div>

        <div>
          <label htmlFor="pt-version" className="text-xs font-semibold block mb-1.5">
            Versión inicial
          </label>
          <input
            id="pt-version"
            type="text"
            defaultValue="1.0"
            className="w-32 px-3 py-2 rounded-xl border bg-white dark:bg-[#0B0F14] text-xs outline-none focus:border-[#557EFF]"
            style={{ borderColor: '#DFE5ED' }}
          />
        </div>

        <div className="flex items-center gap-3">
          <input
            id="pt-active"
            type="checkbox"
            defaultChecked
            className="rounded"
          />
          <label htmlFor="pt-active" className="text-xs font-medium">
            Activo desde publicación
          </label>
        </div>
      </div>
    </div>
  );
}
