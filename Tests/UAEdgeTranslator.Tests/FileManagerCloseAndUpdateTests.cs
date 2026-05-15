namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua;
    using System;
    using System.Collections;
    using System.IO;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Text;
    using Xunit;

    /// <summary>
    /// Reflection-driven coverage for the body of <see cref="FileManager.OnCloseAndUpdate"/>
    /// that the existing FileManager test suite stops short of: the catch branch
    /// returning <see cref="StatusCodes.BadDecodingError"/> and the
    /// <c>AtomicWriteAllText</c> error/cleanup path. These branches are
    /// otherwise unreachable in a unit-test process because they require either
    /// a fully-initialized OPC UA node manager or a deliberately-failing IO call.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class FileManagerCloseAndUpdateTests
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

        private static uint AddHandle(FileManager fm, MemoryStream stream, NodeId session = null)
        {
            object handle = Activator.CreateInstance(_handleType);
            _handleType.GetField("Stream").SetValue(handle, stream);
            _handleType.GetField("Writing").SetValue(handle, true);
            _handleType.GetField("SessionId").SetValue(handle, session ?? NodeId.Null);
            _handleType.GetField("Position").SetValue(handle, 0u);

            FieldInfo handlesField = _sut.GetField("_handles", BindingFlags.NonPublic | BindingFlags.Instance);
            IDictionary map = (IDictionary)handlesField.GetValue(fm);
            uint key = (uint)map.Count + 1u;
            map[key] = handle;
            return key;
        }

        private static void SetFile(FileManager fm, FileState file)
        {
            FieldInfo fileField = _sut.GetField("_file", BindingFlags.NonPublic | BindingFlags.Instance);
            fileField.SetValue(fm, file);
        }

        [Fact]
        public void OnCloseAndUpdate_returns_BadDecodingError_when_inner_processing_throws()
        {
            // Arrange a file whose Parent has a DisplayName (so the catch-branch
            // logger doesn't NRE) but whose NodeId does NOT match
            // _cWoTAssetManagement, so the close handler enters the
            // OnboardAssetFromWoTFileAsync code path. Because _nodeManager is
            // null on this uninitialized FileManager, that call NREs and the
            // catch maps the failure to BadDecodingError.
            FileManager fm = NewBareFileManager();

            BaseObjectState parent = new(null)
            {
                NodeId = new NodeId(99, 99),
                DisplayName = new Opc.Ua.LocalizedText("seeded-asset"),
                BrowseName = new QualifiedName("seeded-asset")
            };

            FileState fs = new(parent)
            {
                OpenCount = new PropertyState<ushort>(null)
            };

            SetFile(fm, fs);
            uint h = AddHandle(fm, new MemoryStream(Encoding.UTF8.GetBytes("payload")));

            MethodInfo onClose = _sut.GetMethod("OnCloseAndUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
            ServiceResult sr = (ServiceResult)onClose.Invoke(fm, new object[] { null, null, null, h });

            Assert.Equal((StatusCode)StatusCodes.BadDecodingError, sr.StatusCode);

            // The handle must be removed even when the close path throws.
            FieldInfo handlesField = _sut.GetField("_handles", BindingFlags.NonPublic | BindingFlags.Instance);
            IDictionary map = (IDictionary)handlesField.GetValue(fm);
            Assert.Empty(map);
        }

        [Fact]
        public void AtomicWriteAllText_cleans_up_temp_file_when_destination_replace_fails()
        {
            // Force the rename step inside AtomicWriteAllText to fail in a
            // cross-platform way: point the target at an *existing directory*.
            // The helper:
            //   1. ensures the parent dir exists,
            //   2. creates the .tmp.<guid> file successfully,
            //   3. takes the !File.Exists(target) branch (File.Exists returns
            //      false for directories) and calls File.Move(tempPath, target),
            //   4. which throws IOException on both Windows and Linux because
            //      the destination path is occupied by a directory — exercising
            //      the catch branch that deletes the temp file before rethrow.
            //
            // The previous version relied on FileShare.None to lock the
            // destination, which only blocks concurrent .NET handles on
            // Windows; on Linux file locking is advisory and File.Replace
            // succeeded, so no exception was thrown and the assertion failed.
            using TestWorkingDirectory tmp = new();
            FileManager fm = NewBareFileManager();

            string target = Path.Combine(tmp.Path, "occupied-by-dir");
            Directory.CreateDirectory(target);

            MethodInfo write = _sut.GetMethod("AtomicWriteAllText", BindingFlags.NonPublic | BindingFlags.Instance);

            TargetInvocationException tie = Assert.Throws<TargetInvocationException>(
                () => write.Invoke(fm, new object[] { target, "new" }));

            Assert.NotNull(tie.InnerException);

            // The catch branch must have removed the temp file before re-throwing.
            string[] leftovers = Directory.GetFiles(tmp.Path, "*.tmp.*");
            Assert.Empty(leftovers);

            // The directory at the target path should still be intact.
            Assert.True(Directory.Exists(target));
        }

        [Fact]
        public void AtomicWriteAllText_throws_when_target_path_is_invalid()
        {
            // An empty path triggers an ArgumentException inside the FileStream
            // constructor, which propagates through the catch branch (without a
            // temp-file to clean up because none was ever created).
            FileManager fm = NewBareFileManager();
            MethodInfo write = _sut.GetMethod("AtomicWriteAllText", BindingFlags.NonPublic | BindingFlags.Instance);

            TargetInvocationException tie = Assert.Throws<TargetInvocationException>(
                () => write.Invoke(fm, new object[] { string.Empty, "x" }));

            Assert.NotNull(tie.InnerException);
        }
    }
}
