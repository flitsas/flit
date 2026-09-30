using Xunit;

namespace Flit.Integration.Tests.Postgres;

/// <summary>
/// HU #13083 — prueba de carga contra Postgres real: solo corre con <c>FLIT_IT_CARGA=1</c> (y Postgres alcanzable).
/// Sembrar 50.000 trámites tarda unos 2 minutos por los triggers fila a fila; en la suite normal pondría en riesgo
/// el tope de 20 minutos del job de CI. Uso: <c>FLIT_IT_CARGA=1 dotnet test --filter ExternalSyncLoadTests</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CargaFactAttribute : FactAttribute
{
    public CargaFactAttribute()
    {
        Skip = "Prueba de carga: definir FLIT_IT_CARGA=1 para correrla (HU #13083).";
        SkipType = typeof(CargaFactAttribute);
        SkipUnless = nameof(ShouldRun);
    }

    public static bool ShouldRun =>
        Environment.GetEnvironmentVariable("FLIT_IT_CARGA") == "1" && PostgresAvailability.ShouldRun;
}
