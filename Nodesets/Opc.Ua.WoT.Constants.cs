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
using System.Reflection;
using System.Xml;
using System.Runtime.Serialization;
using Opc.Ua;

namespace Opc.Ua.WoT
{
    #region Object Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Objects
    {
        /// <remarks />
        public const uint http___opcfoundation_org_UA_WoT_ = 5000;

        /// <remarks />
        public const uint AssetManagement = 5001;
    }
    #endregion

    #region Variable Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Variables
    {
        /// <remarks />
        public const uint http___opcfoundation_org_UA_WoT__NamespaceUri = 6002;

        /// <remarks />
        public const uint http___opcfoundation_org_UA_WoT__NamespaceVersion = 6003;

        /// <remarks />
        public const uint http___opcfoundation_org_UA_WoT__NamespacePublicationDate = 6001;

        /// <remarks />
        public const uint http___opcfoundation_org_UA_WoT__IsNamespaceSubset = 6000;

        /// <remarks />
        public const uint http___opcfoundation_org_UA_WoT__StaticNodeIdTypes = 6004;

        /// <remarks />
        public const uint http___opcfoundation_org_UA_WoT__StaticNumericNodeIdRange = 6005;

        /// <remarks />
        public const uint http___opcfoundation_org_UA_WoT__StaticStringNodeIdPattern = 6006;
    }
    #endregion

    #region Object Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId http___opcfoundation_org_UA_WoT_ = new ExpandedNodeId(Opc.Ua.WoT.Objects.http___opcfoundation_org_UA_WoT_, Opc.Ua.WoT.Namespaces.WoT);

        /// <remarks />
        public static readonly ExpandedNodeId AssetManagement = new ExpandedNodeId(Opc.Ua.WoT.Objects.AssetManagement, Opc.Ua.WoT.Namespaces.WoT);
    }
    #endregion

    #region Variable Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId http___opcfoundation_org_UA_WoT__NamespaceUri = new ExpandedNodeId(Opc.Ua.WoT.Variables.http___opcfoundation_org_UA_WoT__NamespaceUri, Opc.Ua.WoT.Namespaces.WoT);

        /// <remarks />
        public static readonly ExpandedNodeId http___opcfoundation_org_UA_WoT__NamespaceVersion = new ExpandedNodeId(Opc.Ua.WoT.Variables.http___opcfoundation_org_UA_WoT__NamespaceVersion, Opc.Ua.WoT.Namespaces.WoT);

        /// <remarks />
        public static readonly ExpandedNodeId http___opcfoundation_org_UA_WoT__NamespacePublicationDate = new ExpandedNodeId(Opc.Ua.WoT.Variables.http___opcfoundation_org_UA_WoT__NamespacePublicationDate, Opc.Ua.WoT.Namespaces.WoT);

        /// <remarks />
        public static readonly ExpandedNodeId http___opcfoundation_org_UA_WoT__IsNamespaceSubset = new ExpandedNodeId(Opc.Ua.WoT.Variables.http___opcfoundation_org_UA_WoT__IsNamespaceSubset, Opc.Ua.WoT.Namespaces.WoT);

        /// <remarks />
        public static readonly ExpandedNodeId http___opcfoundation_org_UA_WoT__StaticNodeIdTypes = new ExpandedNodeId(Opc.Ua.WoT.Variables.http___opcfoundation_org_UA_WoT__StaticNodeIdTypes, Opc.Ua.WoT.Namespaces.WoT);

        /// <remarks />
        public static readonly ExpandedNodeId http___opcfoundation_org_UA_WoT__StaticNumericNodeIdRange = new ExpandedNodeId(Opc.Ua.WoT.Variables.http___opcfoundation_org_UA_WoT__StaticNumericNodeIdRange, Opc.Ua.WoT.Namespaces.WoT);

        /// <remarks />
        public static readonly ExpandedNodeId http___opcfoundation_org_UA_WoT__StaticStringNodeIdPattern = new ExpandedNodeId(Opc.Ua.WoT.Variables.http___opcfoundation_org_UA_WoT__StaticStringNodeIdPattern, Opc.Ua.WoT.Namespaces.WoT);
    }
    #endregion

    #region BrowseName Declarations
    /// <remarks />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class BrowseNames
    {
        /// <remarks />
        public const string AssetManagement = "AssetManagement";

        /// <remarks />
        public const string ConfigureAsset = "ConfigureAsset";

        /// <remarks />
        public const string DeleteAsset = "DeleteAsset";

        /// <remarks />
        public const string GetConfiguredAssets = "GetConfiguredAssets";

        /// <remarks />
        public const string http___opcfoundation_org_UA_WoT_ = "http://opcfoundation.org/UA/WoT/";
    }
    #endregion

    #region Namespace Declarations
    /// <remarks />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Namespaces
    {
        /// <summary>
        /// The URI for the WoT namespace (.NET code namespace is 'Opc.Ua.WoT').
        /// </summary>
        public const string WoT = "http://opcfoundation.org/UA/WoT/";

        /// <summary>
        /// The URI for the OpcUa namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUa = "http://opcfoundation.org/UA/";
    }
    #endregion
}