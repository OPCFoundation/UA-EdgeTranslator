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

namespace Opc.Ua.WotCon
{
    #region Method Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Methods
    {
        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_Open = 83;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_Close = 86;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_Read = 88;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_Write = 91;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_GetPosition = 93;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_SetPosition = 96;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Open = 11;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Close = 14;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Read = 16;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Write = 19;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_GetPosition = 21;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_SetPosition = 24;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_CloseAndUpdate = 104;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_CreateAsset = 26;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_DeleteAsset = 29;

        /// <remarks />
        public const uint WoTAssetConnectionManagement_CreateAsset = 32;

        /// <remarks />
        public const uint WoTAssetConnectionManagement_DeleteAsset = 35;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_Open = 51;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_Close = 54;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_Read = 56;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_Write = 59;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_GetPosition = 61;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_SetPosition = 64;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_CloseAndUpdate = 106;

        /// <remarks />
        public const uint WoTAssetFileType_CloseAndUpdate = 111;
    }
    #endregion

    #region Object Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Objects
    {
        /// <remarks />
        public const uint WotConNamespaceMetadata = 67;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder = 2;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile = 3;

        /// <remarks />
        public const uint WoTAssetConnectionManagement = 31;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile = 43;
    }
    #endregion

    #region ObjectType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypes
    {
        /// <remarks />
        public const uint WoTAssetConnectionManagementType = 1;

        /// <remarks />
        public const uint IWoTAssetType = 42;

        /// <remarks />
        public const uint WoTAssetFileType = 110;
    }
    #endregion

    #region Variable Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Variables
    {
        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceUri = 68;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceVersion = 69;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespacePublicationDate = 70;

        /// <remarks />
        public const uint WotConNamespaceMetadata_IsNamespaceSubset = 71;

        /// <remarks />
        public const uint WotConNamespaceMetadata_StaticNodeIdTypes = 72;

        /// <remarks />
        public const uint WotConNamespaceMetadata_StaticNumericNodeIdRange = 73;

        /// <remarks />
        public const uint WotConNamespaceMetadata_StaticStringNodeIdPattern = 74;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_Size = 76;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_Writable = 77;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_UserWritable = 78;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_OpenCount = 79;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_Open_InputArguments = 84;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_Open_OutputArguments = 85;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_Close_InputArguments = 87;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_Read_InputArguments = 89;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_Read_OutputArguments = 90;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_Write_InputArguments = 92;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_GetPosition_InputArguments = 94;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_GetPosition_OutputArguments = 95;

        /// <remarks />
        public const uint WotConNamespaceMetadata_NamespaceFile_SetPosition_InputArguments = 97;

        /// <remarks />
        public const uint WotConNamespaceMetadata_DefaultRolePermissions = 99;

        /// <remarks />
        public const uint WotConNamespaceMetadata_DefaultUserRolePermissions = 100;

        /// <remarks />
        public const uint WotConNamespaceMetadata_DefaultAccessRestrictions = 101;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Size = 4;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Writable = 5;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_UserWritable = 6;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_OpenCount = 7;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Open_InputArguments = 12;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Open_OutputArguments = 13;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Close_InputArguments = 15;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Read_InputArguments = 17;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Read_OutputArguments = 18;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Write_InputArguments = 20;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_GetPosition_InputArguments = 22;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_GetPosition_OutputArguments = 23;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_SetPosition_InputArguments = 25;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_CloseAndUpdate_InputArguments = 105;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_CreateAsset_InputArguments = 27;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_CreateAsset_OutputArguments = 28;

        /// <remarks />
        public const uint WoTAssetConnectionManagementType_DeleteAsset_InputArguments = 30;

        /// <remarks />
        public const uint WoTAssetConnectionManagement_CreateAsset_InputArguments = 33;

        /// <remarks />
        public const uint WoTAssetConnectionManagement_CreateAsset_OutputArguments = 34;

        /// <remarks />
        public const uint WoTAssetConnectionManagement_DeleteAsset_InputArguments = 36;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_Size = 44;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_Writable = 45;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_UserWritable = 46;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_OpenCount = 47;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_Open_InputArguments = 52;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_Open_OutputArguments = 53;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_Close_InputArguments = 55;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_Read_InputArguments = 57;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_Read_OutputArguments = 58;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_Write_InputArguments = 60;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_GetPosition_InputArguments = 62;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_GetPosition_OutputArguments = 63;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_SetPosition_InputArguments = 65;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile_CloseAndUpdate_InputArguments = 107;

        /// <remarks />
        public const uint IWoTAssetType_WoTPropertyName_Placeholder = 66;

        /// <remarks />
        public const uint WoTAssetFileType_CloseAndUpdate_InputArguments = 112;
    }
    #endregion

