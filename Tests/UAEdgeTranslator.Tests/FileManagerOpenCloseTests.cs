namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua;
    using System;
    using System.Collections;
    using System.IO;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using Xunit;

    /// <summary>
    /// Reflection-driven coverage for the FileManager open/close branches that
    /// the existing state-machine tests don't already exercise: bad mode,
    /// successful read open, simultaneous-write rejection, too-many-handles,
    /// and the close-and-update unknown-handle / wrong-session paths.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class FileManagerOpenCloseTests
    {
        private static readonly Type _sut = typeof(FileManager);
        private static readonly Type _handleType = _sut.GetNestedType("Handle", BindingFlags.NonPublic);

        private static FileManager NewBareFileManager()
        {
            FileManager fm = (FileManager)RuntimeHelpers.GetUninitializedObject(_sut);

            FieldInfo handles = _sut.GetField("_handles", BindingFlags.NonPublic | BindingFlags.Instance);
            handles.SetValue(fm, Activator.CreateInstance(handles.FieldType));

            FieldInfo nextHandle = _sut.GetField("_nextHandle", BindingFlags.NonPublic | BindingFlags.Instance);
            nextHandle.SetValue(fm, 0u);

            FieldInfo writingField = _sut.GetField("_writing", BindingFlags.NonPublic | BindingFlags.Instance);
            writingField.SetValue(fm, false);

            return fm;
        }

        private static IDictionary GetHandles(FileManager fm)
            => (IDictionary)_sut
                .GetField("_handles", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(fm);

        private static bool GetWritingFlag(FileManager fm)
            => (bool)_sut
                .GetField("_writing", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(fm);

        private static void SetWritingFlag(FileManager fm, bool value)
            => _sut
                .GetField("_writing", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(fm, value);

        private static uint AddHandle(FileManager fm, MemoryStream stream, bool writing, NodeId session)
        {
            object handle = Activator.CreateInstance(_handleType);
            _handleType.GetField("Stream").SetValue(handle, stream);
            _handleType.GetField("Writing").SetValue(handle, writing);
            _handleType.GetField("SessionId").SetValue(handle, session ?? NodeId.Null);
            _handleType.GetField("Position").SetValue(handle, 0u);

            IDictionary map = GetHandles(fm);
            uint key = (uint)map.Count + 1u;
            map[key] = handle;
            return key;
        }

        [Fact]
        public void OnOpen_returns_BadNotSupported_for_unknown_mode()
        {
            FileManager fm = NewBareFileManager();
            MethodInfo onOpen = _sut.GetMethod("OnOpen", BindingFlags.NonPublic | BindingFlags.Instance);

            object[] args = new object[] { null, null, null, (byte)2, 0u };
            ServiceResult sr = (ServiceResult)onOpen.Invoke(fm, args);

            Assert.Equal((StatusCode)StatusCodes.BadNotSupported, sr.StatusCode);
        }

        [Fact]
        public void OnOpen_returns_BadInvalidState_when_writing_already_in_progress()
        {
            FileManager fm = NewBareFileManager();
            SetWritingFlag(fm, true);

            MethodInfo onOpen = _sut.GetMethod("OnOpen", BindingFlags.NonPublic | BindingFlags.Instance);

            // mode = 6 (write/read) but mode == 1 is what is allowed during a write
            object[] args = new object[] { null, null, null, (byte)6, 0u };
            ServiceResult sr = (ServiceResult)onOpen.Invoke(fm, args);

            Assert.Equal((StatusCode)StatusCodes.BadInvalidState, sr.StatusCode);
        }

        [Fact]
        public void OnOpen_returns_BadTooManyOperations_when_handle_cap_reached()
        {
            FileManager fm = NewBareFileManager();
            for (int i = 0; i < 10; i++)
            {
                AddHandle(fm, new MemoryStream(), writing: false, session: null);
            }

            MethodInfo onOpen = _sut.GetMethod("OnOpen", BindingFlags.NonPublic | BindingFlags.Instance);
            object[] args = new object[] { null, null, null, (byte)1, 0u };
            ServiceResult sr = (ServiceResult)onOpen.Invoke(fm, args);

            Assert.Equal((StatusCode)StatusCodes.BadTooManyOperations, sr.StatusCode);
        }

        [Fact]
        public void OnOpen_in_read_mode_creates_a_new_handle_with_Good_status()
        {
            FileManager fm = NewBareFileManager();

            // _file.OpenCount is dereferenced by OnOpen success path. Stub it via
            // reflection so the field write doesn't NPE.
            FieldInfo fileField = _sut.GetField("_file", BindingFlags.NonPublic | BindingFlags.Instance);
            object fakeFile = FormatterServicesShim.CreateOpenCountStub();
            fileField.SetValue(fm, fakeFile);

            MethodInfo onOpen = _sut.GetMethod("OnOpen", BindingFlags.NonPublic | BindingFlags.Instance);
            object[] args = new object[] { null, null, null, (byte)1, 0u };
            ServiceResult sr = (ServiceResult)onOpen.Invoke(fm, args);

            Assert.Equal((StatusCode)StatusCodes.Good, sr.StatusCode);
            Assert.Single(GetHandles(fm));
            Assert.False(GetWritingFlag(fm));
            Assert.NotEqual(0u, (uint)args[4]);
        }

        [Fact]
        public void OnOpen_in_write_mode_creates_handle_and_flips_writing_flag()
        {
            FileManager fm = NewBareFileManager();

            FieldInfo fileField = _sut.GetField("_file", BindingFlags.NonPublic | BindingFlags.Instance);
            object fakeFile = FormatterServicesShim.CreateOpenCountStub();
            fileField.SetValue(fm, fakeFile);

            MethodInfo onOpen = _sut.GetMethod("OnOpen", BindingFlags.NonPublic | BindingFlags.Instance);
            object[] args = new object[] { null, null, null, (byte)6, 0u };
            ServiceResult sr = (ServiceResult)onOpen.Invoke(fm, args);

            Assert.Equal((StatusCode)StatusCodes.Good, sr.StatusCode);
            Assert.True(GetWritingFlag(fm));
        }

        [Fact]
        public void OnCloseAndUpdate_returns_BadInvalidArgument_for_unknown_handle()
        {
            FileManager fm = NewBareFileManager();
            MethodInfo onClose = _sut.GetMethod("OnCloseAndUpdate", BindingFlags.NonPublic | BindingFlags.Instance);

            ServiceResult sr = (ServiceResult)onClose.Invoke(fm, new object[] { null, null, null, 12345u });
            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, sr.StatusCode);
        }

        [Fact]
        public void OnCloseAndUpdate_returns_BadUserAccessDenied_for_wrong_session()
        {
            FileManager fm = NewBareFileManager();
            uint h = AddHandle(fm, new MemoryStream(), writing: false, session: new NodeId(99, 1));

            MethodInfo onClose = _sut.GetMethod("OnCloseAndUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
            ServiceResult sr = (ServiceResult)onClose.Invoke(fm, new object[] { null, null, null, h });

            Assert.Equal((StatusCode)StatusCodes.BadUserAccessDenied, sr.StatusCode);
        }
    }

    /// <summary>
    /// FileManager stores a reference to <c>FileState</c> with public properties
    /// like <c>OpenCount</c> wired to the SDK PropertyState. We need a stub
    /// instance whose <c>OpenCount.Value</c> assignment doesn't NPE.
    /// </summary>
    internal static class FormatterServicesShim
    {
        public static object CreateOpenCountStub()
        {
            // Use FileState (which exposes a settable OpenCount property surface).
            Opc.Ua.FileState fs = new(null);
            fs.OpenCount = new Opc.Ua.PropertyState<ushort>(fs);
            return fs;
        }
    }
}
