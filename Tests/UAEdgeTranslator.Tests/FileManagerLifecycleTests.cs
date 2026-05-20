namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using Xunit;

    /// <summary>
    /// Reflection-driven coverage of the <see cref="FileManager"/> open/read/write
    /// state machine. We use <see cref="RuntimeHelpers.GetUninitializedObject"/>
    /// to bypass the SDK-coupled constructor and seed the private fields the
    /// callbacks rely on so we can exercise every status-code branch without a
    /// running OPC UA stack.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class FileManagerLifecycleTests
    {
        private static readonly Type _sut = typeof(FileManager);

        private static FileManager NewBareInstance()
        {
            FileManager fm = (FileManager)RuntimeHelpers.GetUninitializedObject(_sut);

            // _handles is a Dictionary<uint, Handle>; create it so the lifecycle
            // helpers below have a backing store to add/remove entries.
            FieldInfo handlesField = _sut.GetField("_handles", BindingFlags.NonPublic | BindingFlags.Instance);
            handlesField.SetValue(fm, Activator.CreateInstance(handlesField.FieldType));

            // _nextHandle is a uint counter that the open/write callbacks bump
            // when they hand out a new id.
            FieldInfo nextHandleField = _sut.GetField("_nextHandle", BindingFlags.NonPublic | BindingFlags.Instance);
            nextHandleField.SetValue(fm, 0u);

            return fm;
        }

        [Fact]
        public void Find_throws_BadInvalidArgument_for_unknown_handle()
        {
            FileManager fm = NewBareInstance();
            MethodInfo find = _sut.GetMethod("Find", BindingFlags.NonPublic | BindingFlags.Instance);

            TargetInvocationException tie = Assert.Throws<TargetInvocationException>(
                () => find.Invoke(fm, new object[] { null, 42u }));
            ServiceResultException sre = Assert.IsType<ServiceResultException>(tie.InnerException);
            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, sre.StatusCode);
        }

        [Fact]
        public void Write_seeds_handle_so_subsequent_reads_can_find_it()
        {
            FileManager fm = NewBareInstance();

            byte[] payload = new byte[] { 1, 2, 3, 4, 5 };

            // Public Write(ISystemContext, byte[]) seeds a new read handle.
            _sut.GetMethod("Write", new[] { typeof(ISystemContext), typeof(byte[]) })
                .Invoke(fm, new object[] { null, payload });

            FieldInfo handlesField = _sut.GetField("_handles", BindingFlags.NonPublic | BindingFlags.Instance);
            System.Collections.IDictionary handles = (System.Collections.IDictionary)handlesField.GetValue(fm);

            Assert.Single(handles);

            // The seeded handle's stream must contain exactly the payload.
            uint firstKey = handles.Keys.Cast<uint>().First();
            object handle = handles[firstKey];
            Assert.NotNull(handle);

            FieldInfo streamField = handle.GetType().GetField("Stream", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MemoryStream ms = (MemoryStream)streamField.GetValue(handle);

            Assert.Equal(payload, ms.ToArray());
        }

        [Fact]
        public void GetSessionId_returns_NodeId_Null_for_unknown_context()
        {
            FileManager fm = NewBareInstance();

            MethodInfo m = _sut.GetMethod("GetSessionId", BindingFlags.NonPublic | BindingFlags.Instance);
            NodeId result = (NodeId)m.Invoke(fm, new object[] { null });

            Assert.Equal(NodeId.Null, result);
        }
    }
}
