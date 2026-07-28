namespace Flit.Ict.Domain.Abstractions;

/// <summary>Conflicto de concurrencia optimista (row_version) al persistir una entidad ICT.</summary>
public sealed class IctConcurrencyException : Exception
{
    public IctConcurrencyException()
        : base("Conflicto de concurrencia (row_version).")
    {
    }

    public IctConcurrencyException(string message)
        : base(message)
    {
    }

    public IctConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
