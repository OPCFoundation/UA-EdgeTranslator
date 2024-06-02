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

using Opc.Ua;

namespace UAModel.WoT_Con
{
    #region Method Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Methods
    {
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
        public const uint WoTAssetType_WoTFile_Open = 124;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_Close = 127;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_Read = 129;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_Write = 132;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_GetPosition = 134;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_SetPosition = 137;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_CloseAndUpdate = 139;

        /// <remarks />
        public const uint WoTAssetFileType_CloseAndUpdate = 111;

        /// <remarks />
        public const string WoTAssetConnectionManagementType_CreateAssetMethodType = "";

        /// <remarks />
        public const string WoTAssetConnectionManagementType_DeleteAssetMethodType = "";

        /// <remarks />
        public const string WoTAssetFileType_CloseAndUpdateMethodType = "";
    }
    #endregion

    #region Object Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Objects
    {
        /// <remarks />
        public const uint WoTAssetConnectionManagementType_WoTAssetName_Placeholder = 2;

        /// <remarks />
        public const uint WoTAssetConnectionManagement = 31;

        /// <remarks />
        public const uint IWoTAssetType_WoTFile = 43;

        /// <remarks />
        public const uint WoTAssetType_WoTFile = 116;
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
        public const uint WoTAssetType = 115;

        /// <remarks />
        public const uint WoTAssetFileType = 110;
    }
    #endregion

    #region ReferenceType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ReferenceTypes
    {
        /// <remarks />
        public const uint HasWoTComponent = 142;
    }
    #endregion

