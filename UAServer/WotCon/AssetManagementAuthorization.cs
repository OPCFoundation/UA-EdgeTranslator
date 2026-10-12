namespace Opc.Ua.Edge.Translator.WotCon
{
    using Opc.Ua.Server;
    using Serilog;
    using System;

    /// <summary>
    /// Authorization for the incorporated WoT-Con 1.02 asset-management surface.
    /// </summary>
    /// <remarks>
    /// WoT-Con 1.1 §Security: <c>CreateAsset</c>, <c>DeleteAsset</c>,
    /// <c>CreateAssetForEndpoint</c>, <c>ConnectionTest</c>, <c>DiscoverAssets</c> and
    /// the <c>WoTFile</c> <c>Write</c>/<c>CloseAndUpdate</c> operations are subject to
    /// OPC UA role-based access control, and each operation that alters the
    /// AddressSpace requires a <c>SignAndEncrypt</c> SecureChannel.
    /// <para>
    /// The required role defaults to the well-known <c>ConfigureAdmin</c> role, which
    /// the server grants to an authenticated configuration user. It can be changed
    /// with <c>WOTCON_MANAGEMENT_ROLE</c> (a NodeId such as <c>i=15716</c>).
    /// </para>
    /// <para>
    /// Calls that carry no session (internal callers, e.g. the dashboard or startup
    /// onboarding) are not client requests and are allowed.
    /// </para>
    /// </remarks>
    public static class AssetManagementAuthorization
    {
        public const string RoleVariable = "WOTCON_MANAGEMENT_ROLE";

        /// <summary>
        /// Test hook: disables the checks for in-process test fixtures that run
        /// over an unsecured loopback channel. Never set in production code paths.
        /// </summary>
        public static bool DisabledForTests { get; set; }

        public static NodeId RequiredRoleId
        {
            get
            {
                string configured = Environment.GetEnvironmentVariable(RoleVariable);
                if (!string.IsNullOrWhiteSpace(configured))
                {
                    try
                    {
                        return NodeId.Parse(configured);
                    }
                    catch (Exception ex) when (ex is ServiceResultException or FormatException or ArgumentException)
                    {
                        Log.Logger.Warning("Ignoring invalid {Variable} value {Value}; using ConfigureAdmin.", RoleVariable, configured);
                    }
                }

                return Ua.ObjectIds.WellKnownRole_ConfigureAdmin;
            }
        }

        /// <summary>
        /// Checks that the caller holds the management role and, when
        /// <paramref name="mutates"/> is true, that the channel is SignAndEncrypt.
        /// </summary>
        public static ServiceResult Authorize(ISystemContext context, string operation, bool mutates)
        {
            if (DisabledForTests
                || context is not ServerSystemContext serverContext
                || serverContext.OperationContext is not OperationContext operationContext)
            {
                return ServiceResult.Good;
            }

            MessageSecurityMode mode = operationContext.ChannelContext?.EndpointDescription?.SecurityMode ?? MessageSecurityMode.Invalid;
            return Authorize(operationContext.UserIdentity, mode, operation, mutates);
        }

        /// <summary>
        /// The decision itself, independent of how the caller's identity and channel
        /// security mode were obtained.
        /// </summary>
        public static ServiceResult Authorize(IUserIdentity identity, MessageSecurityMode securityMode, string operation, bool mutates)
        {
            if (mutates && securityMode != MessageSecurityMode.SignAndEncrypt)
            {
                Log.Logger.Warning("Rejected {Operation}: requires a SignAndEncrypt SecureChannel (was {Mode}).", operation, securityMode);
                return new ServiceResult(StatusCodes.BadSecurityModeInsufficient,
                    new Ua.LocalizedText($"{operation} requires a SignAndEncrypt SecureChannel."));
            }

            NodeId requiredRole = RequiredRoleId;
            bool granted = false;
            if (identity != null)
            {
                foreach (NodeId role in identity.GrantedRoleIds)
                {
                    if (role == requiredRole)
                    {
                        granted = true;
                        break;
                    }
                }
            }

            if (!granted)
            {
                Log.Logger.Warning("Rejected {Operation}: user {User} lacks role {Role}.", operation, identity?.DisplayName, requiredRole);
                return new ServiceResult(StatusCodes.BadUserAccessDenied,
                    new Ua.LocalizedText($"{operation} requires the asset-management role."));
            }

            return ServiceResult.Good;
        }
    }
}
