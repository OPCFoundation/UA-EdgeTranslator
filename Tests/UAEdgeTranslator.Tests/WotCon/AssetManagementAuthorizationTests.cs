namespace Opc.Ua.Edge.Translator.Tests.WotCon
{
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.WotCon;
    using Opc.Ua.Server;
    using System;
    using Xunit;

    /// <summary>
    /// WoT-Con 1.1 §Security: the 1.02 asset-management operations require the
    /// management role, and those that alter the AddressSpace require SignAndEncrypt.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class AssetManagementAuthorizationTests : IDisposable
    {
        private readonly string _previousRole = Environment.GetEnvironmentVariable(AssetManagementAuthorization.RoleVariable);

        public AssetManagementAuthorizationTests()
        {
            Environment.SetEnvironmentVariable(AssetManagementAuthorization.RoleVariable, null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(AssetManagementAuthorization.RoleVariable, _previousRole);
        }

        private static IUserIdentity ConfigurationUser() =>
            new SystemConfigurationIdentity(new UserIdentity("admin", "secret"u8.ToArray()));

        [Fact]
        public void Configuration_user_on_SignAndEncrypt_may_mutate()
        {
            ServiceResult result = AssetManagementAuthorization.Authorize(
                ConfigurationUser(), MessageSecurityMode.SignAndEncrypt, "CreateAsset", mutates: true);

            Assert.True(ServiceResult.IsGood(result));
        }

        [Theory]
        [InlineData(MessageSecurityMode.None)]
        [InlineData(MessageSecurityMode.Sign)]
        public void Mutation_without_SignAndEncrypt_is_rejected(MessageSecurityMode mode)
        {
            ServiceResult result = AssetManagementAuthorization.Authorize(
                ConfigurationUser(), mode, "DeleteAsset", mutates: true);

            Assert.Equal(StatusCodes.BadSecurityModeInsufficient, result.StatusCode.Code);
        }

        [Fact]
        public void Read_only_operation_does_not_require_SignAndEncrypt()
        {
            ServiceResult result = AssetManagementAuthorization.Authorize(
                ConfigurationUser(), MessageSecurityMode.Sign, "DiscoverAssets", mutates: false);

            Assert.True(ServiceResult.IsGood(result));
        }

        [Fact]
        public void Anonymous_user_lacks_the_management_role()
        {
            ServiceResult result = AssetManagementAuthorization.Authorize(
                new UserIdentity(), MessageSecurityMode.SignAndEncrypt, "ConnectionTest", mutates: false);

            Assert.Equal(StatusCodes.BadUserAccessDenied, result.StatusCode.Code);
        }

        [Fact]
        public void Missing_identity_is_rejected()
        {
            ServiceResult result = AssetManagementAuthorization.Authorize(
                null, MessageSecurityMode.SignAndEncrypt, "CloseAndUpdate", mutates: true);

            Assert.Equal(StatusCodes.BadUserAccessDenied, result.StatusCode.Code);
        }

        [Fact]
        public void Required_role_can_be_configured()
        {
            // Engineer is not among the roles granted to the configuration user.
            Environment.SetEnvironmentVariable(AssetManagementAuthorization.RoleVariable, ObjectIds.WellKnownRole_Engineer.ToString());

            ServiceResult result = AssetManagementAuthorization.Authorize(
                ConfigurationUser(), MessageSecurityMode.SignAndEncrypt, "CreateAsset", mutates: true);

            Assert.Equal(StatusCodes.BadUserAccessDenied, result.StatusCode.Code);
        }

        [Fact]
        public void Invalid_configured_role_falls_back_to_ConfigureAdmin()
        {
            Environment.SetEnvironmentVariable(AssetManagementAuthorization.RoleVariable, "not a node id");

            Assert.Equal(ObjectIds.WellKnownRole_ConfigureAdmin, AssetManagementAuthorization.RequiredRoleId);
        }
    }
}
