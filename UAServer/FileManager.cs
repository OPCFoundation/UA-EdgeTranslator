namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua;
    using Opc.Ua.Export;
    using Opc.Ua.Server;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    public class FileManager : IDisposable
    {
        private readonly UANodeManager _nodeManager;
        private readonly FileState _file;
        private readonly Dictionary<uint, Handle> _handles = new();
        private bool _writing = false;
        private uint _nextHandle = 1;

        private const string _cWotCon = "http://opcfoundation.org/UA/WoT-Con/";

        private const uint _cWoTAssetManagement = 31;

        // Per-handle upload size cap. Defaults to 5 MB but can be overridden via the
        // WOT_MAX_FILE_BYTES environment variable to support very large nodesets.
        private const int _defaultMaxFileBytes = 5 * 1024 * 1024;
        private static readonly int _maxFileBytes = ResolveMaxFileBytes();

        private static int ResolveMaxFileBytes()
        {
            string raw = Environment.GetEnvironmentVariable("WOT_MAX_FILE_BYTES");
            if (!string.IsNullOrEmpty(raw)
             && int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed)
             && parsed > 0)
            {
                return parsed;
            }

            return _defaultMaxFileBytes;
        }

        private class Handle
        {
            public NodeId SessionId;
            public MemoryStream Stream;
            public uint Position;
            public bool Writing;
        }

        public FileManager(UANodeManager nodeManager, FileState file)
        {
            _nodeManager = nodeManager;
            _file = file;
            _file.Size.Value = 0;
            if (_file.LastModifiedTime != null) _file.LastModifiedTime.Value = DateTime.MinValue;
            _file.Writable.Value = false;
            _file.UserWritable.Value = false;
            _file.OpenCount.Value = 0;
            if (_file.MaxByteStringLength != null) _file.MaxByteStringLength.Value = UInt16.MaxValue;
            _file.Open.OnCall = new OpenMethodStateMethodCallHandler(OnOpen);
            _file.Read.OnCall = new ReadMethodStateMethodCallHandler(OnRead);
            _file.Write.OnCall = new WriteMethodStateMethodCallHandler(OnWrite);
            _file.GetPosition.OnCall = new GetPositionMethodStateMethodCallHandler(OnGetPosition);
            _file.SetPosition.OnCall = new SetPositionMethodStateMethodCallHandler(OnSetPosition);
            _file.Close.OnCall = new CloseMethodStateMethodCallHandler(OnCloseAndUpdate);
            _file.Close.DisplayName = new Opc.Ua.LocalizedText("CloseAndUpdate");
            _file.Close.BrowseName = new QualifiedName("CloseAndUpdate");
        }

        public void Dispose()
        {
            lock (_handles)
            {
                foreach (Handle handle in _handles.Values)
                {
                    handle.Stream.Close();
                    handle.Stream.Dispose();
                }

                _handles.Clear();
            }
        }

        private static NodeId GetSessionId(ISystemContext context)
        {
            if (context is ServerSystemContext serverContext)
            {
                return serverContext.SessionId;
            }

            return NodeId.Null;
        }

        public void Write(ISystemContext context, byte[] contents)
        {
            Handle handle = new()
            {
                SessionId = GetSessionId(context),
                Stream = new MemoryStream(contents),
                Position = 0
            };

            lock (_handles)
            {
                uint fileHandle = ++_nextHandle;
                _handles.Add(fileHandle, handle);
            }
        }

        private Handle Find(ISystemContext _context, uint fileHandle)
        {
            Handle handle;

            lock (_handles)
            {
                if (!_handles.TryGetValue(fileHandle, out handle))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }

                if (handle.SessionId != GetSessionId(_context))
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
            }

            return handle;
        }

        private ServiceResult OnOpen(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        byte mode,
        ref uint fileHandle)
        {
            if (mode != 1 && mode != 6)
            {
                return StatusCodes.BadNotSupported;
            }

            lock (_handles)
            {
                if (_handles.Count >= 10)
                {
                    return StatusCodes.BadTooManyOperations;
                }

                if (_writing && mode != 1)
                {
                    return StatusCodes.BadInvalidState;
                }

                Handle handle = new()
                {
                    SessionId = GetSessionId(_context),
                    Stream = new MemoryStream(),
                    Position = 0
                };

                if (mode == 6)
                {
                    _writing = handle.Writing = true;
                }

                lock (_handles)
                {
                    fileHandle = ++_nextHandle;
                    _handles.Add(fileHandle, handle);
                    _file.OpenCount.Value = (ushort)_handles.Count;
                }
            }

            return ServiceResult.Good;
        }

        private ServiceResult OnGetPosition(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        uint fileHandle,
        ref ulong position)
        {
            Handle handle = Find(_context, fileHandle);
            position = (ulong)handle.Stream.Position;
            return StatusCodes.Good;
        }

        private ServiceResult OnSetPosition(
            ISystemContext _context,
            MethodState _method,
            NodeId _objectId,
            uint fileHandle,
            ulong position)
        {
            Handle handle = Find(_context, fileHandle);
            handle.Stream.Seek((long)position, SeekOrigin.Begin);
            return StatusCodes.Good;
        }

        private ServiceResult OnRead(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        uint fileHandle,
        int length,
        ref byte[] data)
        {
            lock (_handles)
            {
                Handle handle = Find(_context, fileHandle);

                if (handle.Writing)
                {
                    return StatusCodes.BadInvalidState;
                }

                if (length > 0)
                {
                    byte[] buffer = new byte[length];
                    int bytesRead = handle.Stream.Read(buffer, 0, length);
                    
                    // Return only the bytes actually read
                    if (bytesRead < length)
                    {
                        Array.Resize(ref buffer, bytesRead);
                    }
                    data = buffer;
                }
                else
                {
                    data = new byte[0];
                }
            }

            return StatusCodes.Good;
        }

        private ServiceResult OnWrite(
            ISystemContext _context,
            MethodState _method,
            NodeId _objectId,
            uint fileHandle,
            byte[] data)
        {
            lock (_handles)
            {
                Handle handle = Find(_context, fileHandle);

                if (!handle.Writing)
                {
                    return StatusCodes.BadInvalidState;
                }

                if (data != null && data.Length > 0)
                {
                    // enforce a per-handle upload cap to prevent unbounded
                    // memory growth via repeated chunked Write() calls.
                    long projected = handle.Stream.Length + data.Length;
                    if (projected > _maxFileBytes)
                    {
                        Log.Logger.Warning("Rejecting WoT file upload: size {Projected} exceeds configured cap {Cap} bytes.", projected, _maxFileBytes);
                        return new ServiceResult(StatusCodes.BadOutOfMemory, $"Upload exceeds maximum allowed size of {_maxFileBytes} bytes.");
                    }

                    handle.Stream.Write(data, 0, data.Length);
                }
            }

            return StatusCodes.Good;
        }

        private ServiceResult OnCloseAndUpdate(
            ISystemContext _context,
            MethodState _method,
            NodeId _objectId,
            uint fileHandle)
        {
            Handle handle;

            lock (_handles)
            {
                if (!_handles.TryGetValue(fileHandle, out handle))
                {
                    return StatusCodes.BadInvalidArgument;
                }

                if (handle.SessionId != GetSessionId(_context))
                {
                    return StatusCodes.BadUserAccessDenied;
                }

                _writing = false;
                _handles.Remove(fileHandle);
                _file.OpenCount.Value = (ushort)_handles.Count;
            }

            try
            {
                handle.Stream.Close();

                string contents = Encoding.UTF8.GetString(handle.Stream.ToArray());

                ushort WoTConNamespaceIndex = (ushort)_nodeManager.Server.NamespaceUris.GetIndex(_cWotCon);

                if (_file.Parent.NodeId == new NodeId(_cWoTAssetManagement, WoTConNamespaceIndex))
                {
                    string filename = Path.GetRandomFileName();

                    using (Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(contents)))
                    {
                        UANodeSet nodeSet = UANodeSet.Read(stream);
                        if ((nodeSet.Models != null) && (nodeSet.Models.Length > 0))
                        {
                            filename = nodeSet.Models[0].ModelUri.Replace("http://", "").Replace(".", "_").Replace("/", "_").TrimEnd('_');
                        }
                    }

                    File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "nodesets", filename + ".nodeset2.xml"), contents);
                }
                else
                {
                    bool writeContent = true;
                    if (string.IsNullOrEmpty(contents))
                    {
                        // the user closed the file transfer without content: Try to load contents locally instead
                        writeContent = false;
                        contents = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "settings", _file.Parent.DisplayName.Text + ".jsonld"));
                    }

                    _nodeManager.OnboardAssetFromWoTFileAsync(_file.Parent, contents).GetAwaiter().GetResult();

                    _nodeManager.RaiseModelChangedEvent(_file.Parent.NodeId, ModelChangeStructureVerbMask.NodeAdded);

                    if (writeContent)
                    {
                        File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "settings", _file.Parent.DisplayName.Text + ".jsonld"), contents);
                    }
                }

                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                // Log full exception server-side, return only a generic status to
                // the OPC UA client to avoid leaking internal exception text or
                // stack traces over the wire.
                Log.Logger.Error(ex, "Failed to process file close and update for file: {FileName}", _file.Parent.DisplayName.Text);
                return new ServiceResult(StatusCodes.BadDecodingError);
            }
            finally
            {
                handle.Stream.Dispose();
            }
        }
    }
}