    #region Method Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class MethodIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_Open = new ExpandedNodeId(Opc.Ua.WotCon.Methods.WotConNamespaceMetadata_NamespaceFile_Open, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_Close = new ExpandedNodeId(Opc.Ua.WotCon.Methods.WotConNamespaceMetadata_NamespaceFile_Close, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_Read = new ExpandedNodeId(Opc.Ua.WotCon.Methods.WotConNamespaceMetadata_NamespaceFile_Read, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_Write = new ExpandedNodeId(Opc.Ua.WotCon.Methods.WotConNamespaceMetadata_NamespaceFile_Write, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_GetPosition = new ExpandedNodeId(Opc.Ua.WotCon.Methods.WotConNamespaceMetadata_NamespaceFile_GetPosition, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_SetPosition = new ExpandedNodeId(Opc.Ua.WotCon.Methods.WotConNamespaceMetadata_NamespaceFile_SetPosition, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Open = new ExpandedNodeId(Opc.Ua.WotCon.Methods.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Open, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Close = new ExpandedNodeId(Opc.Ua.WotCon.Methods.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Close, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Read = new ExpandedNodeId(Opc.Ua.WotCon.Methods.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Read, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Write = new ExpandedNodeId(Opc.Ua.WotCon.Methods.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Write, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_GetPosition = new ExpandedNodeId(Opc.Ua.WotCon.Methods.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_GetPosition, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_SetPosition = new ExpandedNodeId(Opc.Ua.WotCon.Methods.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_SetPosition, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_CloseAndUpdate = new ExpandedNodeId(Opc.Ua.WotCon.Methods.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_CloseAndUpdate, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_CreateAsset = new ExpandedNodeId(Opc.Ua.WotCon.Methods.WoTAssetConnectionManagementType_CreateAsset, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_DeleteAsset = new ExpandedNodeId(Opc.Ua.WotCon.Methods.WoTAssetConnectionManagementType_DeleteAsset, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagement_CreateAsset = new ExpandedNodeId(Opc.Ua.WotCon.Methods.WoTAssetConnectionManagement_CreateAsset, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagement_DeleteAsset = new ExpandedNodeId(Opc.Ua.WotCon.Methods.WoTAssetConnectionManagement_DeleteAsset, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Open = new ExpandedNodeId(Opc.Ua.WotCon.Methods.IWoTAssetType_WoTFile_Open, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Close = new ExpandedNodeId(Opc.Ua.WotCon.Methods.IWoTAssetType_WoTFile_Close, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Read = new ExpandedNodeId(Opc.Ua.WotCon.Methods.IWoTAssetType_WoTFile_Read, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Write = new ExpandedNodeId(Opc.Ua.WotCon.Methods.IWoTAssetType_WoTFile_Write, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_GetPosition = new ExpandedNodeId(Opc.Ua.WotCon.Methods.IWoTAssetType_WoTFile_GetPosition, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_SetPosition = new ExpandedNodeId(Opc.Ua.WotCon.Methods.IWoTAssetType_WoTFile_SetPosition, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_CloseAndUpdate = new ExpandedNodeId(Opc.Ua.WotCon.Methods.IWoTAssetType_WoTFile_CloseAndUpdate, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetFileType_CloseAndUpdate = new ExpandedNodeId(Opc.Ua.WotCon.Methods.WoTAssetFileType_CloseAndUpdate, Opc.Ua.WotCon.Namespaces.WotCon);
    }
    #endregion

    #region Object Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata = new ExpandedNodeId(Opc.Ua.WotCon.Objects.WotConNamespaceMetadata, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder = new ExpandedNodeId(Opc.Ua.WotCon.Objects.WoTAssetConnectionManagementType_WoTAssetName_Placeholder, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile = new ExpandedNodeId(Opc.Ua.WotCon.Objects.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagement = new ExpandedNodeId(Opc.Ua.WotCon.Objects.WoTAssetConnectionManagement, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile = new ExpandedNodeId(Opc.Ua.WotCon.Objects.IWoTAssetType_WoTFile, Opc.Ua.WotCon.Namespaces.WotCon);
    }
    #endregion

    #region ObjectType Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypeIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType = new ExpandedNodeId(Opc.Ua.WotCon.ObjectTypes.WoTAssetConnectionManagementType, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType = new ExpandedNodeId(Opc.Ua.WotCon.ObjectTypes.IWoTAssetType, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetFileType = new ExpandedNodeId(Opc.Ua.WotCon.ObjectTypes.WoTAssetFileType, Opc.Ua.WotCon.Namespaces.WotCon);
    }
    #endregion

    #region Variable Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceUri = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_NamespaceUri, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceVersion = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_NamespaceVersion, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespacePublicationDate = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_NamespacePublicationDate, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_IsNamespaceSubset = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_IsNamespaceSubset, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_StaticNodeIdTypes = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_StaticNodeIdTypes, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_StaticNumericNodeIdRange = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_StaticNumericNodeIdRange, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_StaticStringNodeIdPattern = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_StaticStringNodeIdPattern, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_Size = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_NamespaceFile_Size, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_Writable = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_NamespaceFile_Writable, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_UserWritable = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_NamespaceFile_UserWritable, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_OpenCount = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_NamespaceFile_OpenCount, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_Open_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_NamespaceFile_Open_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_Open_OutputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_NamespaceFile_Open_OutputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_Close_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_NamespaceFile_Close_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_Read_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_NamespaceFile_Read_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_Read_OutputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_NamespaceFile_Read_OutputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_Write_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_NamespaceFile_Write_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_GetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_NamespaceFile_GetPosition_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_GetPosition_OutputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_NamespaceFile_GetPosition_OutputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_NamespaceFile_SetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_NamespaceFile_SetPosition_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_DefaultRolePermissions = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_DefaultRolePermissions, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_DefaultUserRolePermissions = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_DefaultUserRolePermissions, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WotConNamespaceMetadata_DefaultAccessRestrictions = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WotConNamespaceMetadata_DefaultAccessRestrictions, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Size = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Size, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Writable = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Writable, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_UserWritable = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_UserWritable, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_OpenCount = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_OpenCount, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Open_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Open_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Open_OutputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Open_OutputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Close_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Close_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Read_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Read_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Read_OutputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Read_OutputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Write_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_Write_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_GetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_GetPosition_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_GetPosition_OutputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_GetPosition_OutputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_SetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_SetPosition_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_CloseAndUpdate_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagementType_WoTAssetName_Placeholder_WoTFile_CloseAndUpdate_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_CreateAsset_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagementType_CreateAsset_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_CreateAsset_OutputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagementType_CreateAsset_OutputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_DeleteAsset_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagementType_DeleteAsset_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagement_CreateAsset_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagement_CreateAsset_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagement_CreateAsset_OutputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagement_CreateAsset_OutputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagement_DeleteAsset_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetConnectionManagement_DeleteAsset_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Size = new ExpandedNodeId(Opc.Ua.WotCon.Variables.IWoTAssetType_WoTFile_Size, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Writable = new ExpandedNodeId(Opc.Ua.WotCon.Variables.IWoTAssetType_WoTFile_Writable, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_UserWritable = new ExpandedNodeId(Opc.Ua.WotCon.Variables.IWoTAssetType_WoTFile_UserWritable, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_OpenCount = new ExpandedNodeId(Opc.Ua.WotCon.Variables.IWoTAssetType_WoTFile_OpenCount, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Open_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.IWoTAssetType_WoTFile_Open_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Open_OutputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.IWoTAssetType_WoTFile_Open_OutputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Close_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.IWoTAssetType_WoTFile_Close_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Read_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.IWoTAssetType_WoTFile_Read_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Read_OutputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.IWoTAssetType_WoTFile_Read_OutputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Write_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.IWoTAssetType_WoTFile_Write_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_GetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.IWoTAssetType_WoTFile_GetPosition_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_GetPosition_OutputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.IWoTAssetType_WoTFile_GetPosition_OutputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_SetPosition_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.IWoTAssetType_WoTFile_SetPosition_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_CloseAndUpdate_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.IWoTAssetType_WoTFile_CloseAndUpdate_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTPropertyName_Placeholder = new ExpandedNodeId(Opc.Ua.WotCon.Variables.IWoTAssetType_WoTPropertyName_Placeholder, Opc.Ua.WotCon.Namespaces.WotCon);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetFileType_CloseAndUpdate_InputArguments = new ExpandedNodeId(Opc.Ua.WotCon.Variables.WoTAssetFileType_CloseAndUpdate_InputArguments, Opc.Ua.WotCon.Namespaces.WotCon);
    }
    #endregion

    #region BrowseName Declarations
    /// <remarks />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class BrowseNames
    {
        /// <remarks />
        public const string CloseAndUpdate = "CloseAndUpdate";

        /// <remarks />
        public const string CreateAsset = "CreateAsset";

        /// <remarks />
        public const string DeleteAsset = "DeleteAsset";

        /// <remarks />
        public const string IWoTAssetType = "IWoTAssetType";

        /// <remarks />
        public const string WoTAssetConnectionManagement = "WoTAssetConnectionManagement";

        /// <remarks />
        public const string WoTAssetConnectionManagementType = "WoTAssetConnectionManagementType";

        /// <remarks />
        public const string WoTAssetFileType = "WoTAssetFileType";

        /// <remarks />
        public const string WoTAssetName_Placeholder = "<WoTAssetName>";

        /// <remarks />
        public const string WotConNamespaceMetadata = "http://opcfoundation.org/UA/WoT-Con/";

        /// <remarks />
        public const string WoTFile = "WoTFile";

        /// <remarks />
        public const string WoTPropertyName_Placeholder = "<WoTPropertyName>";
    }
    #endregion

    #region Namespace Declarations
    /// <remarks />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Namespaces
    {
        /// <summary>
        /// The URI for the WotCon namespace (.NET code namespace is 'Opc.Ua.WotCon').
        /// </summary>
        public const string WotCon = "http://opcfoundation.org/UA/WoT-Con/";

        /// <summary>
        /// The URI for the OpcUa namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUa = "http://opcfoundation.org/UA/";

        /// <summary>
        /// The URI for the OpcUaXsd namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUaXsd = "http://opcfoundation.org/UA/2008/02/Types.xsd";
    }
    #endregion
}
