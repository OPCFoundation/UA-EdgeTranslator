/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Runtime.Serialization;
using Opc.Ua;

namespace Opc.Ua.WoT
{
    #region IAssetManagementTypeState Class
    #if (!OPCUA_EXCLUDE_IAssetManagementTypeState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class IAssetManagementTypeState : BaseInterfaceState
    {
        #region Constructors
        /// <remarks />
        public IAssetManagementTypeState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.WoT.ObjectTypes.IAssetManagementType, Opc.Ua.WoT.Namespaces.WoT, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);

            if (DeleteAsset != null)
            {
                DeleteAsset.Initialize(context, DeleteAsset_InitializationString);
            }

            if (GetAssets != null)
            {
                GetAssets.Initialize(context, GetAssets_InitializationString);
            }

            if (ConfigureAsset != null)
            {
                ConfigureAsset.Initialize(context, ConfigureAsset_InitializationString);
            }
        }

        #region Initialization String
        private const string DeleteAsset_InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29UL/////8EYYIKBAAAAAEACwAAAERl" +
           "bGV0ZUFzc2V0AQFYGwAvAQFYG1gbAAABAf////8CAAAAN2CpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50" +
           "cwEBeBcDAAAAAFMAAAB0aGUgZGVmaW5pdGlvbiBvZiB0aGUgaW5wdXQgYXJndW1lbnQgb2YgbWV0aG9k" +
           "IDE6SUFzc2V0TWFuYWdlbWVudFR5cGUuMTpEZWxldGVBc3NldAAuAER4FwAAlgEAAAABACoBARYAAAAH" +
           "AAAAQXNzZXRJZAAO/////wAAAAAAAQAoAQEAAAABAAAAAQAAAAEB/////wAAAAA3YIkKAgAAAAAADwAA" +
           "AE91dHB1dEFyZ3VtZW50cwEBeRcDAAAAAFUAAAB0aGUgZGVmaW5pdGlvbiBvZiB0aGUgb3V0cHV0IGFy" +
           "Z3VtZW50cyBvZiBtZXRob2QgMTpJQXNzZXRNYW5hZ2VtZW50VHlwZS4xOkRlbGV0ZUFzc2V0AC4ARHkX" +
           "AAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAAA==";

        private const string GetAssets_InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29UL/////8EYYIKBAAAAAEACQAAAEdl" +
           "dEFzc2V0cwEBWRsALwEBWRtZGwAAAQH/////AgAAADdgiQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMB" +
           "AXoXAwAAAABRAAAAdGhlIGRlZmluaXRpb24gb2YgdGhlIGlucHV0IGFyZ3VtZW50IG9mIG1ldGhvZCAx" +
           "OklBc3NldE1hbmFnZW1lbnRUeXBlLjE6R2V0QXNzZXRzAC4ARHoXAAABACgBAQAAAAEAAAAAAAAAAQH/" +
           "////AAAAADdgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQF7FwMAAAAAUwAAAHRoZSBkZWZpbml0" +
           "aW9uIG9mIHRoZSBvdXRwdXQgYXJndW1lbnRzIG9mIG1ldGhvZCAxOklBc3NldE1hbmFnZW1lbnRUeXBl" +
           "LjE6R2V0QXNzZXRzAC4ARHsXAACWAQAAAAEAKgEBFwAAAAgAAABBc3NldElkcwAO/////wAAAAAAAQAo" +
           "AQEAAAABAAAAAQAAAAEB/////wAAAAA=";

        private const string ConfigureAsset_InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29UL/////8EYYIKBAAAAAEADgAAAENv" +
           "bmZpZ3VyZUFzc2V0AQFaGwAvAQFaG1obAAABAf////8CAAAAN2CpCgIAAAAAAA4AAABJbnB1dEFyZ3Vt" +
           "ZW50cwEBfBcDAAAAAFYAAAB0aGUgZGVmaW5pdGlvbiBvZiB0aGUgaW5wdXQgYXJndW1lbnQgb2YgbWV0" +
           "aG9kIDE6SUFzc2V0TWFuYWdlbWVudFR5cGUuMTpDb25maWd1cmVBc3NldAAuAER8FwAAlgEAAAABACoB" +
           "AR8AAAAQAAAAVGhpbmdEZXNjcmlwdGlvbgAM/////wAAAAAAAQAoAQEAAAABAAAAAQAAAAEB/////wAA" +
           "AAA3YKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBfRcDAAAAAFgAAAB0aGUgZGVmaW5pdGlvbiBv" +
           "ZiB0aGUgb3V0cHV0IGFyZ3VtZW50cyBvZiBtZXRob2QgMTpJQXNzZXRNYW5hZ2VtZW50VHlwZS4xOkNv" +
           "bmZpZ3VyZUFzc2V0AC4ARH0XAACWAQAAAAEAKgEBFgAAAAcAAABBc3NldElkAA7/////AAAAAAABACgB" +
           "AQAAAAEAAAABAAAAAQH/////AAAAAA==";

        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29UL/////8EYIACAQAAAAEAHAAAAElB" +
           "c3NldE1hbmFnZW1lbnRUeXBlSW5zdGFuY2UBAegDAQHoA+gDAAD/////AwAAAARhggoEAAAAAQALAAAA" +
           "RGVsZXRlQXNzZXQBAVgbAC8BAVgbWBsAAAEB/////wIAAAA3YKkKAgAAAAAADgAAAElucHV0QXJndW1l" +
           "bnRzAQF4FwMAAAAAUwAAAHRoZSBkZWZpbml0aW9uIG9mIHRoZSBpbnB1dCBhcmd1bWVudCBvZiBtZXRo" +
           "b2QgMTpJQXNzZXRNYW5hZ2VtZW50VHlwZS4xOkRlbGV0ZUFzc2V0AC4ARHgXAACWAQAAAAEAKgEBFgAA" +
           "AAcAAABBc3NldElkAA7/////AAAAAAABACgBAQAAAAEAAAABAAAAAQH/////AAAAADdgiQoCAAAAAAAP" +
           "AAAAT3V0cHV0QXJndW1lbnRzAQF5FwMAAAAAVQAAAHRoZSBkZWZpbml0aW9uIG9mIHRoZSBvdXRwdXQg" +
           "YXJndW1lbnRzIG9mIG1ldGhvZCAxOklBc3NldE1hbmFnZW1lbnRUeXBlLjE6RGVsZXRlQXNzZXQALgBE" +
           "eRcAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAABGGCCgQAAAABAAkAAABHZXRBc3NldHMBAVkbAC8B" +
           "AVkbWRsAAAEB/////wIAAAA3YIkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQF6FwMAAAAAUQAAAHRo" +
           "ZSBkZWZpbml0aW9uIG9mIHRoZSBpbnB1dCBhcmd1bWVudCBvZiBtZXRob2QgMTpJQXNzZXRNYW5hZ2Vt" +
           "ZW50VHlwZS4xOkdldEFzc2V0cwAuAER6FwAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAA3YKkKAgAA" +
           "AAAADwAAAE91dHB1dEFyZ3VtZW50cwEBexcDAAAAAFMAAAB0aGUgZGVmaW5pdGlvbiBvZiB0aGUgb3V0" +
           "cHV0IGFyZ3VtZW50cyBvZiBtZXRob2QgMTpJQXNzZXRNYW5hZ2VtZW50VHlwZS4xOkdldEFzc2V0cwAu" +
           "AER7FwAAlgEAAAABACoBARcAAAAIAAAAQXNzZXRJZHMADv////8AAAAAAAEAKAEBAAAAAQAAAAEAAAAB" +
           "Af////8AAAAABGGCCgQAAAABAA4AAABDb25maWd1cmVBc3NldAEBWhsALwEBWhtaGwAAAQH/////AgAA" +
           "ADdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAXwXAwAAAABWAAAAdGhlIGRlZmluaXRpb24gb2Yg" +
           "dGhlIGlucHV0IGFyZ3VtZW50IG9mIG1ldGhvZCAxOklBc3NldE1hbmFnZW1lbnRUeXBlLjE6Q29uZmln" +
           "dXJlQXNzZXQALgBEfBcAAJYBAAAAAQAqAQEfAAAAEAAAAFRoaW5nRGVzY3JpcHRpb24ADP////8AAAAA" +
           "AAEAKAEBAAAAAQAAAAEAAAABAf////8AAAAAN2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAX0X" +
           "AwAAAABYAAAAdGhlIGRlZmluaXRpb24gb2YgdGhlIG91dHB1dCBhcmd1bWVudHMgb2YgbWV0aG9kIDE6" +
           "SUFzc2V0TWFuYWdlbWVudFR5cGUuMTpDb25maWd1cmVBc3NldAAuAER9FwAAlgEAAAABACoBARYAAAAH" +
           "AAAAQXNzZXRJZAAO/////wAAAAAAAQAoAQEAAAABAAAAAQAAAAEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public DeleteAssetMethodState DeleteAsset
        {
            get
            {
                return m_deleteAssetMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_deleteAssetMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_deleteAssetMethod = value;
            }
        }

        /// <remarks />
        public GetAssetsMethodState GetAssets
        {
            get
            {
                return m_getAssetsMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_getAssetsMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_getAssetsMethod = value;
            }
        }

        /// <remarks />
        public ConfigureAssetMethodState ConfigureAsset
        {
            get
            {
                return m_configureAssetMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_configureAssetMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_configureAssetMethod = value;
            }
        }
        #endregion

        #region Overridden Methods
        /// <remarks />
        public override void GetChildren(
            ISystemContext context,
            IList<BaseInstanceState> children)
        {
            if (m_deleteAssetMethod != null)
            {
                children.Add(m_deleteAssetMethod);
            }

            if (m_getAssetsMethod != null)
            {
                children.Add(m_getAssetsMethod);
            }

            if (m_configureAssetMethod != null)
            {
                children.Add(m_configureAssetMethod);
            }

            base.GetChildren(context, children);
        }
            
        /// <remarks />
        protected override BaseInstanceState FindChild(
            ISystemContext context,
            QualifiedName browseName,
            bool createOrReplace,
            BaseInstanceState replacement)
        {
            if (QualifiedName.IsNull(browseName))
            {
                return null;
            }

            BaseInstanceState instance = null;

            switch (browseName.Name)
            {
                case Opc.Ua.WoT.BrowseNames.DeleteAsset:
                {
                    if (createOrReplace)
                    {
                        if (DeleteAsset == null)
                        {
                            if (replacement == null)
                            {
                                DeleteAsset = new DeleteAssetMethodState(this);
                            }
                            else
                            {
                                DeleteAsset = (DeleteAssetMethodState)replacement;
                            }
                        }
                    }

                    instance = DeleteAsset;
                    break;
                }

                case Opc.Ua.WoT.BrowseNames.GetAssets:
                {
                    if (createOrReplace)
                    {
                        if (GetAssets == null)
                        {
                            if (replacement == null)
                            {
                                GetAssets = new GetAssetsMethodState(this);
                            }
                            else
                            {
                                GetAssets = (GetAssetsMethodState)replacement;
                            }
                        }
                    }

                    instance = GetAssets;
                    break;
                }

                case Opc.Ua.WoT.BrowseNames.ConfigureAsset:
                {
                    if (createOrReplace)
                    {
                        if (ConfigureAsset == null)
                        {
                            if (replacement == null)
                            {
                                ConfigureAsset = new ConfigureAssetMethodState(this);
                            }
                            else
                            {
                                ConfigureAsset = (ConfigureAssetMethodState)replacement;
                            }
                        }
                    }

                    instance = ConfigureAsset;
                    break;
                }
            }

            if (instance != null)
            {
                return instance;
            }

            return base.FindChild(context, browseName, createOrReplace, replacement);
        }
        #endregion

        #region Private Fields
        private DeleteAssetMethodState m_deleteAssetMethod;
        private GetAssetsMethodState m_getAssetsMethod;
        private ConfigureAssetMethodState m_configureAssetMethod;
        #endregion
    }
    #endif
    #endregion

    #region DeleteAssetMethodState Class
    #if (!OPCUA_EXCLUDE_DeleteAssetMethodState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class DeleteAssetMethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public DeleteAssetMethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new DeleteAssetMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29UL/////8EYYIABAAAAAEAFQAAAERl" +
           "bGV0ZUFzc2V0TWV0aG9kVHlwZQEBAAABAQAAAQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public DeleteAssetMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <remarks />
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            Uuid assetId = (Uuid)_inputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    assetId);
            }

            return _result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <remarks />
    /// <exclude />
    public delegate ServiceResult DeleteAssetMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        Uuid assetId);
    #endif
    #endregion

    #region GetAssetsMethodState Class
    #if (!OPCUA_EXCLUDE_GetAssetsMethodState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class GetAssetsMethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public GetAssetsMethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new GetAssetsMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29UL/////8EYYIABAAAAAEAEwAAAEdl" +
           "dEFzc2V0c01ldGhvZFR5cGUBAQAAAQEAAAEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public GetAssetsMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <remarks />
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            Uuid assetIds = (Uuid)_outputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    ref assetIds);
            }

            _outputArguments[0] = assetIds;

            return _result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <remarks />
    /// <exclude />
    public delegate ServiceResult GetAssetsMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        ref Uuid assetIds);
    #endif
    #endregion

    #region ConfigureAssetMethodState Class
    #if (!OPCUA_EXCLUDE_ConfigureAssetMethodState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ConfigureAssetMethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public ConfigureAssetMethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new ConfigureAssetMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29UL/////8EYYIABAAAAAEAGAAAAENv" +
           "bmZpZ3VyZUFzc2V0TWV0aG9kVHlwZQEBAAABAQAAAQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public ConfigureAssetMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <remarks />
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            string thingDescription = (string)_inputArguments[0];

            Uuid assetId = (Uuid)_outputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    thingDescription,
                    ref assetId);
            }

            _outputArguments[0] = assetId;

            return _result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <remarks />
    /// <exclude />
    public delegate ServiceResult ConfigureAssetMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        string thingDescription,
        ref Uuid assetId);
    #endif
    #endregion
}