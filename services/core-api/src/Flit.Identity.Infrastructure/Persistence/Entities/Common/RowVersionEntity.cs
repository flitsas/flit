namespace Flit.Infrastructure.Persistence.Entities.Common;

public abstract class RowVersionEntity
{
    public Guid Id { get; set; }

    public long RowVersion { get; set; }
}
