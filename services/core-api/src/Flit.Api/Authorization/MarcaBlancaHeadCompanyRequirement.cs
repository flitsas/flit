using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #12429 (endurecimiento del hecho 88) — igual que <see cref="GroupHeadCompanyRequirement"/>
/// (rol AdminCompany + <c>is_group_parent</c>) pero exige además que la clase de la cabeza sea
/// <c>MARCA_BLANCA</c>. Una Concesión con hijas es cabeza de grupo legítima para la jerarquía
/// (<see cref="GroupHeadCompanyRequirement"/>, HU #12345) pero NUNCA para autogestionar marca o
/// dominio: esas rutas son exclusivas de la red Marca Blanca (CF10/CF14 de #12366).
/// </summary>
public sealed class MarcaBlancaHeadCompanyRequirement : IAuthorizationRequirement;
