using Flit.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

public sealed class ImprintSignatureValidationSchemaTests
{
    [Fact]
    public void Ddl_100_ImprintSignatureValidations_HasRequiredObjects()
    {
        var sql = EmbeddedDdl.LoadUp("100-imprint-signature-validations.sql");

        sql.Should().Contain("tramites.imprint_signature_validations");
        sql.Should().Contain("ix_imprint_signature_validations_tenant_validated_at");
        sql.Should().Contain("ix_imprint_signature_validations_imprint");
        sql.Should().Contain("tenant_isolation");
        sql.Should().Contain("ck_imprint_signature_validations_result");
    }
}
