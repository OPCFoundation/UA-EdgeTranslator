﻿
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;

    public class FileManager : IDisposable
    {
        private readonly UANodeManager _nodeManager;
        private readonly UAModel.WoT_Con.WoTAssetFileTypeState _file;
        private readonly Dictionary<uint, Handle> _handles = new();
        private bool _writing = false;
        private uint _nextHandle = 1;

        private class Handle
        {
            public NodeId SessionId;
            public MemoryStream Stream;
            public uint Position;
            public bool Writing;
        }

        public FileManager(UANodeManager nodeManager, UAModel.WoT_Con.WoTAssetFileTypeState file)
        {
            _nodeManager = nodeManager;
            _file = file;
            _file.Size.Value = 0;
            if (_file.MaxByteStringLength != null) _file.LastModifiedTime.Value = DateTime.MinValue;
            _file.Writable.Value = false;
            _file.UserWritable.Value = false;
            _file.OpenCount.Value = 0;
            if (_file.MaxByteStringLength != null) _file.MaxByteStringLength.Value = UInt16.MaxValue;
            _file.Open.OnCall = new OpenMethodStateMethodCallHandler(OnOpen);
            _file.Close.OnCall = new CloseMethodStateMethodCallHandler(OnClose);
            _file.Read.OnCall = new ReadMethodStateMethodCallHandler(OnRead);
            _file.Write.OnCall = new WriteMethodStateMethodCallHandler(OnWrite);
            _file.GetPosition.OnCall = new GetPositionMethodStateMethodCallHandler(OnGetPosition);
            _file.SetPosition.OnCall = new SetPositionMethodStateMethodCallHandler(OnSetPosition);
            _file.CloseAndUpdate.OnCall = new UAModel.WoT_Con.CloseAndUpdateMethodStateMethodCallHandler(OnCloseAndUpdate);
        }

        public void Dispose()
        {
            lock (_handles)
            {
                foreach (var handle in _handles.Values)
                {
                    handle.Stream.Close();
                    handle.Stream.Dispose();
                }

                _handles.Clear();
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

                if (handle.SessionId != _context.SessionId)
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

                var handle = new Handle
                {
                    SessionId = _context.SessionId,
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
                var handle = Find(_context, fileHandle);

                if (handle.Writing)
                {
                    return StatusCodes.BadInvalidState;
                }

                if (data != null && data.Length > 0)
                {
                    byte[] buffer = new byte[data.Length];
                    handle.Stream.Read(data, 0, data.Length);
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
                var handle = Find(_context, fileHandle);

                if (!handle.Writing)
                {
                    return StatusCodes.BadInvalidState;
                }

                if (data != null && data.Length > 0)
                {
                    handle.Stream.Write(data, 0, data.Length);
                }
            }

            return StatusCodes.Good;
        }

        private ServiceResult OnClose(
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

                if (handle.SessionId != _context.SessionId)
                {
                    return StatusCodes.BadUserAccessDenied;
                }

                _writing = false;
                _handles.Remove(fileHandle);
                _file.OpenCount.Value = (ushort)_handles.Count;
            }

            handle.Stream.Close();
            handle.Stream.Dispose();

            return ServiceResult.Good;
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

                if (handle.SessionId != _context.SessionId)
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

                _nodeManager.AddNodesForWoTProperties(_file.Parent, contents);

                File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "settings", _file.Parent.DisplayName.Text + ".jsonld"), contents);

                _ = Task.Run(() => _nodeManager.HandleServerRestart());

                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
                return new ServiceResult(StatusCodes.BadDecodingError, ex);
            }
            finally
            {
                handle.Stream.Dispose();
            }
        }
    }
}
