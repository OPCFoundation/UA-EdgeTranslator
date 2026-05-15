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
    /// Drives the <see cref="FileManager"/> read/write/get-position/set-position
    /// state machine via reflection. Avoids the SDK's <c>FileState</c> wiring by
    /// seeding handles into the private <c>_handles</c> dictionary directly,
    /// which lets us cover every status-code branch documented on the
    /// FileType callbacks (BadInvalidArgument, BadUserAccessDenied,
    /// BadInvalidState, BadOutOfMemory, success).
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class FileManagerStateMachineTests
    {
        private static readonly Type _sut = typeof(FileManager);
        private static readonly Type _handleType = _sut.GetNestedType("Handle", BindingFlags.NonPublic);

        private const uint StatusCodeGood = StatusCodes.Good;

        private static FileManager NewBareFileManager(int maxBytes = 16)
        {
            FileManager fm = (FileManager)RuntimeHelpers.GetUninitializedObject(_sut);

            FieldInfo handles = _sut.GetField("_handles", BindingFlags.NonPublic | BindingFlags.Instance);
            handles.SetValue(fm, Activator.CreateInstance(handles.FieldType));
            FieldInfo nextHandle = _sut.GetField("_nextHandle", BindingFlags.NonPublic | BindingFlags.Instance);
            nextHandle.SetValue(fm, 0u);
            FieldInfo maxField = _sut.GetField("_maxFileBytes", BindingFlags.NonPublic | BindingFlags.Instance);
            maxField.SetValue(fm, maxBytes);
            FieldInfo writingField = _sut.GetField("_writing", BindingFlags.NonPublic | BindingFlags.Instance);
            writingField.SetValue(fm, false);

            return fm;
        }

        private static uint AddHandle(FileManager fm, MemoryStream stream, bool writing, NodeId session = null)
        {
            object handle = Activator.CreateInstance(_handleType);
            _handleType.GetField("Stream").SetValue(handle, stream);
            _handleType.GetField("Writing").SetValue(handle, writing);
            _handleType.GetField("SessionId").SetValue(handle, session ?? NodeId.Null);
            _handleType.GetField("Position").SetValue(handle, 0u);

            FieldInfo handlesField = _sut.GetField("_handles", BindingFlags.NonPublic | BindingFlags.Instance);
            IDictionary map = (IDictionary)handlesField.GetValue(fm);
            uint key = (uint)map.Count + 1u;
            map[key] = handle;
            return key;
        }

        [Fact]
        public void OnRead_returns_zero_bytes_for_empty_stream()
        {
            FileManager fm = NewBareFileManager();
            uint h = AddHandle(fm, new MemoryStream(), writing: false);

            MethodInfo onRead = _sut.GetMethod("OnRead", BindingFlags.NonPublic | BindingFlags.Instance);
            object[] args = new object[] { null, null, null, h, 32, null };
            ServiceResult sr = (ServiceResult)onRead.Invoke(fm, args);
            Assert.Equal(StatusCodeGood, (uint)sr.StatusCode);

            byte[] data = (byte[])args[5];
            Assert.NotNull(data);
            Assert.Empty(data);
        }

        [Fact]
        public void OnRead_returns_BadInvalidState_when_handle_is_writing()
        {
            FileManager fm = NewBareFileManager();
            uint h = AddHandle(fm, new MemoryStream(), writing: true);

            MethodInfo onRead = _sut.GetMethod("OnRead", BindingFlags.NonPublic | BindingFlags.Instance);
            object[] args = new object[] { null, null, null, h, 4, null };
            ServiceResult sr = (ServiceResult)onRead.Invoke(fm, args);
            Assert.Equal((StatusCode)StatusCodes.BadInvalidState, sr.StatusCode);
        }

        [Fact]
        public void OnRead_returns_partial_buffer_when_stream_is_short()
        {
            FileManager fm = NewBareFileManager();
            uint h = AddHandle(fm, new MemoryStream(new byte[] { 9, 8, 7 }), writing: false);

            MethodInfo onRead = _sut.GetMethod("OnRead", BindingFlags.NonPublic | BindingFlags.Instance);
            object[] args = new object[] { null, null, null, h, 16, null };
            ServiceResult sr = (ServiceResult)onRead.Invoke(fm, args);
            Assert.Equal(StatusCodeGood, (uint)sr.StatusCode);

            byte[] data = (byte[])args[5];
            Assert.Equal(new byte[] { 9, 8, 7 }, data);
        }

        [Fact]
        public void OnRead_with_zero_length_returns_empty_array()
        {
            FileManager fm = NewBareFileManager();
            uint h = AddHandle(fm, new MemoryStream(new byte[] { 1, 2, 3 }), writing: false);

            MethodInfo onRead = _sut.GetMethod("OnRead", BindingFlags.NonPublic | BindingFlags.Instance);
            object[] args = new object[] { null, null, null, h, 0, null };
            ServiceResult sr = (ServiceResult)onRead.Invoke(fm, args);
            Assert.Equal(StatusCodeGood, (uint)sr.StatusCode);

            byte[] data = (byte[])args[5];
            Assert.NotNull(data);
            Assert.Empty(data);
        }

        [Fact]
        public void OnWrite_returns_BadInvalidState_when_handle_is_read_only()
        {
            FileManager fm = NewBareFileManager();
            uint h = AddHandle(fm, new MemoryStream(), writing: false);

            MethodInfo onWrite = _sut.GetMethod("OnWrite", BindingFlags.NonPublic | BindingFlags.Instance);
            ServiceResult sr = (ServiceResult)onWrite.Invoke(fm, new object[] { null, null, null, h, new byte[] { 0xAA } });
            Assert.Equal((StatusCode)StatusCodes.BadInvalidState, sr.StatusCode);
        }

        [Fact]
        public void OnWrite_caps_payload_with_BadOutOfMemory()
        {
            FileManager fm = NewBareFileManager(maxBytes: 8);
            uint h = AddHandle(fm, new MemoryStream(), writing: true);

            MethodInfo onWrite = _sut.GetMethod("OnWrite", BindingFlags.NonPublic | BindingFlags.Instance);
            ServiceResult sr = (ServiceResult)onWrite.Invoke(fm, new object[] { null, null, null, h, new byte[64] });
            Assert.Equal((StatusCode)StatusCodes.BadOutOfMemory, sr.StatusCode);
        }

        [Fact]
        public void OnWrite_appends_to_stream_when_under_cap()
        {
            FileManager fm = NewBareFileManager(maxBytes: 1024);
            MemoryStream ms = new();
            uint h = AddHandle(fm, ms, writing: true);

            MethodInfo onWrite = _sut.GetMethod("OnWrite", BindingFlags.NonPublic | BindingFlags.Instance);
            ServiceResult sr = (ServiceResult)onWrite.Invoke(fm, new object[] { null, null, null, h, new byte[] { 1, 2, 3, 4 } });
            Assert.Equal(StatusCodeGood, (uint)sr.StatusCode);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, ms.ToArray());

            sr = (ServiceResult)onWrite.Invoke(fm, new object[] { null, null, null, h, (byte[])null });
            Assert.Equal(StatusCodeGood, (uint)sr.StatusCode);
        }

        [Fact]
        public void OnGetPosition_and_OnSetPosition_round_trip()
        {
            FileManager fm = NewBareFileManager(maxBytes: 1024);
            MemoryStream ms = new(new byte[] { 1, 2, 3 });
            ms.Seek(3, SeekOrigin.Begin);
            uint h = AddHandle(fm, ms, writing: false);

            MethodInfo onGet = _sut.GetMethod("OnGetPosition", BindingFlags.NonPublic | BindingFlags.Instance);
            object[] getArgs = new object[] { null, null, null, h, 0UL };
            ServiceResult sr = (ServiceResult)onGet.Invoke(fm, getArgs);
            Assert.Equal(StatusCodeGood, (uint)sr.StatusCode);
            Assert.Equal(3UL, (ulong)getArgs[4]);

            MethodInfo onSet = _sut.GetMethod("OnSetPosition", BindingFlags.NonPublic | BindingFlags.Instance);
            sr = (ServiceResult)onSet.Invoke(fm, new object[] { null, null, null, h, 0UL });
            Assert.Equal(StatusCodeGood, (uint)sr.StatusCode);

            sr = (ServiceResult)onGet.Invoke(fm, getArgs);
            Assert.Equal(0UL, (ulong)getArgs[4]);
        }

        [Fact]
        public void Find_throws_BadInvalidArgument_for_unknown_handle()
        {
            FileManager fm = NewBareFileManager();
            MethodInfo find = _sut.GetMethod("Find", BindingFlags.NonPublic | BindingFlags.Instance);

            TargetInvocationException tie = Assert.Throws<TargetInvocationException>(
                () => find.Invoke(fm, new object[] { null, 999u }));
            ServiceResultException sre = Assert.IsType<ServiceResultException>(tie.InnerException);
            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, sre.StatusCode);
        }

        [Fact]
        public void Find_throws_BadUserAccessDenied_when_session_does_not_match()
        {
            FileManager fm = NewBareFileManager();
            uint h = AddHandle(fm, new MemoryStream(), writing: false, session: new NodeId(42, 1));

            MethodInfo find = _sut.GetMethod("Find", BindingFlags.NonPublic | BindingFlags.Instance);

            TargetInvocationException tie = Assert.Throws<TargetInvocationException>(
                () => find.Invoke(fm, new object[] { null, h }));
            ServiceResultException sre = Assert.IsType<ServiceResultException>(tie.InnerException);
            Assert.Equal((StatusCode)StatusCodes.BadUserAccessDenied, sre.StatusCode);
        }

        [Fact]
        public void Dispose_clears_all_handles()
        {
            FileManager fm = NewBareFileManager();
            AddHandle(fm, new MemoryStream(), writing: false);
            AddHandle(fm, new MemoryStream(), writing: true);

            FieldInfo handlesField = _sut.GetField("_handles", BindingFlags.NonPublic | BindingFlags.Instance);
            IDictionary map = (IDictionary)handlesField.GetValue(fm);
            Assert.Equal(2, map.Count);

            fm.Dispose();
            Assert.Empty(map);
        }
    }
}
