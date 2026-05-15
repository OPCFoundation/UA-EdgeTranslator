namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua;
    using System;
    using System.IO;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using Xunit;

    /// <summary>
    /// Reflection-driven coverage for the property/early-return branches of
    /// <see cref="UANodeManager.OnReadValue"/> and
    /// <see cref="UANodeManager.OnWriteValue"/> that don't require a wired
    /// OPC UA address space.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class UANodeManagerOnReadWriteTests
    {
        private static readonly Type _sut = typeof(UANodeManager);
        private const string _envIgnoreProvisioning = "IGNORE_PROVISIONING_MODE";

        private static UANodeManager NewBareInstance()
        {
            UANodeManager nm = (UANodeManager)RuntimeHelpers.GetUninitializedObject(_sut);
            EnsureField(nm, "_reconnectStates");
            EnsureField(nm, "_uaVariables");
            EnsureField(nm, "_uaProperties");
            EnsureField(nm, "_assets");
            EnsureField(nm, "_tags");
            EnsureField(nm, "_tagIndex");
            EnsureField(nm, "_fileManagers");
            EnsureProgramTelemetry();
            return nm;
        }

        private static void EnsureField(object instance, string name)
        {
            FieldInfo field = _sut.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field?.GetValue(instance) == null && field != null)
            {
                field.SetValue(instance, Activator.CreateInstance(field.FieldType));
            }
        }

        private static void EnsureProgramTelemetry()
        {
            PropertyInfo prop = typeof(Program).GetProperty(nameof(Program.Telemetry), BindingFlags.Public | BindingFlags.Static);
            if (prop.GetValue(null) == null)
            {
                prop.SetValue(null, new Opc.Ua.Cloud.ConsoleTelemetry());
            }
        }

        [Fact]
        public void OnReadValue_returns_BadNotReadable_in_provisioning_mode()
        {
            using TestWorkingDirectory tmp = new();
            // No pki/issuer/certs => provisioning mode is active.
            Directory.CreateDirectory(Path.Combine(tmp.Path, "pki", "issuer", "certs"));
            // Empty cert dir => still provisioning mode. Don't set IGNORE_PROVISIONING_MODE.
            string previous = Environment.GetEnvironmentVariable(_envIgnoreProvisioning);
            try
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, null);

                UANodeManager nm = NewBareInstance();
                MethodInfo m = _sut.GetMethod("OnReadValue", BindingFlags.Public | BindingFlags.Instance);

                object[] args = new object[] { null, null, null, null, null, (StatusCode)0, DateTime.MinValue };
                ServiceResult sr = (ServiceResult)m.Invoke(nm, args);

                Assert.Equal((StatusCode)StatusCodes.BadNotReadable, sr.StatusCode);
            }
            finally
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, previous);
            }
        }

        [Fact]
        public void OnWriteValue_returns_BadNotWritable_in_provisioning_mode()
        {
            using TestWorkingDirectory tmp = new();
            Directory.CreateDirectory(Path.Combine(tmp.Path, "pki", "issuer", "certs"));
            string previous = Environment.GetEnvironmentVariable(_envIgnoreProvisioning);
            try
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, null);

                UANodeManager nm = NewBareInstance();
                MethodInfo m = _sut.GetMethod("OnWriteValue", BindingFlags.Public | BindingFlags.Instance);

                object value = null;
                StatusCode statusCode = StatusCodes.Good;
                DateTime ts = DateTime.MinValue;

                object[] args = new object[] { null, null, null, null, value, statusCode, ts };
                ServiceResult sr = (ServiceResult)m.Invoke(nm, args);

                Assert.Equal((StatusCode)StatusCodes.BadNotWritable, sr.StatusCode);
            }
            finally
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, previous);
            }
        }

        [Fact]
        public void OnWriteValue_writes_SupportedWoTBindings_property_with_Good_status_outside_provisioning()
        {
            using TestWorkingDirectory tmp = new();
            Directory.CreateDirectory(Path.Combine(tmp.Path, "pki", "issuer", "certs"));
            string previous = Environment.GetEnvironmentVariable(_envIgnoreProvisioning);
            try
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, "1");

                UANodeManager nm = NewBareInstance();
                MethodInfo m = _sut.GetMethod("OnWriteValue", BindingFlags.Public | BindingFlags.Instance);

                PropertyState property = new(null)
                {
                    DisplayName = new LocalizedText("SupportedWoTBindings"),
                    BrowseName = new QualifiedName("SupportedWoTBindings")
                };

                object value = new[] { "uri" };
                StatusCode statusCode = StatusCodes.Bad;
                DateTime ts = DateTime.MinValue;

                object[] args = new object[] { null, property, null, null, value, statusCode, ts };
                ServiceResult sr = (ServiceResult)m.Invoke(nm, args);

                Assert.Equal((StatusCode)StatusCodes.Good, sr.StatusCode);
                Assert.Equal((StatusCode)StatusCodes.Good, (StatusCode)args[5]);
            }
            finally
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, previous);
            }
        }

        [Fact]
        public void OnWriteValue_writes_SupportedOPCUAInfoModels_property_with_Good_status_outside_provisioning()
        {
            using TestWorkingDirectory tmp = new();
            Directory.CreateDirectory(Path.Combine(tmp.Path, "pki", "issuer", "certs"));
            string previous = Environment.GetEnvironmentVariable(_envIgnoreProvisioning);
            try
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, "1");

                UANodeManager nm = NewBareInstance();
                MethodInfo m = _sut.GetMethod("OnWriteValue", BindingFlags.Public | BindingFlags.Instance);

                PropertyState property = new(null)
                {
                    DisplayName = new LocalizedText("SupportedOPCUAInfoModels"),
                    BrowseName = new QualifiedName("SupportedOPCUAInfoModels")
                };

                object value = new[] { "x.nodeset2.xml" };
                StatusCode statusCode = StatusCodes.Bad;
                DateTime ts = DateTime.MinValue;

                object[] args = new object[] { null, property, null, null, value, statusCode, ts };
                ServiceResult sr = (ServiceResult)m.Invoke(nm, args);

                Assert.Equal((StatusCode)StatusCodes.Good, sr.StatusCode);
            }
            finally
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, previous);
            }
        }

        [Fact]
        public void OnWriteValue_License_returns_BadInvalidArgument_when_value_empty()
        {
            using TestWorkingDirectory tmp = new();
            Directory.CreateDirectory(Path.Combine(tmp.Path, "pki", "issuer", "certs"));
            string previous = Environment.GetEnvironmentVariable(_envIgnoreProvisioning);
            try
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, "1");

                UANodeManager nm = NewBareInstance();
                MethodInfo m = _sut.GetMethod("OnWriteValue", BindingFlags.Public | BindingFlags.Instance);

                PropertyState property = new(null)
                {
                    DisplayName = new LocalizedText("License"),
                    BrowseName = new QualifiedName("License")
                };

                object[] args = new object[] { null, property, null, null, "", (StatusCode)StatusCodes.Good, DateTime.MinValue };
                ServiceResult sr = (ServiceResult)m.Invoke(nm, args);

                Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, sr.StatusCode);
            }
            finally
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, previous);
            }
        }

        [Fact]
        public void OnWriteValue_License_returns_BadNotSupported_when_LICENSE_KEY_unset()
        {
            using TestWorkingDirectory tmp = new();
            Directory.CreateDirectory(Path.Combine(tmp.Path, "pki", "issuer", "certs"));
            string previous = Environment.GetEnvironmentVariable(_envIgnoreProvisioning);
            string previousLicense = Environment.GetEnvironmentVariable("LICENSE_KEY");
            try
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, "1");
                Environment.SetEnvironmentVariable("LICENSE_KEY", null);

                UANodeManager nm = NewBareInstance();
                MethodInfo m = _sut.GetMethod("OnWriteValue", BindingFlags.Public | BindingFlags.Instance);

                PropertyState property = new(null)
                {
                    DisplayName = new LocalizedText("License"),
                    BrowseName = new QualifiedName("License")
                };

                object[] args = new object[] { null, property, null, null, "some-key", (StatusCode)StatusCodes.Good, DateTime.MinValue };
                ServiceResult sr = (ServiceResult)m.Invoke(nm, args);

                Assert.Equal((StatusCode)StatusCodes.BadNotSupported, sr.StatusCode);
            }
            finally
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, previous);
                Environment.SetEnvironmentVariable("LICENSE_KEY", previousLicense);
            }
        }

        [Fact]
        public void OnWriteValue_License_returns_BadInvalidArgument_when_key_does_not_match()
        {
            using TestWorkingDirectory tmp = new();
            Directory.CreateDirectory(Path.Combine(tmp.Path, "pki", "issuer", "certs"));
            string previous = Environment.GetEnvironmentVariable(_envIgnoreProvisioning);
            string previousLicense = Environment.GetEnvironmentVariable("LICENSE_KEY");
            try
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, "1");
                Environment.SetEnvironmentVariable("LICENSE_KEY", "expected-license");

                UANodeManager nm = NewBareInstance();
                MethodInfo m = _sut.GetMethod("OnWriteValue", BindingFlags.Public | BindingFlags.Instance);

                PropertyState property = new(null)
                {
                    DisplayName = new LocalizedText("License"),
                    BrowseName = new QualifiedName("License")
                };

                object[] args = new object[] { null, property, null, null, "wrong-license", (StatusCode)StatusCodes.Good, DateTime.MinValue };
                ServiceResult sr = (ServiceResult)m.Invoke(nm, args);

                Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, sr.StatusCode);
            }
            finally
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, previous);
                Environment.SetEnvironmentVariable("LICENSE_KEY", previousLicense);
            }
        }

        [Fact]
        public void OnWriteValue_MemoryWorkingSet_variable_returns_Good_outside_provisioning()
        {
            using TestWorkingDirectory tmp = new();
            Directory.CreateDirectory(Path.Combine(tmp.Path, "pki", "issuer", "certs"));
            string previous = Environment.GetEnvironmentVariable(_envIgnoreProvisioning);
            try
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, "1");

                UANodeManager nm = NewBareInstance();
                MethodInfo m = _sut.GetMethod("OnWriteValue", BindingFlags.Public | BindingFlags.Instance);

                BaseDataVariableState variable = new(null)
                {
                    DisplayName = new LocalizedText("MemoryWorkingSet"),
                    BrowseName = new QualifiedName("MemoryWorkingSet"),
                    NodeId = new NodeId(99u, 0)
                };

                object[] args = new object[] { null, variable, null, null, 42, (StatusCode)StatusCodes.Bad, DateTime.MinValue };
                ServiceResult sr = (ServiceResult)m.Invoke(nm, args);

                Assert.Equal((StatusCode)StatusCodes.Good, sr.StatusCode);
            }
            finally
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, previous);
            }
        }

        [Fact]
        public void OnWriteValue_returns_Good_when_node_is_neither_property_nor_variable()
        {
            using TestWorkingDirectory tmp = new();
            Directory.CreateDirectory(Path.Combine(tmp.Path, "pki", "issuer", "certs"));
            string previous = Environment.GetEnvironmentVariable(_envIgnoreProvisioning);
            try
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, "1");

                UANodeManager nm = NewBareInstance();
                MethodInfo m = _sut.GetMethod("OnWriteValue", BindingFlags.Public | BindingFlags.Instance);

                BaseObjectState obj = new(null)
                {
                    DisplayName = new LocalizedText("SomeObject"),
                    BrowseName = new QualifiedName("SomeObject"),
                    NodeId = new NodeId(99u, 0)
                };

                object[] args = new object[] { null, obj, null, null, null, (StatusCode)StatusCodes.Bad, DateTime.MinValue };
                ServiceResult sr = (ServiceResult)m.Invoke(nm, args);

                Assert.Equal((StatusCode)StatusCodes.Good, sr.StatusCode);
            }
            finally
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, previous);
            }
        }

        [Fact]
        public void OnReadValue_MemoryWorkingSet_returns_positive_value_outside_provisioning()
        {
            using TestWorkingDirectory tmp = new();
            Directory.CreateDirectory(Path.Combine(tmp.Path, "pki", "issuer", "certs"));
            string previous = Environment.GetEnvironmentVariable(_envIgnoreProvisioning);
            try
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, "1");

                UANodeManager nm = NewBareInstance();
                MethodInfo m = _sut.GetMethod("OnReadValue", BindingFlags.Public | BindingFlags.Instance);

                BaseDataVariableState variable = new(null)
                {
                    DisplayName = new LocalizedText("MemoryWorkingSet(MB)"),
                    BrowseName = new QualifiedName("MemoryWorkingSet(MB)"),
                    NodeId = new NodeId(99u, 0)
                };

                object[] args = new object[] { null, variable, null, null, null, (StatusCode)0, DateTime.MinValue };
                ServiceResult sr = (ServiceResult)m.Invoke(nm, args);

                Assert.Equal((StatusCode)StatusCodes.Good, sr.StatusCode);
                Assert.True(Convert.ToInt64(args[4]) > 0);
            }
            finally
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, previous);
            }
        }

        [Fact]
        public void OnReadValue_returns_Good_when_node_is_neither_property_nor_variable()
        {
            using TestWorkingDirectory tmp = new();
            Directory.CreateDirectory(Path.Combine(tmp.Path, "pki", "issuer", "certs"));
            string previous = Environment.GetEnvironmentVariable(_envIgnoreProvisioning);
            try
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, "1");

                UANodeManager nm = NewBareInstance();
                MethodInfo m = _sut.GetMethod("OnReadValue", BindingFlags.Public | BindingFlags.Instance);

                BaseObjectState obj = new(null)
                {
                    DisplayName = new LocalizedText("SomeObject"),
                    BrowseName = new QualifiedName("SomeObject"),
                    NodeId = new NodeId(99u, 0)
                };

                object[] args = new object[] { null, obj, null, null, null, (StatusCode)0, DateTime.MinValue };
                ServiceResult sr = (ServiceResult)m.Invoke(nm, args);

                Assert.Equal((StatusCode)StatusCodes.Good, sr.StatusCode);
            }
            finally
            {
                Environment.SetEnvironmentVariable(_envIgnoreProvisioning, previous);
            }
        }
    }
}
