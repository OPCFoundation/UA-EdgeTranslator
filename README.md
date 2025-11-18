# UA Edge Translator
An industrial connectivity edge reference application translating from proprietary protocols to [OPC UA](https://opcfoundation.org/) leveraging the [W3C Web of Things (WoT)](https://www.w3.org/WoT/) thing descriptions via the new [WoT-Connectivity specification](https://reference.opcfoundation.org/WoT/v100/docs/). Thing Descriptions can be easily edited using the [Eclipse Foundation's edi{TD}or](https://eclipse.github.io/editdor/).

## How It Works

UA Edge Translator solves the common "brownfield" use case of connecting disparate industrial assets with proprietary interfaces and translates their data into an OPC UA information model (ideally to one of the [standardized companion specifications](https://opcfoundation.org/developer-tools/documents/) from the [UA Cloud Library](https://uacloudlibrary.opcfoundation.org/)), enabling processing of the assets' data either on the edge or in the cloud leveraging a normalized, IEC standard (OPC UA) data format. This accelerates Industrial IoT projects and saves cost since the data doesn't need to be normalized in the cloud and makes use of the OT expertise often only found on-premises. For defining a mapping from the proprietary data format to OPC UA, the Web of Things (WoT) Thing Description schema (JSON-LD-based) is used. Additionally, the mechanism to provide the schema to the UA Edge Translator is also leveraging OPC UA. Therefore, for the first time, OPC UA is used for both the control and data plane for industrial connectivity, while previous solutions only used OPC UA for the data plane and a proprietary REST interface for the control plane.

## Installation

UA Edge Translator is available as a pre-built Docker container (supporting both AMD64 and ARM64 CPUs) directly from GitHub and will run on any Docker- or Kubernetes-enabled edge device. See "Packages" in this repo for details.

## Provisioning
UA Edge Translator supports provisioning via GDS Server Push functionality as described in part 12 of the OPC UA specification. Until an issuer certificate is provided in the issuer certificate store of UA Edge Translator, it is in provisioning mode and access to the WoT-Connectivity-related OPC UA nodes in its address space is restricted. An issuer certificate can be provided as part of the GDS Server Push mechanism or by manually copying a certificate into the issuer certificate store found in the /app/pki/issuer/certs directory. During provisioning, all client certificates are auto-approved by UA Edge Translator, but afterwards they need to be manually trusted by copying them from the rejected certificate store to the trusted certificate store, unless of course the certificates were already trusted (for example because they were provided by the GDS Server Push mechanism). These stores can also be found in the /app/pki/ folder.

## Operation

UA Edge Translator can be controlled through the use of just 2 OPC UA methods readily available through the OPC UA server interface built in. The methods are:

* CreateAsset(assetName) - Creates an asset node and an OPC UA File API node below the asset node (which can be used to upload the WoT Thing Description), returning the node ID of the newly created asset node on success.
* DeleteAsset(assetNodeId) - deletes a configured asset.

## Supported Southbound Asset Interfaces

The following southbound asset interfaces (a.k.a. protocol drivers) are supported:

* Modbus TCP
* OPC UA
* Rockwell CIP (Ethernet/IP)
* Beckhoff ADS (TwinCAT)
* LoRaWAN
* Matter
* OCPP (Open Charge Point Protocol) V1.6J
* Siemens S7Comm (experimental)
* Mitsubishi MC Protocol (experimental)
* BACNet (experimental)
* IEC61850 (experimental)
* OCPP V2.1 (experimental). Bidirectional Power Transfer (BPT) for Electric Vehicles based in ISO 15118-20 is in the works!

> **Note**: Since BACNet uses UDP messages, BACNet support is limited to running UA Edge Translator natively or with the --net=host argument within a Docker container!

> **Note**: Network discovery for Rockwell PLCs only works when running UA Edge Translator natively or with the --net=host argument within a Docker container!

> **Note**: The LoRaWAN Network Server is available on port 5000 (not secure) and port 5001 (secure), which needs to be mapped to the Docker host for access. If you need a LoRaWAN Gateway, you can use the open-source [Basic Station](https://github.com/lorabasics/basicstation) together with a [LoRaWAN HAT for Raspberry Pi](https://www.waveshare.com/wiki/SX1302_LoRaWAN_Gateway_HAT).

> **Note**: The OCPP Central System is available on port 19520 (not secure) and on port 19521 (secure), which needs to be mapped to the Docker host for access.

> **Note**: Since Matter uses BluetoothLE and mDNS as the underlying network protocol for commissioning, Matter support is limited to running UA Edge Translator natively or with the --network=host argument as well as with the -v /run/dbus:/run/dbus:ro argument within a Docker container! Also, if you are using the BlueZ stack on Linux, make sure that experimental features are enabled since Matter uses some Bluetooth features that are not enabled by default in this stack.

> **Note**: For testing the Matter asset interface, you will also need to create a Thread network using an OpenThread Border Router (OTBR). An open-source OTBR is available [here](https://openthread.io/guides/border-router) and runs on a Raspberry Pi equipped with a Thread radio USB dongle, the setup instructions are [here](https://github.com/make2explore/Open-Thread-Border-Router-on-RaspberryPi). If you need a Matter commissioning QR-code scanner/decoder, there is an online one [here](https://zxing.org/w/decode.jspx).

Other interfaces can easily be added by implementing the IAsset interface. There is also a tool provided that can convert from an OPC UA nodeset file (with instance variable nodes defined in it), an AutomationML file, a TwinCAT file, or an Asset Admin Shell file, to a WoT Thing Model file.

## Running UA Edge Translator from a Docker environment

The following folders within the Docker container store logs, certificates, secrets, settings and OPC UA nodeset files (used during asset mapping to OPC UA) and should be mapped and persisted (-v argument in Docker command line) to the Docker host to encrypted folders, e.g. protected folders using BitLocker:
* /app/pki
* /app/settings

E.g. -v c:/uaedgetranslator/pki:/app/pki, etc.

Client certificates need to be manually moved from the /pki/rejected/certs folder to the /pki/trusted/certs folder to trust an OPC UA client trying to connect.

## Mandatory Environment Variables

* `OPCUA_USERNAME` - OPC UA username to connect to UA Edge Translator.
* `OPCUA_PASSWORD` - OPC UA password to connect to UA Edge Translator.

## Optional Environment Variables

* `LOG_FILE_PATH` - path to the log file to use. Default is /logs/uaedgetranslator.logfile.txt (in the Docker container).
* `APP_NAME` - OPC UA application name to use. Default is UAEdgeTranslator.
* `UACLURL` - UA Cloud Library URL (e.g. https://uacloudlibrary.opcfoundation.org or https://cloudlib.cesmii.net).
* `UACLUsername` - UA Cloud Library username.
* `UACLPassword` - UA Cloud Library password.
* `OPCUA_CLIENT_USERNAME` - OPC UA client username to connect to an OPC UA asset.
* `OPCUA_CLIENT_PASSWORD` - OPC UA client password to connect to an OPC UA asset.
* `DISABLE_ASSET_CONNECTION_TEST` - Set to `1` to disable the connection test when mapping an asset to OPC UA.
* `IGNORE_PROVISIONING_MODE` - Set to `1` to ignore provisioning mode and allow access to WoT-Connectivity-related OPC UA nodes in the address space.
* `OPC_UA_GDS_ENDPOINT_URL` - The endpoint URL of an OPC UA Global Discovery Server on the network, which will then be used during network discovery.
* `DISABLE_TLS` - Set to `1` to turn off TLS for OCPP and LoRaWAN connections.
