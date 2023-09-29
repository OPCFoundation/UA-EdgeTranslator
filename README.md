# UA Edge Translator
An industrial connectivity edge reference application translating from proprietary protocols to [OPC UA](https://opcfoundation.org/) leveraging the [W3C Web of Things (WoT)](https://www.w3.org/WoT/) thing descriptions as well as [IoT Plug and Play digital twins](https://learn.microsoft.com/en-us/azure/iot-develop/concepts-digital-twin). Thing descriptions can be easily edited using the [Eclipse Foundation's edi{TD}or](https://eclipse.github.io/editdor/).

## How It Works

UA Edge Translator solves the common "brownfield" use case of connecting disparate industrial assets with proprietary interfaces and translates their data into an OPC UA information model (ideally to one of the [standardized companion specifications](https://opcfoundation.org/developer-tools/documents/) from the [UA Cloud Library](https://uacloudlibrary.opcfoundation.org/)), enabling processing of the assets' data either on the edge or in the cloud leveraging a normalized, IEC standard (OPC UA) data format. This accelerates Industrial IoT projects and saves cost since the data doesn't need to be normalized in the cloud and makes use of the OT expertise often only found on-premises. For defining a mapping from the proprietary data format to OPC UA, the Web of Things (WoT) Thing Description schema (JSON-LD-based) or the IoT Plug and Play digital twins schema (also JSON-LD-based) is used. Additionally, the mechanism to provide the schema to the UA Edge Translator is also leveraging OPC UA. Therefore, for the first time, OPC UA is used for both the control and data plane for industrial connectivity, while previous solutions only used OPC UA for the data plane and a proprietary REST interface for the control plane.

## Installation

UA Edge Translator is available as a pre-built Docker container and will run on any Docker- or Kubernetes-enabled edge device. See "Packages" in this repo for details.

## Operation

UA Edge Translator can be controlled through the use of just 3 OPC UA methods readily available through the OPC UA server interface built in. The methods are:

* ConfigureAsset(thingDescription) - configures a new asset, returning the ID of the newly configured asset on success
* DeleteAsset(assetId) - deletes a configured asset
* GetConfiguredAssets() - returns a list of configured assetIds, each element in the list is a WoT Thing Description ID

## Supported "Southbound" Asset Interfaces

In this reference implementation, only Modbus TCP is supported, but other interfaces can easily be added by implementing the IAsset interface.

## Optional Environment Variables

* UACLURL - UA Cloud Library URL (e.g. https://uacloudlibrary.opcfoundation.org or https://cloudlib.cesmii.net)
* UACLUsername - UA Cloud Library Username
* UACLPassword - UA Cloud Library Password
