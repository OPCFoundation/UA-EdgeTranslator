namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua;
    using System;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Text;
    using Xunit;

    /// <summary>
    /// Reflection-driven coverage for <see cref="UAServer.VerifyPassword"/>.
    /// Exercises every status-code branch (empty username, empty password,
    /// missing configured credentials, mismatch, success) without standing up
    /// a real OPC UA SDK server.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class UAServerVerifyPasswordTests
    {
        private static readonly Type _sut = typeof(UAServer);

        [Fact]
        public void VerifyPassword_throws_BadIdentityTokenInvalid_for_empty_username()
        {
            UAServer server = NewBareServer();
            UserNameIdentityToken token = NewToken(string.Empty, "p");

            ServiceResultException sre = InvokeAndExpect(server, token);
            Assert.Equal((StatusCode)StatusCodes.BadIdentityTokenInvalid, sre.StatusCode);
        }

        [Fact]
        public void VerifyPassword_throws_BadIdentityTokenRejected_for_empty_password()
        {
            UAServer server = NewBareServer();
            UserNameIdentityToken token = NewToken("alice", string.Empty);

            ServiceResultException sre = InvokeAndExpect(server, token);
            Assert.Equal((StatusCode)StatusCodes.BadIdentityTokenRejected, sre.StatusCode);
        }

        [Fact]
        public void VerifyPassword_throws_BadUserAccessDenied_when_credentials_not_configured()
        {
            string previousUser = Program.OpcUaUsername;
            string previousPass = Program.OpcUaPassword;
            try
            {
                SetProgramCreds(null, null);

                UAServer server = NewBareServer();
                UserNameIdentityToken token = NewToken("alice", "secret");

                ServiceResultException sre = InvokeAndExpect(server, token);
                Assert.Equal((StatusCode)StatusCodes.BadUserAccessDenied, sre.StatusCode);
            }
            finally
            {
                SetProgramCreds(previousUser, previousPass);
            }
        }

        [Fact]
        public void VerifyPassword_throws_BadUserAccessDenied_for_wrong_credentials()
        {
            string previousUser = Program.OpcUaUsername;
            string previousPass = Program.OpcUaPassword;
            try
            {
                SetProgramCreds("alice", "secret");

                UAServer server = NewBareServer();
                UserNameIdentityToken token = NewToken("alice", "wrong");

                ServiceResultException sre = InvokeAndExpect(server, token);
                Assert.Equal((StatusCode)StatusCodes.BadUserAccessDenied, sre.StatusCode);
            }
            finally
            {
                SetProgramCreds(previousUser, previousPass);
            }
        }

        [Fact]
        public void VerifyPassword_returns_identity_for_correct_credentials()
        {
            string previousUser = Program.OpcUaUsername;
            string previousPass = Program.OpcUaPassword;
            try
            {
                SetProgramCreds("alice", "secret");

                UAServer server = NewBareServer();
                UserNameIdentityToken token = NewToken("alice", "secret");

                MethodInfo m = _sut.GetMethod("VerifyPassword", BindingFlags.NonPublic | BindingFlags.Instance);
                object identity = m.Invoke(server, new object[] { token });

                Assert.NotNull(identity);
                Assert.IsAssignableFrom<IUserIdentity>(identity);
            }
            finally
            {
                SetProgramCreds(previousUser, previousPass);
            }
        }

        private static UAServer NewBareServer()
            => (UAServer)RuntimeHelpers.GetUninitializedObject(_sut);

        private static UserNameIdentityToken NewToken(string user, string password)
        {
            UserNameIdentityToken token = new()
            {
                UserName = user,
            };

            // Reflection: DecryptedPassword is an internal byte[] that the SDK
            // populates after token decryption; we need to seed it directly.
            FieldInfo field = typeof(UserNameIdentityToken).GetField(
                "m_decryptedPassword",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(token, Encoding.UTF8.GetBytes(password ?? string.Empty));
            }
            else
            {
                // Newer SDK versions expose it as a property setter.
                PropertyInfo prop = typeof(UserNameIdentityToken).GetProperty(
                    "DecryptedPassword",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                prop?.SetValue(token, Encoding.UTF8.GetBytes(password ?? string.Empty));
            }

            return token;
        }

        private static ServiceResultException InvokeAndExpect(UAServer server, UserNameIdentityToken token)
        {
            MethodInfo m = _sut.GetMethod("VerifyPassword", BindingFlags.NonPublic | BindingFlags.Instance);
            TargetInvocationException tie = Assert.Throws<TargetInvocationException>(
                () => m.Invoke(server, new object[] { token }));
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
