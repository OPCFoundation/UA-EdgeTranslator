/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.WotCon
{
    #region WoTAssetConnectionManagementState Class
    #if (!OPCUA_EXCLUDE_WoTAssetConnectionManagementState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class WoTAssetConnectionManagementState : BaseObjectState
    {
        #region Constructors
        /// <remarks />
        public WoTAssetConnectionManagementState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.WotCon.ObjectTypes.WoTAssetConnectionManagementType, Opc.Ua.WotCon.Namespaces.WotCon, namespaceUris);
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

            if (SupportedWoTBindings != null)
            {
                SupportedWoTBindings.Initialize(context, SupportedWoTBindings_InitializationString);
            }

            if (DiscoverAssets != null)
            {
                DiscoverAssets.Initialize(context, DiscoverAssets_InitializationString);
            }

            if (CreateAssetForEndpoint != null)
            {
                CreateAssetForEndpoint.Initialize(context, CreateAssetForEndpoint_InitializationString);
            }

            if (ConnectionTest != null)
            {
                ConnectionTest.Initialize(context, ConnectionTest_InitializationString);
            }

            if (Configuration != null)
            {
                Configuration.Initialize(context, Configuration_InitializationString);
            }
        }

        #region Initialization String
        private const string SupportedWoTBindings_InitializationString =
           "AQAAACQAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29ULUNvbi//////F2CJCgIAAAABABQA" +
           "AABTdXBwb3J0ZWRXb1RCaW5kaW5ncwEBWQAALgBEWQAAAAEAx1wBAAAAAQAAAAAAAAABAf////8AAAAA";

        private const string DiscoverAssets_InitializationString =
           "AQAAACQAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29ULUNvbi//////BGGCCgQAAAABAA4A" +
           "AABEaXNjb3ZlckFzc2V0cwEBWgAALwEBWgBaAAAAAQH/////AQAAABdgqQoCAAAAAAAPAAAAT3V0cHV0" +
           "QXJndW1lbnRzAQF1AAAuAER1AAAAlgEAAAABACoBASEAAAAOAAAAQXNzZXRFbmRwb2ludHMADAEAAAAB" +
           "AAAAAAAAAAABACgBAQAAAAEAAAABAAAAAQH/////AAAAAA==";

        private const string CreateAssetForEndpoint_InitializationString =
           "AQAAACQAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29ULUNvbi//////BGGCCgQAAAABABYA" +
           "AABDcmVhdGVBc3NldEZvckVuZHBvaW50AQFcAAAvAQFcAFwAAAABAf////8BAAAAF2CpCgIAAAAAAA4A" +
           "AABJbnB1dEFyZ3VtZW50cwEBXQAALgBEXQAAAJYCAAAAAQAqAQEYAAAACQAAAEFzc2V0TmFtZQAM////" +
           "/wAAAAAAAQAqAQEcAAAADQAAAEFzc2V0RW5kcG9pbnQADP////8AAAAAAAEAKAEBAAAAAQAAAAIAAAAB" +
           "Af////8AAAAA";

        private const string ConnectionTest_InitializationString =
           "AQAAACQAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29ULUNvbi//////BGGCCgQAAAABAA4A" +
           "AABDb25uZWN0aW9uVGVzdAEBXgAALwEBXgBeAAAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRB" +
           "cmd1bWVudHMBAV8AAC4ARF8AAACWAQAAAAEAKgEBHAAAAA0AAABBc3NldEVuZHBvaW50AAz/////AAAA" +
           "AAABACgBAQAAAAEAAAABAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQF2" +
           "AAAuAER2AAAAlgIAAAABACoBARYAAAAHAAAAU3VjY2VzcwAB/////wAAAAAAAQAqAQEVAAAABgAAAFN0" +
           "YXR1cwAM/////wAAAAAAAQAoAQEAAAABAAAAAgAAAAEB/////wAAAAA=";

        private const string Configuration_InitializationString =
           "AQAAACQAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29ULUNvbi//////BGCACgEAAAABAA0A" +
           "AABDb25maWd1cmF0aW9uAQFgAAAvAQFxAGAAAAD/////AAAAAA==";

        private const string InitializationString =
           "AQAAACQAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29ULUNvbi//////BGCAAgEAAAABACgA" +
           "AABXb1RBc3NldENvbm5lY3Rpb25NYW5hZ2VtZW50VHlwZUluc3RhbmNlAQEmAAEBJgAmAAAA/////wcA" +
           "AAAXYIkKAgAAAAEAFAAAAFN1cHBvcnRlZFdvVEJpbmRpbmdzAQFZAAAuAERZAAAAAQDHXAEAAAABAAAA" +
           "AAAAAAEB/////wAAAAAEYYIKBAAAAAEACwAAAENyZWF0ZUFzc2V0AQEoAAAvAQEoACgAAAABAf////8C" +
           "AAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEBKQAALgBEKQAAAJYBAAAAAQAqAQEYAAAACQAA" +
           "AEFzc2V0TmFtZQAM/////wAAAAAAAQAoAQEAAAABAAAAAQAAAAEB/////wAAAAAXYKkKAgAAAAAADwAA" +
           "AE91dHB1dEFyZ3VtZW50cwEBKgAALgBEKgAAAJYBAAAAAQAqAQEWAAAABwAAAEFzc2V0SWQAEf////8A" +
           "AAAAAAEAKAEBAAAAAQAAAAEAAAABAf////8AAAAABGGCCgQAAAABAAsAAABEZWxldGVBc3NldAEBKwAA" +
           "LwEBKwArAAAAAQH/////AQAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBASwAAC4ARCwAAACW" +
           "AQAAAAEAKgEBFgAAAAcAAABBc3NldElkABH/////AAAAAAABACgBAQAAAAEAAAABAAAAAQH/////AAAA" +
           "AARhggoEAAAAAQAOAAAARGlzY292ZXJBc3NldHMBAVoAAC8BAVoAWgAAAAEB/////wEAAAAXYKkKAgAA" +
           "AAAADwAAAE91dHB1dEFyZ3VtZW50cwEBdQAALgBEdQAAAJYBAAAAAQAqAQEhAAAADgAAAEFzc2V0RW5k" +
           "cG9pbnRzAAwBAAAAAQAAAAAAAAAAAQAoAQEAAAABAAAAAQAAAAEB/////wAAAAAEYYIKBAAAAAEAFgAA" +
           "AENyZWF0ZUFzc2V0Rm9yRW5kcG9pbnQBAVwAAC8BAVwAXAAAAAEB/////wEAAAAXYKkKAgAAAAAADgAA" +
           "AElucHV0QXJndW1lbnRzAQFdAAAuAERdAAAAlgIAAAABACoBARgAAAAJAAAAQXNzZXROYW1lAAz/////" +
           "AAAAAAABACoBARwAAAANAAAAQXNzZXRFbmRwb2ludAAM/////wAAAAAAAQAoAQEAAAABAAAAAgAAAAEB" +
           "/////wAAAAAEYYIKBAAAAAEADgAAAENvbm5lY3Rpb25UZXN0AQFeAAAvAQFeAF4AAAABAf////8CAAAA" +
           "F2CpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEBXwAALgBEXwAAAJYBAAAAAQAqAQEcAAAADQAAAEFz" +
           "c2V0RW5kcG9pbnQADP////8AAAAAAAEAKAEBAAAAAQAAAAEAAAABAf////8AAAAAF2CpCgIAAAAAAA8A" +
           "AABPdXRwdXRBcmd1bWVudHMBAXYAAC4ARHYAAACWAgAAAAEAKgEBFgAAAAcAAABTdWNjZXNzAAH/////" +
           "AAAAAAABACoBARUAAAAGAAAAU3RhdHVzAAz/////AAAAAAABACgBAQAAAAEAAAACAAAAAQH/////AAAA" +
           "AARggAoBAAAAAQANAAAAQ29uZmlndXJhdGlvbgEBYAAALwEBcQBgAAAA/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public PropertyState<string[]> SupportedWoTBindings
        {
            get
            {
                return m_supportedWoTBindings;
            }

            set
            {
                if (!Object.ReferenceEquals(m_supportedWoTBindings, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_supportedWoTBindings = value;
            }
        }

        /// <remarks />
        public CreateAssetMethodState CreateAsset
        {
            get
            {
                return m_createAssetMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_createAssetMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_createAssetMethod = value;
            }
        }

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
        public DiscoverAssetsMethodState DiscoverAssets
        {
            get
            {
                return m_discoverAssetsMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_discoverAssetsMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_discoverAssetsMethod = value;
            }
        }

        /// <remarks />
        public CreateAssetForEndpointMethodState CreateAssetForEndpoint
        {
            get
            {
                return m_createAssetForEndpointMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_createAssetForEndpointMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_createAssetForEndpointMethod = value;
            }
        }

        /// <remarks />
        public ConnectionTestMethodState ConnectionTest
        {
            get
            {
                return m_connectionTestMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_connectionTestMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_connectionTestMethod = value;
            }
        }

        /// <remarks />
        public WoTAssetConfigurationState Configuration
        {
            get
            {
                return m_configuration;
            }

            set
            {
                if (!Object.ReferenceEquals(m_configuration, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_configuration = value;
            }
        }
        #endregion

        #region Overridden Methods
        /// <remarks />
        public override void GetChildren(
            ISystemContext context,
            IList<BaseInstanceState> children)
        {
            if (m_supportedWoTBindings != null)
            {
                children.Add(m_supportedWoTBindings);
            }

            if (m_createAssetMethod != null)
            {
                children.Add(m_createAssetMethod);
            }

            if (m_deleteAssetMethod != null)
            {
                children.Add(m_deleteAssetMethod);
            }

            if (m_discoverAssetsMethod != null)
            {
                children.Add(m_discoverAssetsMethod);
            }

            if (m_createAssetForEndpointMethod != null)
            {
                children.Add(m_createAssetForEndpointMethod);
            }

            if (m_connectionTestMethod != null)
            {
                children.Add(m_connectionTestMethod);
            }

            if (m_configuration != null)
            {
                children.Add(m_configuration);
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
                case Opc.Ua.WotCon.BrowseNames.SupportedWoTBindings:
                {
                    if (createOrReplace)
                    {
                        if (SupportedWoTBindings == null)
                        {
                            if (replacement == null)
                            {
                                SupportedWoTBindings = new PropertyState<string[]>(this);
                            }
                            else
                            {
                                SupportedWoTBindings = (PropertyState<string[]>)replacement;
                            }
                        }
                    }

                    instance = SupportedWoTBindings;
                    break;
                }

                case Opc.Ua.WotCon.BrowseNames.CreateAsset:
                {
                    if (createOrReplace)
                    {
                        if (CreateAsset == null)
                        {
                            if (replacement == null)
                            {
                                CreateAsset = new CreateAssetMethodState(this);
                            }
                            else
                            {
                                CreateAsset = (CreateAssetMethodState)replacement;
                            }
                        }
                    }

                    instance = CreateAsset;
                    break;
                }

                case Opc.Ua.WotCon.BrowseNames.DeleteAsset:
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

                case Opc.Ua.WotCon.BrowseNames.DiscoverAssets:
                {
                    if (createOrReplace)
                    {
                        if (DiscoverAssets == null)
                        {
                            if (replacement == null)
                            {
                                DiscoverAssets = new DiscoverAssetsMethodState(this);
                            }
                            else
                            {
                                DiscoverAssets = (DiscoverAssetsMethodState)replacement;
                            }
                        }
                    }

                    instance = DiscoverAssets;
                    break;
                }

                case Opc.Ua.WotCon.BrowseNames.CreateAssetForEndpoint:
                {
                    if (createOrReplace)
                    {
                        if (CreateAssetForEndpoint == null)
                        {
                            if (replacement == null)
                            {
                                CreateAssetForEndpoint = new CreateAssetForEndpointMethodState(this);
                            }
                            else
                            {
                                CreateAssetForEndpoint = (CreateAssetForEndpointMethodState)replacement;
                            }
                        }
                    }

                    instance = CreateAssetForEndpoint;
                    break;
                }

                case Opc.Ua.WotCon.BrowseNames.ConnectionTest:
                {
                    if (createOrReplace)
                    {
                        if (ConnectionTest == null)
                        {
                            if (replacement == null)
                            {
                                ConnectionTest = new ConnectionTestMethodState(this);
                            }
                            else
                            {
                                ConnectionTest = (ConnectionTestMethodState)replacement;
                            }
                        }
                    }

                    instance = ConnectionTest;
                    break;
                }

                case Opc.Ua.WotCon.BrowseNames.Configuration:
                {
                    if (createOrReplace)
                    {
                        if (Configuration == null)
                        {
                            if (replacement == null)
                            {
                                Configuration = new WoTAssetConfigurationState(this);
                            }
                            else
                            {
                                Configuration = (WoTAssetConfigurationState)replacement;
                            }
                        }
                    }

                    instance = Configuration;
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
        private PropertyState<string[]> m_supportedWoTBindings;
        private CreateAssetMethodState m_createAssetMethod;
        private DeleteAssetMethodState m_deleteAssetMethod;
        private DiscoverAssetsMethodState m_discoverAssetsMethod;
        private CreateAssetForEndpointMethodState m_createAssetForEndpointMethod;
        private ConnectionTestMethodState m_connectionTestMethod;
        private WoTAssetConfigurationState m_configuration;
        #endregion
    }
    #endif
    #endregion

    #region CreateAssetMethodState Class
    #if (!OPCUA_EXCLUDE_CreateAssetMethodState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class CreateAssetMethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public CreateAssetMethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new CreateAssetMethodState(parent);
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
           "AQAAACQAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29ULUNvbi//////BGGCCgQAAAABABUA" +
           "AABDcmVhdGVBc3NldE1ldGhvZFR5cGUBATMAAC8BATMAMwAAAAEB/////wIAAAAXYKkKAgAAAAAADgAA" +
           "AElucHV0QXJndW1lbnRzAQE0AAAuAEQ0AAAAlgEAAAABACoBARgAAAAJAAAAQXNzZXROYW1lAAz/////" +
           "AAAAAAABACgBAQAAAAEAAAABAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRz" +
           "AQE1AAAuAEQ1AAAAlgEAAAABACoBARYAAAAHAAAAQXNzZXRJZAAR/////wAAAAAAAQAoAQEAAAABAAAA" +
           "AQAAAAEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public CreateAssetMethodStateMethodCallHandler OnCall;
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

            string assetName = (string)_inputArguments[0];

            NodeId assetId = (NodeId)_outputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    assetName,
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
    public delegate ServiceResult CreateAssetMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        string assetName,
        ref NodeId assetId);
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
           "AQAAACQAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29ULUNvbi//////BGGCCgQAAAABABUA" +
           "AABEZWxldGVBc3NldE1ldGhvZFR5cGUBATYAAC8BATYANgAAAAEB/////wEAAAAXYKkKAgAAAAAADgAA" +
           "AElucHV0QXJndW1lbnRzAQE3AAAuAEQ3AAAAlgEAAAABACoBARYAAAAHAAAAQXNzZXRJZAAR/////wAA" +
           "AAAAAQAoAQEAAAABAAAAAQAAAAEB/////wAAAAA=";
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

            NodeId assetId = (NodeId)_inputArguments[0];

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
        NodeId assetId);
    #endif
    #endregion

    #region DiscoverAssetsMethodState Class
    #if (!OPCUA_EXCLUDE_DiscoverAssetsMethodState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class DiscoverAssetsMethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public DiscoverAssetsMethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new DiscoverAssetsMethodState(parent);
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
           "AQAAACQAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29ULUNvbi//////BGGCCgQAAAABABgA" +
           "AABEaXNjb3ZlckFzc2V0c01ldGhvZFR5cGUBAWsAAC8BAWsAawAAAAEB/////wEAAAAXYKkKAgAAAAAA" +
           "DwAAAE91dHB1dEFyZ3VtZW50cwEBeQAALgBEeQAAAJYBAAAAAQAqAQEhAAAADgAAAEFzc2V0RW5kcG9p" +
           "bnRzAAwBAAAAAQAAAAAAAAAAAQAoAQEAAAABAAAAAQAAAAEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public DiscoverAssetsMethodStateMethodCallHandler OnCall;
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

            string[] assetEndpoints = (string[])_outputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    ref assetEndpoints);
            }

            _outputArguments[0] = assetEndpoints;

            return _result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <remarks />
    /// <exclude />
    public delegate ServiceResult DiscoverAssetsMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        ref string[] assetEndpoints);
    #endif
    #endregion

    #region CreateAssetForEndpointMethodState Class
    #if (!OPCUA_EXCLUDE_CreateAssetForEndpointMethodState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class CreateAssetForEndpointMethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public CreateAssetForEndpointMethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new CreateAssetForEndpointMethodState(parent);
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
           "AQAAACQAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29ULUNvbi//////BGGCCgQAAAABACAA" +
           "AABDcmVhdGVBc3NldEZvckVuZHBvaW50TWV0aG9kVHlwZQEBbQAALwEBbQBtAAAAAQH/////AQAAABdg" +
           "qQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAW4AAC4ARG4AAACWAgAAAAEAKgEBGAAAAAkAAABBc3Nl" +
           "dE5hbWUADP////8AAAAAAAEAKgEBHAAAAA0AAABBc3NldEVuZHBvaW50AAz/////AAAAAAABACgBAQAA" +
           "AAEAAAACAAAAAQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public CreateAssetForEndpointMethodStateMethodCallHandler OnCall;
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

            string assetName = (string)_inputArguments[0];
            string assetEndpoint = (string)_inputArguments[1];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    assetName,
                    assetEndpoint);
            }

            return _result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <remarks />
    /// <exclude />
    public delegate ServiceResult CreateAssetForEndpointMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        string assetName,
        string assetEndpoint);
    #endif
    #endregion

    #region ConnectionTestMethodState Class
    #if (!OPCUA_EXCLUDE_ConnectionTestMethodState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ConnectionTestMethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public ConnectionTestMethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new ConnectionTestMethodState(parent);
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
           "AQAAACQAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29ULUNvbi//////BGGCCgQAAAABABgA" +
           "AABDb25uZWN0aW9uVGVzdE1ldGhvZFR5cGUBAW8AAC8BAW8AbwAAAAEB/////wIAAAAXYKkKAgAAAAAA" +
           "DgAAAElucHV0QXJndW1lbnRzAQFwAAAuAERwAAAAlgEAAAABACoBARwAAAANAAAAQXNzZXRFbmRwb2lu" +
           "dAAM/////wAAAAAAAQAoAQEAAAABAAAAAQAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1dEFy" +
           "Z3VtZW50cwEBegAALgBEegAAAJYCAAAAAQAqAQEWAAAABwAAAFN1Y2Nlc3MAAf////8AAAAAAAEAKgEB" +
           "FQAAAAYAAABTdGF0dXMADP////8AAAAAAAEAKAEBAAAAAQAAAAIAAAABAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public ConnectionTestMethodStateMethodCallHandler OnCall;
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

            string assetEndpoint = (string)_inputArguments[0];

            bool success = (bool)_outputArguments[0];
            string status = (string)_outputArguments[1];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    assetEndpoint,
                    ref success,
                    ref status);
            }

            _outputArguments[0] = success;
            _outputArguments[1] = status;

            return _result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <remarks />
    /// <exclude />
    public delegate ServiceResult ConnectionTestMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        string assetEndpoint,
        ref bool success,
        ref string status);
    #endif
    #endregion

    #region WoTAssetConfigurationState Class
    #if (!OPCUA_EXCLUDE_WoTAssetConfigurationState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class WoTAssetConfigurationState : BaseInterfaceState
    {
        #region Constructors
        /// <remarks />
        public WoTAssetConfigurationState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.WotCon.ObjectTypes.WoTAssetConfigurationType, Opc.Ua.WotCon.Namespaces.WotCon, namespaceUris);
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

            if (License != null)
            {
                License.Initialize(context, License_InitializationString);
            }
        }

        #region Initialization String
        private const string License_InitializationString =
           "AQAAACQAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29ULUNvbi//////FWCJCgIAAAABAAcA" +
           "AABMaWNlbnNlAQFzAAAuAERzAAAAAAz/////AQH/////AAAAAA==";

        private const string InitializationString =
           "AQAAACQAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29ULUNvbi//////BGCAAgEAAAABACEA" +
           "AABXb1RBc3NldENvbmZpZ3VyYXRpb25UeXBlSW5zdGFuY2UBAXEAAQFxAHEAAAD/////AQAAABVgiQoC" +
           "AAAAAQAHAAAATGljZW5zZQEBcwAALgBEcwAAAAAM/////wEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public PropertyState<string> License
        {
            get
            {
                return m_license;
            }

            set
            {
                if (!Object.ReferenceEquals(m_license, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_license = value;
            }
        }
        #endregion

        #region Overridden Methods
        /// <remarks />
        public override void GetChildren(
            ISystemContext context,
            IList<BaseInstanceState> children)
        {
            if (m_license != null)
            {
                children.Add(m_license);
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
                case Opc.Ua.WotCon.BrowseNames.License:
                {
                    if (createOrReplace)
                    {
                        if (License == null)
                        {
                            if (replacement == null)
                            {
                                License = new PropertyState<string>(this);
                            }
                            else
                            {
                                License = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = License;
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
        private PropertyState<string> m_license;
        #endregion
    }
    #endif
    #endregion

    #region IWoTAssetState Class
    #if (!OPCUA_EXCLUDE_IWoTAssetState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class IWoTAssetState : BaseInterfaceState
    {
        #region Constructors
        /// <remarks />
        public IWoTAssetState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.WotCon.ObjectTypes.IWoTAssetType, Opc.Ua.WotCon.Namespaces.WotCon, namespaceUris);
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

            if (AssetEndpoint != null)
            {
                AssetEndpoint.Initialize(context, AssetEndpoint_InitializationString);
            }
        }

        #region Initialization String
        private const string AssetEndpoint_InitializationString =
           "AQAAACQAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29ULUNvbi//////FWCJCgIAAAABAA0A" +
           "AABBc3NldEVuZHBvaW50AQF0AAAuAER0AAAAAAz/////AQH/////AAAAAA==";

        private const string InitializationString =
           "AQAAACQAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29ULUNvbi//////BGCAAgEAAAABABUA" +
           "AABJV29UQXNzZXRUeXBlSW5zdGFuY2UBATgAAQE4ADgAAAD/////AgAAAARggAoBAAAAAQAHAAAAV29U" +
           "RmlsZQEBOQAALwEBVgA5AAAA/////wsAAAAVYIkKAgAAAAAABAAAAFNpemUBAToAAC4ARDoAAAAACf//" +
           "//8BAf////8AAAAAFWCJCgIAAAAAAAgAAABXcml0YWJsZQEBOwAALgBEOwAAAAAB/////wEB/////wAA" +
           "AAAVYIkKAgAAAAAADAAAAFVzZXJXcml0YWJsZQEBPAAALgBEPAAAAAAB/////wEB/////wAAAAAVYIkK" +
           "AgAAAAAACQAAAE9wZW5Db3VudAEBPQAALgBEPQAAAAAF/////wEB/////wAAAAAEYYIKBAAAAAAABAAA" +
           "AE9wZW4BAUEAAC8BADwtQQAAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFC" +
           "AAAuAERCAAAAlgEAAAABACoBARMAAAAEAAAATW9kZQAD/////wAAAAAAAQAoAQEAAAABAAAAAQAAAAEB" +
           "/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBQwAALgBEQwAAAJYBAAAAAQAqAQEZ" +
           "AAAACgAAAEZpbGVIYW5kbGUAB/////8AAAAAAAEAKAEBAAAAAQAAAAEAAAABAf////8AAAAABGGCCgQA" +
           "AAAAAAUAAABDbG9zZQEBRAAALwEAPy1EAAAAAQH/////AQAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1" +
           "bWVudHMBAUUAAC4AREUAAACWAQAAAAEAKgEBGQAAAAoAAABGaWxlSGFuZGxlAAf/////AAAAAAABACgB" +
           "AQAAAAEAAAABAAAAAQH/////AAAAAARhggoEAAAAAAAEAAAAUmVhZAEBRgAALwEAQS1GAAAAAQH/////" +
           "AgAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAUcAAC4AREcAAACWAgAAAAEAKgEBGQAAAAoA" +
           "AABGaWxlSGFuZGxlAAf/////AAAAAAABACoBARUAAAAGAAAATGVuZ3RoAAb/////AAAAAAABACgBAQAA" +
           "AAEAAAACAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQFIAAAuAERIAAAA" +
           "lgEAAAABACoBARMAAAAEAAAARGF0YQAP/////wAAAAAAAQAoAQEAAAABAAAAAQAAAAEB/////wAAAAAE" +
           "YYIKBAAAAAAABQAAAFdyaXRlAQFJAAAvAQBELUkAAAABAf////8BAAAAF2CpCgIAAAAAAA4AAABJbnB1" +
           "dEFyZ3VtZW50cwEBSgAALgBESgAAAJYCAAAAAQAqAQEZAAAACgAAAEZpbGVIYW5kbGUAB/////8AAAAA" +
           "AAEAKgEBEwAAAAQAAABEYXRhAA//////AAAAAAABACgBAQAAAAEAAAACAAAAAQH/////AAAAAARhggoE" +
           "AAAAAAALAAAAR2V0UG9zaXRpb24BAUsAAC8BAEYtSwAAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElu" +
           "cHV0QXJndW1lbnRzAQFMAAAuAERMAAAAlgEAAAABACoBARkAAAAKAAAARmlsZUhhbmRsZQAH/////wAA" +
           "AAAAAQAoAQEAAAABAAAAAQAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEB" +
           "TQAALgBETQAAAJYBAAAAAQAqAQEXAAAACAAAAFBvc2l0aW9uAAn/////AAAAAAABACgBAQAAAAEAAAAB" +
           "AAAAAQH/////AAAAAARhggoEAAAAAAALAAAAU2V0UG9zaXRpb24BAU4AAC8BAEktTgAAAAEB/////wEA" +
           "AAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFPAAAuAERPAAAAlgIAAAABACoBARkAAAAKAAAA" +
           "RmlsZUhhbmRsZQAH/////wAAAAAAAQAqAQEXAAAACAAAAFBvc2l0aW9uAAn/////AAAAAAABACgBAQAA" +
           "AAEAAAACAAAAAQH/////AAAAAARhggoEAAAAAQAOAAAAQ2xvc2VBbmRVcGRhdGUBAVAAAC8BAVcAUAAA" +
           "AAEB/////wEAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFRAAAuAERRAAAAlgEAAAABACoB" +
           "ARkAAAAKAAAARmlsZUhhbmRsZQAH/////wAAAAAAAQAoAQEAAAABAAAAAQAAAAEB/////wAAAAAVYIkK" +
           "AgAAAAEADQAAAEFzc2V0RW5kcG9pbnQBAXQAAC4ARHQAAAAADP////8BAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public WoTAssetFileState WoTFile
        {
            get
            {
                return m_woTFile;
            }

            set
            {
                if (!Object.ReferenceEquals(m_woTFile, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_woTFile = value;
            }
        }

        /// <remarks />
        public PropertyState<string> AssetEndpoint
        {
            get
            {
                return m_assetEndpoint;
            }

            set
            {
                if (!Object.ReferenceEquals(m_assetEndpoint, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_assetEndpoint = value;
            }
        }
        #endregion

        #region Overridden Methods
        /// <remarks />
        public override void GetChildren(
            ISystemContext context,
            IList<BaseInstanceState> children)
        {
            if (m_woTFile != null)
            {
                children.Add(m_woTFile);
            }

            if (m_assetEndpoint != null)
            {
                children.Add(m_assetEndpoint);
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
                case Opc.Ua.WotCon.BrowseNames.WoTFile:
                {
                    if (createOrReplace)
                    {
                        if (WoTFile == null)
                        {
                            if (replacement == null)
                            {
                                WoTFile = new WoTAssetFileState(this);
                            }
                            else
                            {
                                WoTFile = (WoTAssetFileState)replacement;
                            }
                        }
                    }

                    instance = WoTFile;
                    break;
                }

                case Opc.Ua.WotCon.BrowseNames.AssetEndpoint:
                {
                    if (createOrReplace)
                    {
                        if (AssetEndpoint == null)
                        {
                            if (replacement == null)
                            {
                                AssetEndpoint = new PropertyState<string>(this);
                            }
                            else
                            {
                                AssetEndpoint = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = AssetEndpoint;
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
        private WoTAssetFileState m_woTFile;
        private PropertyState<string> m_assetEndpoint;
        #endregion
    }
    #endif
    #endregion

    #region CloseAndUpdateMethodState Class
    #if (!OPCUA_EXCLUDE_CloseAndUpdateMethodState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class CloseAndUpdateMethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public CloseAndUpdateMethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new CloseAndUpdateMethodState(parent);
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
           "AQAAACQAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29ULUNvbi//////BGGCCgQAAAABABgA" +
           "AABDbG9zZUFuZFVwZGF0ZU1ldGhvZFR5cGUBAVQAAC8BAVQAVAAAAAEB/////wEAAAAXYKkKAgAAAAAA" +
           "DgAAAElucHV0QXJndW1lbnRzAQFVAAAuAERVAAAAlgEAAAABACoBARkAAAAKAAAARmlsZUhhbmRsZQAH" +
           "/////wAAAAAAAQAoAQEAAAABAAAAAQAAAAEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public CloseAndUpdateMethodStateMethodCallHandler OnCall;
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

            uint fileHandle = (uint)_inputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    fileHandle);
            }

            return _result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <remarks />
    /// <exclude />
    public delegate ServiceResult CloseAndUpdateMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        uint fileHandle);
    #endif
    #endregion

    #region WoTAssetFileState Class
    #if (!OPCUA_EXCLUDE_WoTAssetFileState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class WoTAssetFileState : FileState
    {
        #region Constructors
        /// <remarks />
        public WoTAssetFileState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.WotCon.ObjectTypes.WoTAssetFileType, Opc.Ua.WotCon.Namespaces.WotCon, namespaceUris);
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
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACQAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvV29ULUNvbi//////BGCAAgEAAAABABgA" +
           "AABXb1RBc3NldEZpbGVUeXBlSW5zdGFuY2UBAVYAAQFWAFYAAAD/////CwAAABVgiQoCAAAAAAAEAAAA" +
           "U2l6ZQIBAEJCDwAALgBEQkIPAAAJ/////wEB/////wAAAAAVYIkKAgAAAAAACAAAAFdyaXRhYmxlAgEA" +
           "Q0IPAAAuAERDQg8AAAH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAAVXNlcldyaXRhYmxlAgEAREIP" +
           "AAAuAEREQg8AAAH/////AQH/////AAAAABVgiQoCAAAAAAAJAAAAT3BlbkNvdW50AgEARUIPAAAuAERF" +
           "Qg8AAAX/////AQH/////AAAAAARhggoEAAAAAAAEAAAAT3BlbgIBAElCDwAALwEAPC1JQg8AAQH/////" +
           "AgAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMCAQBKQg8AAC4AREpCDwCWAQAAAAEAKgEBEwAA" +
           "AAQAAABNb2RlAAP/////AAAAAAABACgBAQAAAAEAAAABAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAA" +
           "T3V0cHV0QXJndW1lbnRzAgEAS0IPAAAuAERLQg8AlgEAAAABACoBARkAAAAKAAAARmlsZUhhbmRsZQAH" +
           "/////wAAAAAAAQAoAQEAAAABAAAAAQAAAAEB/////wAAAAAEYYIKBAAAAAAABQAAAENsb3NlAgEATEIP" +
           "AAAvAQA/LUxCDwABAf////8BAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwIBAE1CDwAALgBE" +
           "TUIPAJYBAAAAAQAqAQEZAAAACgAAAEZpbGVIYW5kbGUAB/////8AAAAAAAEAKAEBAAAAAQAAAAEAAAAB" +
           "Af////8AAAAABGGCCgQAAAAAAAQAAABSZWFkAgEATkIPAAAvAQBBLU5CDwABAf////8CAAAAF2CpCgIA" +
           "AAAAAA4AAABJbnB1dEFyZ3VtZW50cwIBAE9CDwAALgBET0IPAJYCAAAAAQAqAQEZAAAACgAAAEZpbGVI" +
           "YW5kbGUAB/////8AAAAAAAEAKgEBFQAAAAYAAABMZW5ndGgABv////8AAAAAAAEAKAEBAAAAAQAAAAIA" +
           "AAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMCAQBQQg8AAC4ARFBCDwCWAQAA" +
           "AAEAKgEBEwAAAAQAAABEYXRhAA//////AAAAAAABACgBAQAAAAEAAAABAAAAAQH/////AAAAAARhggoE" +
           "AAAAAAAFAAAAV3JpdGUCAQBRQg8AAC8BAEQtUUIPAAEB/////wEAAAAXYKkKAgAAAAAADgAAAElucHV0" +
           "QXJndW1lbnRzAgEAUkIPAAAuAERSQg8AlgIAAAABACoBARkAAAAKAAAARmlsZUhhbmRsZQAH/////wAA" +
           "AAAAAQAqAQETAAAABAAAAERhdGEAD/////8AAAAAAAEAKAEBAAAAAQAAAAIAAAABAf////8AAAAABGGC" +
           "CgQAAAAAAAsAAABHZXRQb3NpdGlvbgIBAFNCDwAALwEARi1TQg8AAQH/////AgAAABdgqQoCAAAAAAAO" +
           "AAAASW5wdXRBcmd1bWVudHMCAQBUQg8AAC4ARFRCDwCWAQAAAAEAKgEBGQAAAAoAAABGaWxlSGFuZGxl" +
           "AAf/////AAAAAAABACgBAQAAAAEAAAABAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0cHV0QXJn" +
           "dW1lbnRzAgEAVUIPAAAuAERVQg8AlgEAAAABACoBARcAAAAIAAAAUG9zaXRpb24ACf////8AAAAAAAEA" +
           "KAEBAAAAAQAAAAEAAAABAf////8AAAAABGGCCgQAAAAAAAsAAABTZXRQb3NpdGlvbgIBAFZCDwAALwEA" +
           "SS1WQg8AAQH/////AQAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMCAQBXQg8AAC4ARFdCDwCW" +
           "AgAAAAEAKgEBGQAAAAoAAABGaWxlSGFuZGxlAAf/////AAAAAAABACoBARcAAAAIAAAAUG9zaXRpb24A" +
           "Cf////8AAAAAAAEAKAEBAAAAAQAAAAIAAAABAf////8AAAAABGGCCgQAAAABAA4AAABDbG9zZUFuZFVw" +
           "ZGF0ZQEBVwAALwEBVwBXAAAAAQH/////AQAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAVgA" +
           "AC4ARFgAAACWAQAAAAEAKgEBGQAAAAoAAABGaWxlSGFuZGxlAAf/////AAAAAAABACgBAQAAAAEAAAAB" +
           "AAAAAQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public CloseAndUpdateMethodState CloseAndUpdate
        {
            get
            {
                return m_closeAndUpdateMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_closeAndUpdateMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_closeAndUpdateMethod = value;
            }
        }
        #endregion

        #region Overridden Methods
        /// <remarks />
        public override void GetChildren(
            ISystemContext context,
            IList<BaseInstanceState> children)
        {
            if (m_closeAndUpdateMethod != null)
            {
                children.Add(m_closeAndUpdateMethod);
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
                case Opc.Ua.WotCon.BrowseNames.CloseAndUpdate:
                {
                    if (createOrReplace)
                    {
                        if (CloseAndUpdate == null)
                        {
                            if (replacement == null)
                            {
                                CloseAndUpdate = new CloseAndUpdateMethodState(this);
                            }
                            else
                            {
                                CloseAndUpdate = (CloseAndUpdateMethodState)replacement;
                            }
                        }
                    }

                    instance = CloseAndUpdate;
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
        private CloseAndUpdateMethodState m_closeAndUpdateMethod;
        #endregion
    }
    #endif
    #endregion
}
