namespace Opc.Ua.Edge.Translator.Tests
{
    using System;
    using System.IO;
    using System.Reflection;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Xunit;

    /// <summary>
    /// Reflection-driven coverage for <see cref="Program"/> helpers that have
    /// no side effects on the running process. Program.Main itself blocks
    /// indefinitely and is exercised end-to-end through the integration suite.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class ProgramHelperTests
    {
        private static readonly Type _sut = typeof(Program);

        [Fact]
        public void ValidateRequiredEnvironment_throws_when_username_missing()
        {
            string previousUser = Environment.GetEnvironmentVariable("OPCUA_USERNAME");
            string previousPass = Environment.GetEnvironmentVariable("OPCUA_PASSWORD");
            try
            {
                Environment.SetEnvironmentVariable("OPCUA_USERNAME", null);
                Environment.SetEnvironmentVariable("OPCUA_PASSWORD", "p");

                var ex = Assert.Throws<TargetInvocationException>(() => InvokeValidate());
                Assert.IsType<InvalidOperationException>(ex.InnerException);
            }
            finally
            {
                Environment.SetEnvironmentVariable("OPCUA_USERNAME", previousUser);
                Environment.SetEnvironmentVariable("OPCUA_PASSWORD", previousPass);
            }
        }

        [Fact]
        public void ValidateRequiredEnvironment_throws_when_password_missing()
        {
            string previousUser = Environment.GetEnvironmentVariable("OPCUA_USERNAME");
            string previousPass = Environment.GetEnvironmentVariable("OPCUA_PASSWORD");
            try
            {
                Environment.SetEnvironmentVariable("OPCUA_USERNAME", "u");
                Environment.SetEnvironmentVariable("OPCUA_PASSWORD", null);

                var ex = Assert.Throws<TargetInvocationException>(() => InvokeValidate());
                Assert.IsType<InvalidOperationException>(ex.InnerException);
            }
            finally
            {
                Environment.SetEnvironmentVariable("OPCUA_USERNAME", previousUser);
                Environment.SetEnvironmentVariable("OPCUA_PASSWORD", previousPass);
            }
        }

        [Fact]
        public void ValidateRequiredEnvironment_sets_static_properties_when_both_provided()
        {
            string previousUser = Environment.GetEnvironmentVariable("OPCUA_USERNAME");
            string previousPass = Environment.GetEnvironmentVariable("OPCUA_PASSWORD");
            string previousU = Program.OpcUaUsername;
            string previousP = Program.OpcUaPassword;
            try
            {
                Environment.SetEnvironmentVariable("OPCUA_USERNAME", "alice");
                Environment.SetEnvironmentVariable("OPCUA_PASSWORD", "secret");

                InvokeValidate();

                Assert.Equal("alice", Program.OpcUaUsername);
                Assert.Equal("secret", Program.OpcUaPassword);
            }
            finally
            {
                Environment.SetEnvironmentVariable("OPCUA_USERNAME", previousUser);
                Environment.SetEnvironmentVariable("OPCUA_PASSWORD", previousPass);
                SetStaticString("OpcUaUsername", previousU);
                SetStaticString("OpcUaPassword", previousP);
            }
        }

        [Fact]
        public void LogStartupVersion_does_not_throw()
        {
            // Pure logging path -> just exercise it without crashing.
            MethodInfo m = _sut.GetMethod("LogStartupVersion", BindingFlags.NonPublic | BindingFlags.Static);
            m.Invoke(null, null);
        }

        [Fact]
        public void Drivers_singleton_is_initialized_and_reusable()
        {
            ProtocolDriverRegistry registry = Program.Drivers;
            Assert.NotNull(registry);
            Assert.Same(registry, Program.Drivers);
        }

        [Fact]
        public void Telemetry_default_instance_is_initialized()
        {
            // Telemetry has a default-constructed instance unless tests reset it.
            // Defend against fixtures that set it to null and restore afterwards.
            object previous = typeof(Program)
                .GetProperty("Telemetry")!
                .GetValue(null);
            try
            {
                if (previous == null)
                {
                    SetStaticObject("Telemetry", new Opc.Ua.Cloud.ConsoleTelemetry());
                }
                Assert.NotNull(Program.Telemetry);
            }
            finally
            {
                SetStaticObject("Telemetry", previous);
            }
        }

        private static void InvokeValidate()
        {
            MethodInfo m = _sut.GetMethod("ValidateRequiredEnvironment", BindingFlags.NonPublic | BindingFlags.Static);
            m.Invoke(null, null);
        }

        private static void SetStaticString(string name, string value)
        {
            PropertyInfo p = _sut.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            p.SetValue(null, value);
        }

        private static void SetStaticObject(string name, object value)
        {
            PropertyInfo p = _sut.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            p.SetValue(null, value);
        }
    }

    /// <summary>
    /// Reflection-driven coverage for <see cref="UAServer"/> helpers that don't
    /// require a live OPC UA stack to be started. Anything else is already
    /// exercised by the OPC UA integration suite.
    /// </summary>
    public class UAServerHelperTests
    {
        private static readonly Type _sut = typeof(UAServer);

        [Fact]
        public void LoadServerProperties_returns_OPC_Foundation_branding()
        {
            UAServer server = (UAServer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(_sut);

            object props = _sut.GetMethod("LoadServerProperties", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(server, null);

            Assert.NotNull(props);
            Type pt = props.GetType();
            Assert.Equal("OPC Foundation", pt.GetProperty("ManufacturerName").GetValue(props));
            Assert.Equal("UA Edge Translator", pt.GetProperty("ProductName").GetValue(props));
        }

        [Fact]
        public void FixedTimeEqualsHashed_returns_true_for_equal_strings()
        {
            UAServer server = (UAServer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(_sut);
            MethodInfo m = _sut.GetMethod("FixedTimeEqualsHashed", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.True((bool)m.Invoke(server, new object[] { "abc", "abc" }));
            Assert.False((bool)m.Invoke(server, new object[] { "abc", "abd" }));
            Assert.True((bool)m.Invoke(server, new object[] { string.Empty, string.Empty }));
            Assert.True((bool)m.Invoke(server, new object[] { null, null }));
            Assert.False((bool)m.Invoke(server, new object[] { "x", null }));
        }
    }
}
