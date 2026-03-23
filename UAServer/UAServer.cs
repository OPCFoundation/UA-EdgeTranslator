
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua;
    using Opc.Ua.Server;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Text;

    public partial class UAServer : ReverseConnectServer
    {
        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            List<INodeManager> nodeManagers = new List<INodeManager>
            {
                new UANodeManager(server, configuration)
            };

            return new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
        }

        protected override ServerProperties LoadServerProperties()
        {
            ServerProperties properties = new ServerProperties
            {
                ManufacturerName = "OPC Foundation",
                ProductName = "UA Edge Translator",
                ProductUri = "http://opcfoundation.org/UA/EdgeTranslator",
                SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
                BuildNumber = Utils.GetAssemblyBuildNumber(),
                BuildDate = Utils.GetAssemblyTimestamp()
            };

            return properties;
        }

        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);

            server.SessionManager.ImpersonateUser += new ImpersonateEventHandler(SessionManager_ImpersonateUser);
        }

        private void SessionManager_ImpersonateUser(ISession session, ImpersonateEventArgs args)
        {
            UserNameIdentityToken userNameToken = args.NewIdentity as UserNameIdentityToken;
            if (userNameToken != null)
            {
                args.Identity = VerifyPassword(userNameToken);

                Log.Logger.Information("Username Token Accepted: {0}", args.Identity?.DisplayName);
                return;
            }

            throw ServiceResultException.Create(StatusCodes.BadIdentityTokenInvalid, "Not supported user token type: {0}.", args.NewIdentity);
        }


        private IUserIdentity VerifyPassword(UserNameIdentityToken userNameToken)
        {
            string userName = userNameToken.UserName;
            string password = Encoding.UTF8.GetString(userNameToken.DecryptedPassword);
            if (string.IsNullOrEmpty(userName))
            {
                throw ServiceResultException.Create(StatusCodes.BadIdentityTokenInvalid,
                    "Security token is not a valid username token. An empty username is not accepted.");
            }

            if (string.IsNullOrEmpty(password))
            {
                throw ServiceResultException.Create(StatusCodes.BadIdentityTokenRejected,
                    "Security token is not a valid username token. An empty password is not accepted.");
            }

            string configuredUsername = Environment.GetEnvironmentVariable("OPCUA_USERNAME");
            string configuredPassword = Environment.GetEnvironmentVariable("OPCUA_PASSWORD");
            if (!string.IsNullOrEmpty(configuredUsername)
             && !string.IsNullOrEmpty(configuredPassword)
             && (userName == configuredUsername)
             && (password == configuredPassword))
            {
                return new SystemConfigurationIdentity(new UserIdentity(userNameToken));
            }

            throw new ServiceResultException(StatusCodes.BadUserAccessDenied, userName);
        }
    }
}
