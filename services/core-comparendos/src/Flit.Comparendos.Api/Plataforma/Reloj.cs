using Flit.Comparendos.Domain.Tiempo;

namespace Flit.Comparendos.Api.Plataforma;

/// <summary>Hoy y ahora del negocio (hora de Colombia), con un <see cref="TimeProvider"/> que las pruebas reemplazan.</summary>
public sealed class Reloj(TimeProvider time)
{
    public DateTimeOffset Ahora => time.GetUtcNow();

    public DateOnly Hoy => ColombiaTime.Today(time);

    public DateTimeOffset AhoraColombia => ColombiaTime.From(time.GetUtcNow());
}
