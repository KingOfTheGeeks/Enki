using SDI.Enki.Identity.Validation;
using SDI.Enki.Shared.Identity;

namespace SDI.Enki.Identity.Tests.Validation;

/// <summary>
/// Pure-function tests for the classification triplet rules. Same
/// validator runs in both the admin endpoints and the seed reconciler,
/// so coverage here protects both write paths.
/// </summary>
public class UserClassificationValidatorTests
{
    [Fact]
    public void Team_with_Office_subtype_and_no_tenant_is_valid()
    {
        var failures = UserClassificationValidator.Validate(
            UserType.Team, TeamSubtype.Office, tenantId: null, isEnkiAdmin: false);
        Assert.Empty(failures);
    }

    [Fact]
    public void Team_admin_is_valid()
    {
        // Team users are explicitly allowed to hold IsEnkiAdmin (Mike, Gavin).
        var failures = UserClassificationValidator.Validate(
            UserType.Team, TeamSubtype.Field, tenantId: null, isEnkiAdmin: true);
        Assert.Empty(failures);
    }

    [Fact]
    public void Tenant_with_tenant_id_no_subtype_is_valid()
    {
        var failures = UserClassificationValidator.Validate(
            UserType.Tenant, teamSubtype: null,
            tenantId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            isEnkiAdmin: false);
        Assert.Empty(failures);
    }

    [Fact]
    public void Null_user_type_is_rejected()
    {
        var failures = UserClassificationValidator.Validate(
            userType: null, teamSubtype: null, tenantId: null, isEnkiAdmin: false);
        Assert.Contains(failures, f => f.Field == "UserType");
    }

    [Fact]
    public void Team_without_subtype_is_rejected()
    {
        var failures = UserClassificationValidator.Validate(
            UserType.Team, teamSubtype: null, tenantId: null, isEnkiAdmin: false);
        Assert.Contains(failures, f => f.Field == "TeamSubtype");
    }

    [Fact]
    public void Team_with_tenant_id_is_rejected()
    {
        var failures = UserClassificationValidator.Validate(
            UserType.Team, TeamSubtype.Office,
            tenantId: Guid.NewGuid(), isEnkiAdmin: false);
        Assert.Contains(failures, f => f.Field == "TenantId");
    }

    [Fact]
    public void Tenant_without_tenant_id_is_rejected()
    {
        var failures = UserClassificationValidator.Validate(
            UserType.Tenant, teamSubtype: null, tenantId: null, isEnkiAdmin: false);
        Assert.Contains(failures, f => f.Field == "TenantId");
    }

    [Fact]
    public void Tenant_with_empty_tenant_id_is_rejected()
    {
        var failures = UserClassificationValidator.Validate(
            UserType.Tenant, teamSubtype: null, tenantId: Guid.Empty, isEnkiAdmin: false);
        Assert.Contains(failures, f => f.Field == "TenantId");
    }

    [Fact]
    public void Tenant_with_subtype_is_rejected()
    {
        var failures = UserClassificationValidator.Validate(
            UserType.Tenant, TeamSubtype.Office,
            tenantId: Guid.NewGuid(), isEnkiAdmin: false);
        Assert.Contains(failures, f => f.Field == "TeamSubtype");
    }

    [Fact]
    public void Tenant_admin_is_rejected()
    {
        // Hard rule: Tenant users can never hold cross-tenant admin.
        var failures = UserClassificationValidator.Validate(
            UserType.Tenant, teamSubtype: null,
            tenantId: Guid.NewGuid(), isEnkiAdmin: true);
        Assert.Contains(failures, f => f.Field == "IsEnkiAdmin");
    }

    [Fact]
    public void String_overload_rejects_unknown_user_type()
    {
        var failures = UserClassificationValidator.Validate(
            userTypeName: "WhatIsThis", teamSubtypeName: null, tenantId: null, isEnkiAdmin: false);
        Assert.Single(failures);
        Assert.Equal("UserType", failures[0].Field);
    }

    [Fact]
    public void String_overload_rejects_unknown_team_subtype()
    {
        var failures = UserClassificationValidator.Validate(
            userTypeName: "Team", teamSubtypeName: "NotARealSubtype",
            tenantId: null, isEnkiAdmin: false);
        Assert.Single(failures);
        Assert.Equal("TeamSubtype", failures[0].Field);
    }

    [Fact]
    public void String_overload_round_trips_valid_combo()
    {
        var failures = UserClassificationValidator.Validate(
            userTypeName: "Team", teamSubtypeName: "Supervisor",
            tenantId: null, isEnkiAdmin: false);
        Assert.Empty(failures);
    }
}
