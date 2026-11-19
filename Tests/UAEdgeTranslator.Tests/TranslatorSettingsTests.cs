namespace Opc.Ua.Edge.Translator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using Xunit;

    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class TranslatorSettingsTests : IDisposable
    {
        private const string Plain = "WOTCON_MANAGEMENT_ROLE";
        private const string Secret = "LICENSE_KEY";
        private const string RestartOnly = "DISABLE_WOTCON_REGISTRY";

        private static readonly string[] s_names = { Plain, Secret, RestartOnly };

        private readonly Dictionary<string, string> _previous = new();

        private readonly string _directory = Path.Combine(Path.GetTempPath(), "uaet-settings-" + Guid.NewGuid().ToString("N"));

        public TranslatorSettingsTests()
        {
            foreach (string name in s_names)
            {
                _previous[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        private string FilePath => Path.Combine(_directory, TranslatorSettings.FileName);

        public void Dispose()
        {
            foreach (KeyValuePair<string, string> pair in _previous)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }

            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private TranslatorSettings NewLoaded()
        {
            var settings = new TranslatorSettings(FilePath);
            settings.Load();
            return settings;
        }

        [Fact]
        public void Load_ReadsEnvironmentValue_WhenNothingPersisted()
        {
            Environment.SetEnvironmentVariable(Plain, "ns=1;s=EnvRole");

            SettingState state = NewLoaded().Get(Plain);

            Assert.Equal(SettingSource.Environment, state.Source);
            Assert.Equal("ns=1;s=EnvRole", state.EffectiveValue);
            Assert.False(state.IsPersisted);
        }

        [Fact]
        public void Get_ReportsNotSet_WhenNeitherEnvironmentNorPersisted()
        {
            SettingState state = NewLoaded().Get(Plain);

            Assert.Equal(SettingSource.NotSet, state.Source);
            Assert.False(state.HasValue);
        }

        [Fact]
        public void Set_PersistsAndAppliesToEnvironment()
        {
            Environment.SetEnvironmentVariable(Plain, "ns=1;s=EnvRole");
            TranslatorSettings settings = NewLoaded();

            SettingChangeResult result = settings.Set(Plain, "ns=1;s=SavedRole");

            Assert.True(result.Success, result.Message);
            Assert.Equal("ns=1;s=SavedRole", Environment.GetEnvironmentVariable(Plain));
            Dictionary<string, string> file = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));
            Assert.Equal("ns=1;s=SavedRole", file[Plain]);
            SettingState state = settings.Get(Plain);
            Assert.Equal(SettingSource.Persisted, state.Source);
            Assert.Equal("ns=1;s=EnvRole", state.EnvironmentValue);
        }

        [Fact]
        public void Load_PersistedValueBeatsEnvironmentAcrossRestart()
        {
            NewLoaded().Set(Plain, "ns=1;s=SavedRole");

            // Simulate a restart with the original environment value.
            Environment.SetEnvironmentVariable(Plain, "ns=1;s=EnvRole");
            SettingState state = NewLoaded().Get(Plain);

            Assert.Equal("ns=1;s=SavedRole", Environment.GetEnvironmentVariable(Plain));
            Assert.Equal(SettingSource.Persisted, state.Source);
            Assert.Equal("ns=1;s=SavedRole", state.EffectiveValue);
            Assert.Equal("ns=1;s=EnvRole", state.EnvironmentValue);
        }

        [Fact]
        public void Set_EmptyValue_PersistsUnsetOverridingEnvironment()
        {
            Environment.SetEnvironmentVariable(Plain, "ns=1;s=EnvRole");
            TranslatorSettings settings = NewLoaded();

            Assert.True(settings.Set(Plain, string.Empty).Success);

            Assert.Null(Environment.GetEnvironmentVariable(Plain));
            Environment.SetEnvironmentVariable(Plain, "ns=1;s=EnvRole");
            SettingState state = NewLoaded().Get(Plain);
            Assert.Null(Environment.GetEnvironmentVariable(Plain));
            Assert.Equal(SettingSource.Persisted, state.Source);
            Assert.False(state.HasValue);
        }

        [Fact]
        public void Reset_RestoresEnvironmentValueAndRemovesFromFile()
        {
            Environment.SetEnvironmentVariable(Plain, "ns=1;s=EnvRole");
            TranslatorSettings settings = NewLoaded();
            settings.Set(Plain, "ns=1;s=SavedRole");

            SettingChangeResult result = settings.Reset(Plain);

            Assert.True(result.Success, result.Message);
            Assert.Equal("ns=1;s=EnvRole", Environment.GetEnvironmentVariable(Plain));
            Assert.Equal(SettingSource.Environment, settings.Get(Plain).Source);
            Dictionary<string, string> file = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));
            Assert.False(file.ContainsKey(Plain));
        }

        [Fact]
        public void Set_UnknownName_IsRejected()
        {
            SettingChangeResult result = NewLoaded().Set("PATH", "x");

            Assert.False(result.Success);
            Assert.False(File.Exists(FilePath));
        }

        [Fact]
        public void Set_RequiredSettingEmpty_IsRejected()
        {
            SettingChangeResult result = NewLoaded().Set("OPCUA_USERNAME", string.Empty);

            Assert.False(result.Success);
            Assert.False(File.Exists(FilePath));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Set_AppNameEmpty_IsRejected(string value)
        {
            SettingChangeResult result = NewLoaded().Set("APP_NAME", value);

            Assert.False(result.Success);
            Assert.False(File.Exists(FilePath));
        }

        [Fact]
        public void Reset_AppName_AllowedWithoutEnvironmentValue()
        {
            string previous = Environment.GetEnvironmentVariable("APP_NAME");
            Environment.SetEnvironmentVariable("APP_NAME", null);
            try
            {
                TranslatorSettings settings = NewLoaded();
                Assert.True(settings.Set("APP_NAME", "Saved").Success);

                Assert.True(settings.Reset("APP_NAME").Success);
                Assert.Null(Environment.GetEnvironmentVariable("APP_NAME"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("APP_NAME", previous);
            }
        }

        [Fact]
        public void Load_IgnoresUnknownPersistedNames()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(FilePath, "{\"UAET_NOT_A_SETTING\":\"x\",\"" + Plain + "\":\"ns=1;s=SavedRole\"}");

            NewLoaded();

            Assert.Null(Environment.GetEnvironmentVariable("UAET_NOT_A_SETTING"));
            Assert.Equal("ns=1;s=SavedRole", Environment.GetEnvironmentVariable(Plain));
        }

        [Fact]
        public void Load_CorruptFile_FallsBackToEnvironment()
        {
            Environment.SetEnvironmentVariable(Plain, "ns=1;s=EnvRole");
            Directory.CreateDirectory(_directory);
            File.WriteAllText(FilePath, "{ not json");

            SettingState state = NewLoaded().Get(Plain);

            Assert.Equal(SettingSource.Environment, state.Source);
            Assert.Equal("ns=1;s=EnvRole", Environment.GetEnvironmentVariable(Plain));
        }

        [Fact]
        public void RequiresRestartSetting_IsFlaggedPendingUntilReload()
        {
            TranslatorSettings settings = NewLoaded();
            Assert.False(settings.Get(RestartOnly).PendingRestart);

            settings.Set(RestartOnly, "1");

            Assert.True(settings.Get(RestartOnly).PendingRestart);
            Assert.False(NewLoaded().Get(RestartOnly).PendingRestart);
        }

        [Fact]
        public void Set_RuntimeSetting_IsNotFlaggedPendingRestart()
        {
            TranslatorSettings settings = NewLoaded();

            settings.Set(Secret, "s3cret");

            Assert.False(settings.Get(Secret).PendingRestart);
            Assert.True(settings.Get(Secret).Definition.IsSecret);
        }

        [Theory]
        [InlineData("OPC_UA_GDS_ENDPOINT_URL")]
        [InlineData("REDFISH_PASSWORD")]
        [InlineData("LORAWAN_APPKEY")]
        public void DriverVariables_AreNotSettings(string name)
        {
            Assert.Null(TranslatorSettings.FindDefinition(name));
            Assert.False(NewLoaded().Set(name, "x").Success);
        }

        [Fact]
        public void Definitions_HaveUniqueNames()
        {
            Assert.Equal(
                TranslatorSettings.Definitions.Count,
                TranslatorSettings.Definitions.Select(d => d.Name).Distinct(StringComparer.Ordinal).Count());
        }
    }
}