    #region Variable Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Variables
    {
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
        public const uint WoTAssetType_WoTFile_Size = 117;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_Writable = 118;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_UserWritable = 119;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_OpenCount = 120;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_Open_InputArguments = 125;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_Open_OutputArguments = 126;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_Close_InputArguments = 128;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_Read_InputArguments = 130;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_Read_OutputArguments = 131;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_Write_InputArguments = 133;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_GetPosition_InputArguments = 135;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_GetPosition_OutputArguments = 136;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_SetPosition_InputArguments = 138;

        /// <remarks />
        public const uint WoTAssetType_WoTFile_CloseAndUpdate_InputArguments = 140;

        /// <remarks />
        public const uint WoTAssetType_WoTPropertyName_Placeholder = 141;

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
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_CreateAsset = new ExpandedNodeId(UAModel.WoT_Con.Methods.WoTAssetConnectionManagementType_CreateAsset, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_DeleteAsset = new ExpandedNodeId(UAModel.WoT_Con.Methods.WoTAssetConnectionManagementType_DeleteAsset, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagement_CreateAsset = new ExpandedNodeId(UAModel.WoT_Con.Methods.WoTAssetConnectionManagement_CreateAsset, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagement_DeleteAsset = new ExpandedNodeId(UAModel.WoT_Con.Methods.WoTAssetConnectionManagement_DeleteAsset, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Open = new ExpandedNodeId(UAModel.WoT_Con.Methods.IWoTAssetType_WoTFile_Open, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Close = new ExpandedNodeId(UAModel.WoT_Con.Methods.IWoTAssetType_WoTFile_Close, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Read = new ExpandedNodeId(UAModel.WoT_Con.Methods.IWoTAssetType_WoTFile_Read, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Write = new ExpandedNodeId(UAModel.WoT_Con.Methods.IWoTAssetType_WoTFile_Write, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_GetPosition = new ExpandedNodeId(UAModel.WoT_Con.Methods.IWoTAssetType_WoTFile_GetPosition, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_SetPosition = new ExpandedNodeId(UAModel.WoT_Con.Methods.IWoTAssetType_WoTFile_SetPosition, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_CloseAndUpdate = new ExpandedNodeId(UAModel.WoT_Con.Methods.IWoTAssetType_WoTFile_CloseAndUpdate, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_Open = new ExpandedNodeId(UAModel.WoT_Con.Methods.WoTAssetType_WoTFile_Open, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_Close = new ExpandedNodeId(UAModel.WoT_Con.Methods.WoTAssetType_WoTFile_Close, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_Read = new ExpandedNodeId(UAModel.WoT_Con.Methods.WoTAssetType_WoTFile_Read, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_Write = new ExpandedNodeId(UAModel.WoT_Con.Methods.WoTAssetType_WoTFile_Write, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_GetPosition = new ExpandedNodeId(UAModel.WoT_Con.Methods.WoTAssetType_WoTFile_GetPosition, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_SetPosition = new ExpandedNodeId(UAModel.WoT_Con.Methods.WoTAssetType_WoTFile_SetPosition, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_CloseAndUpdate = new ExpandedNodeId(UAModel.WoT_Con.Methods.WoTAssetType_WoTFile_CloseAndUpdate, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetFileType_CloseAndUpdate = new ExpandedNodeId(UAModel.WoT_Con.Methods.WoTAssetFileType_CloseAndUpdate, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_CreateAssetMethodType = new ExpandedNodeId(UAModel.WoT_Con.Methods.WoTAssetConnectionManagementType_CreateAssetMethodType, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_DeleteAssetMethodType = new ExpandedNodeId(UAModel.WoT_Con.Methods.WoTAssetConnectionManagementType_DeleteAssetMethodType, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetFileType_CloseAndUpdateMethodType = new ExpandedNodeId(UAModel.WoT_Con.Methods.WoTAssetFileType_CloseAndUpdateMethodType, UAModel.WoT_Con.Namespaces.WoT_Con);
    }
    #endregion

    #region Object Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_WoTAssetName_Placeholder = new ExpandedNodeId(UAModel.WoT_Con.Objects.WoTAssetConnectionManagementType_WoTAssetName_Placeholder, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagement = new ExpandedNodeId(UAModel.WoT_Con.Objects.WoTAssetConnectionManagement, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile = new ExpandedNodeId(UAModel.WoT_Con.Objects.IWoTAssetType_WoTFile, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile = new ExpandedNodeId(UAModel.WoT_Con.Objects.WoTAssetType_WoTFile, UAModel.WoT_Con.Namespaces.WoT_Con);
    }
    #endregion

    #region ObjectType Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypeIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType = new ExpandedNodeId(UAModel.WoT_Con.ObjectTypes.WoTAssetConnectionManagementType, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType = new ExpandedNodeId(UAModel.WoT_Con.ObjectTypes.IWoTAssetType, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType = new ExpandedNodeId(UAModel.WoT_Con.ObjectTypes.WoTAssetType, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetFileType = new ExpandedNodeId(UAModel.WoT_Con.ObjectTypes.WoTAssetFileType, UAModel.WoT_Con.Namespaces.WoT_Con);
    }
    #endregion

    #region ReferenceType Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ReferenceTypeIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId HasWoTComponent = new ExpandedNodeId(UAModel.WoT_Con.ReferenceTypes.HasWoTComponent, UAModel.WoT_Con.Namespaces.WoT_Con);
    }
    #endregion

    #region Variable Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_CreateAsset_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetConnectionManagementType_CreateAsset_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_CreateAsset_OutputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetConnectionManagementType_CreateAsset_OutputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagementType_DeleteAsset_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetConnectionManagementType_DeleteAsset_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagement_CreateAsset_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetConnectionManagement_CreateAsset_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagement_CreateAsset_OutputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetConnectionManagement_CreateAsset_OutputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetConnectionManagement_DeleteAsset_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetConnectionManagement_DeleteAsset_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Size = new ExpandedNodeId(UAModel.WoT_Con.Variables.IWoTAssetType_WoTFile_Size, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Writable = new ExpandedNodeId(UAModel.WoT_Con.Variables.IWoTAssetType_WoTFile_Writable, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_UserWritable = new ExpandedNodeId(UAModel.WoT_Con.Variables.IWoTAssetType_WoTFile_UserWritable, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_OpenCount = new ExpandedNodeId(UAModel.WoT_Con.Variables.IWoTAssetType_WoTFile_OpenCount, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Open_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.IWoTAssetType_WoTFile_Open_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Open_OutputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.IWoTAssetType_WoTFile_Open_OutputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Close_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.IWoTAssetType_WoTFile_Close_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Read_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.IWoTAssetType_WoTFile_Read_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Read_OutputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.IWoTAssetType_WoTFile_Read_OutputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_Write_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.IWoTAssetType_WoTFile_Write_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_GetPosition_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.IWoTAssetType_WoTFile_GetPosition_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_GetPosition_OutputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.IWoTAssetType_WoTFile_GetPosition_OutputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_SetPosition_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.IWoTAssetType_WoTFile_SetPosition_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTFile_CloseAndUpdate_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.IWoTAssetType_WoTFile_CloseAndUpdate_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId IWoTAssetType_WoTPropertyName_Placeholder = new ExpandedNodeId(UAModel.WoT_Con.Variables.IWoTAssetType_WoTPropertyName_Placeholder, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_Size = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetType_WoTFile_Size, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_Writable = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetType_WoTFile_Writable, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_UserWritable = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetType_WoTFile_UserWritable, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_OpenCount = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetType_WoTFile_OpenCount, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_Open_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetType_WoTFile_Open_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_Open_OutputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetType_WoTFile_Open_OutputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_Close_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetType_WoTFile_Close_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_Read_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetType_WoTFile_Read_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_Read_OutputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetType_WoTFile_Read_OutputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_Write_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetType_WoTFile_Write_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_GetPosition_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetType_WoTFile_GetPosition_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_GetPosition_OutputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetType_WoTFile_GetPosition_OutputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_SetPosition_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetType_WoTFile_SetPosition_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTFile_CloseAndUpdate_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetType_WoTFile_CloseAndUpdate_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetType_WoTPropertyName_Placeholder = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetType_WoTPropertyName_Placeholder, UAModel.WoT_Con.Namespaces.WoT_Con);

        /// <remarks />
        public static readonly ExpandedNodeId WoTAssetFileType_CloseAndUpdate_InputArguments = new ExpandedNodeId(UAModel.WoT_Con.Variables.WoTAssetFileType_CloseAndUpdate_InputArguments, UAModel.WoT_Con.Namespaces.WoT_Con);
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
        public const string CloseAndUpdateMethodType = "CloseAndUpdateMethodType";

        /// <remarks />
        public const string CreateAsset = "CreateAsset";

        /// <remarks />
        public const string CreateAssetMethodType = "CreateAssetMethodType";

        /// <remarks />
        public const string DeleteAsset = "DeleteAsset";

        /// <remarks />
        public const string DeleteAssetMethodType = "DeleteAssetMethodType";

        /// <remarks />
        public const string HasWoTComponent = "HasWoTComponent";

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
        public const string WoTAssetType = "WoTAssetType";

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
        /// The URI for the WoT_Con namespace (.NET code namespace is 'UAModel.WoT_Con').
        /// </summary>
        public const string WoT_Con = "http://opcfoundation.org/UA/WoT-Con/";

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
