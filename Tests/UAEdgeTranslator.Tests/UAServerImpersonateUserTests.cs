namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua;
    using Opc.Ua.Server;
    using System;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Text;
    using Xunit;

    /// <summary>
    /// Reflection-driven coverage for the private
    /// <see cref="UAServer"/>.<c>SessionManager_ImpersonateUser</c> hook that
    /// the SDK invokes for each session login. The method is impossible to
    /// reach via the public OPC UA wire surface without booting a full server
    /// and a real client, so the tests construct an <see cref="ImpersonateEventArgs"/>
    /// directly and dispatch the hook through reflection.
    ///
    /// Joined to the working-directory collection so the tests don't race the
    /// integration fixture, which mutates <c>Program.OpcUaUsername</c> /
    /// <c>Program.OpcUaPassword</c>.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class UAServerImpersonateUserTests
    {
        private static readonly Type _sut = typeof(UAServer);

        [Fact]
        public void ImpersonateUser_accepts_valid_username_token_and_assigns_identity()
        {
            string previousUser = Program.OpcUaUsername;
            string previousPass = Program.OpcUaPassword;
            try
            {
                SetProgramCreds("alice", "secret");

                UAServer server = NewBareServer();
                ImpersonateEventArgs args = NewArgs(NewUserNameToken("alice", "secret"));

                InvokeHook(server, args);

                Assert.NotNull(args.Identity);
                Assert.IsAssignableFrom<IUserIdentity>(args.Identity);
            }
            finally
            {
                SetProgramCreds(previousUser, previousPass);
            }
        }

        [Fact]
        public void ImpersonateUser_rejects_unsupported_token_type_with_BadIdentityTokenInvalid()
        {
            UAServer server = NewBareServer();

            // AnonymousIdentityToken is a valid UserIdentityToken subtype but is
            // NOT a UserNameIdentityToken, so the production hook must throw
            // BadIdentityTokenInvalid for it.
            ImpersonateEventArgs args = NewArgs(new AnonymousIdentityToken());

            ServiceResultException sre = InvokeAndExpect(server, args);
            Assert.Equal((StatusCode)StatusCodes.BadIdentityTokenInvalid, sre.StatusCode);
        }

        [Fact]
        public void ImpersonateUser_propagates_BadUserAccessDenied_for_wrong_credentials()
        {
            string previousUser = Program.OpcUaUsername;
            string previousPass = Program.OpcUaPassword;
            try
            {
                SetProgramCreds("alice", "secret");

                UAServer server = NewBareServer();
                ImpersonateEventArgs args = NewArgs(NewUserNameToken("alice", "wrong"));

                ServiceResultException sre = InvokeAndExpect(server, args);
                Assert.Equal((StatusCode)StatusCodes.BadUserAccessDenied, sre.StatusCode);
            }
            finally
            {
                SetProgramCreds(previousUser, previousPass);
            }
        }

        [Fact]
        public void ImpersonateUser_propagates_BadIdentityTokenInvalid_for_empty_username()
        {
            UAServer server = NewBareServer();
            ImpersonateEventArgs args = NewArgs(NewUserNameToken(string.Empty, "secret"));

            ServiceResultException sre = InvokeAndExpect(server, args);
            Assert.Equal((StatusCode)StatusCodes.BadIdentityTokenInvalid, sre.StatusCode);
        }

        // ---- helpers ----

        private static UAServer NewBareServer()
            => (UAServer)RuntimeHelpers.GetUninitializedObject(_sut);

        private static ImpersonateEventArgs NewArgs(UserIdentityToken token)
        {
            // ImpersonateEventArgs(UserIdentityToken newIdentity, UserTokenPolicy policy, EndpointDescription endpoint)
            ConstructorInfo ctor = typeof(ImpersonateEventArgs).GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(UserIdentityToken), typeof(UserTokenPolicy), typeof(EndpointDescription) },
                modifiers: null);

            Assert.NotNull(ctor);
            return (ImpersonateEventArgs)ctor.Invoke(new object[] { token, new UserTokenPolicy(), new EndpointDescription() });
        }

        private static UserNameIdentityToken NewUserNameToken(string user, string password)
        {
            UserNameIdentityToken token = new()
            {
                UserName = user,
            };

            FieldInfo field = typeof(UserNameIdentityToken).GetField(
                "m_decryptedPassword",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(token, Encoding.UTF8.GetBytes(password ?? string.Empty));
            }
            else
            {
                PropertyInfo prop = typeof(UserNameIdentityToken).GetProperty(
                    "DecryptedPassword",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                prop?.SetValue(token, Encoding.UTF8.GetBytes(password ?? string.Empty));
            }

            return token;
        }

        private static void InvokeHook(UAServer server, ImpersonateEventArgs args)
        {
            MethodInfo m = _sut.GetMethod(
                "SessionManager_ImpersonateUser",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(m);
            // session is unused inside the hook; null is safe.
            m.Invoke(server, new object[] { null, args });
        }

        private static ServiceResultException InvokeAndExpect(UAServer server, ImpersonateEventArgs args)
        {
            MethodInfo m = _sut.GetMethod(
                "SessionManager_ImpersonateUser",
                BindingFlags.NonPublic | BindingFlags.Instance);
            TargetInvocationException tie = Assert.Throws<TargetInvocationException>(
                () => m.Invoke(server, new object[] { null, args }));
            return Assert.IsType<ServiceResultException>(tie.InnerException);
        }

        private static void SetProgramCreds(string user, string pass)
        {
            typeof(Program).GetProperty("OpcUaUsername", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, user);
            typeof(Program).GetProperty("OpcUaPassword", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, pass);
        }
    }
}
