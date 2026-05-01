# UA Edge Translator
An standards-based and containerized industrial connectivity edge application translating from many proprietary protocols to [OPC UA](https://opcfoundation.org/) leveraging the [W3C Web of Things (WoT)](https://www.w3.org/WoT/) thing descriptions via the new [WoT-Connectivity specification](https://reference.opcfoundation.org/WoT/v100/docs/). Data transformation into OPC UA [Companion Specs](https://opcfoundation.org/about/opc-technologies/opc-ua/ua-companion-specifications/) is also supported. Thing Descriptions can be easily edited using the [Eclipse Foundation's edi{TD}or](https://eclipse-editdor.github.io/editdor/), or automatically generated using AI. UA Edge Translator runs on both ARM and X64 architectures, it runs on both Windows and Linux and it runs in both Docker and Kubernetes environments.

## How It Works

UA Edge Translator solves the common "brownfield" use case of connecting disparate industrial assets with many different interfaces and translates their data into an OPC UA information model (ideally to one of the [standardized companion specifications](https://opcfoundation.org/developer-tools/documents/) from the [UA Cloud Library](https://uacloudlibrary.opcfoundation.org/)), enabling processing of the assets' data either on the edge or in the cloud leveraging a normalized, IEC standard (OPC UA) data format. This accelerates Industrial IoT projects and saves cost since the data doesn't need to be normalized in the cloud and makes use of the OT expertise often only found on-premises. For defining a mapping from the proprietary data format to OPC UA, the Web of Things (WoT) Thing Description schema (JSON-LD-based) is used. Additionally, the mechanism to provide the schema to the UA Edge Translator is also leveraging OPC UA. Therefore, for the first time, OPC UA is used for both the control and data plane for industrial connectivity, while previous solutions only used OPC UA for the data plane and a proprietary REST interface for the control plane.

## Installation

UA Edge Translator is available as a pre-built Docker container (supporting both AMD64 and ARM64 CPUs) directly from GitHub and will run on any Docker- or Kubernetes-enabled edge device. See "Packages" in this repo for details.

## Provisioning
UA Edge Translator supports provisioning via GDS Server Push functionality as described in part 12 of the OPC UA specification. Until an issuer certificate is provided in the issuer certificate store of UA Edge Translator, it is in provisioning mode and access to the WoT-Connectivity-related OPC UA nodes in its address space is restricted. An issuer certificate can be provided as part of the GDS Server Push mechanism or by manually copying a certificate into the issuer certificate store found in the /app/pki/issuer/certs directory. During provisioning, all client certificates are auto-approved by UA Edge Translator, but afterwards they need to be manually trusted by copying them from the rejected certificate store to the trusted certificate store, unless of course the certificates were already trusted (for example because they were provided by the GDS Server Push mechanism). These stores can also be found in the /app/pki/ folder.

## Operation

UA Edge Translator can be controlled through the use of just 2 OPC UA methods (and OPC UA file transfer functionality) readily available through the OPC UA server interface built in. The methods are:

* CreateAsset(assetName) - Creates an asset node and an OPC UA File API node below the asset node (which can be used to upload the WoT Thing Description), returning the node ID of the newly created asset node on success.
* DeleteAsset(assetNodeId) - deletes a configured asset.

## Supported Southbound Asset Interfaces (Protocol Drivers)

The following southbound asset interfaces (a.k.a. protocol drivers) are supported:

* Modbus TCP
* Modbus RTU (experimental)
* OPC UA
* OPC DA (a.k.a. OPC Classic)
* HTTP
* Aveva PI (experimental)
* Rockwell CIP (Ethernet/IP)
* Beckhoff ADS (TwinCAT)
* LoRaWAN
* Matter
* OCPP (Open Charge Point Protocol) V1.6J
* OCPP (Open Charge Point Protocol) V2.1 (experimental)
* Siemens S7Comm (experimental)
* Mitsubishi MC Protocol (experimental)
* BACNet (experimental)
* IEC61850 (experimental)

> **Note**: Since BACNet uses UDP messages, BACNet support is limited to running UA Edge Translator natively or with the --net=host argument within a Docker container!

> **Note**: Network discovery for Rockwell PLCs only works when running UA Edge Translator natively or with the --net=host argument within a Docker container!

> **Note**: The LoRaWAN Network Server is available on port 5000 (not secure) and port 5001 (secure), which needs to be mapped to the Docker host for access. If you need a LoRaWAN Gateway, you can use the open-source [Basic Station](https://github.com/lorabasics/basicstation) together with a [LoRaWAN HAT for Raspberry Pi](https://www.waveshare.com/wiki/SX1302_LoRaWAN_Gateway_HAT).

> **Note**: The OCPP Central System is available on port 19520 (not secure) and on port 19521 (secure), which needs to be mapped to the Docker host for access.

> **Note**: Since Matter uses BluetoothLE and mDNS as the underlying network protocol for commissioning, Matter support is limited to running UA Edge Translator natively or with the --network=host argument as well as with the -v /run/dbus:/run/dbus:ro argument within a Docker container! Also, if you are using the BlueZ stack on Linux, make sure that experimental features are enabled since Matter uses some Bluetooth features that are not enabled by default in this stack.

> **Note**: OPC DA (OLE for Process Control Data Access) is a legacy protocol that relies on COM/DCOM and its support is limited to running UA Edge Translator natively on Windows on x86 CPUs and the OPC DA server must be located on the same machine as UA Edge Translator (i.e. no DCOM support).

> **Note**: For testing the Matter asset interface, you will also need to create a Thread network using an OpenThread Border Router (OTBR). An open-source OTBR is available [here](https://openthread.io/guides/border-router) and runs on a Raspberry Pi equipped with a Thread radio USB dongle, the setup instructions are [here](https://github.com/make2explore/Open-Thread-Border-Router-on-RaspberryPi). If you need a Matter commissioning QR-code scanner/decoder, there is an online one [here](https://zxing.org/w/decode.jspx).

> **Note**: The Modbus RTU interface requires access to a serial port on the host system. When running UA Edge Translator in a Docker container, make sure to map the serial port device into the container using the --device argument, e.g. -v /dev/ttyUSB1:/dev/ttyUSB1 and run the container with the --privileged argument.

Other interfaces can easily be added by implementing the IAsset interface (for runtime interaction with the asset) as well as the IProtocolDriver interface (for asset onboarding). 

There is also a tool provided (WoTThingModelGenerator) that can convert from an OPC UA nodeset file (with instance variable nodes defined in it), an AutomationML file, a Beckhoff TwinCAT module class file, a Rockwell Studio 5000 tag CSV export, an Asset Admin Shell file, or a Siemens TIA Portal project file (via TIA Openness) to a WoT Thing Model file. See [Generating WoT Thing Descriptions from PLC Engineering Tools](#generating-wot-thing-descriptions-from-plc-engineering-tools) below for details.

## How to build your own Protocol Driver

UA Edge Translator loads protocol drivers as DLLs from the `/app/drivers` folder at runtime. To build your own protocol driver, create a new .NET10 Class Library project and add a project reference to the UaEdgeTranslator, making sure that only the protocol driver DLL is published:

```
<ItemGroup>
  <ProjectReference Include="..\..\UAServer\UaEdgeTranslator.csproj">
    <Private>true</Private>
      <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
  </ProjectReference>
</ItemGroup>
```

Then implement the IProtocolDriver and IAsset interface and publish your project into the `..\..\UAServer\drivers\<yourdrivername>` folder and restart UA Edge Translator to load your new protocol driver.

## Running UA Edge Translator from a Docker environment

```
# 1) Create a named volume for drivers
docker volume create translator_drivers

# 2) Copy drivers from the driver-pack image into the volume
docker run --rm -v translator_drivers:/out ghcr.io/opcfoundation/ua-edgetranslator-drivers:main /bin/sh -c 'cp -a /drivers/. /out/'

# 3) Run UA Edge Translator with the drivers volume mounted to /app/drivers
docker run -d --name ua-edge-translator -v translator_drivers:/app/drivers -p 4840:4840 ghcr.io/opcfoundation/ua-edgetranslator:main
```

In addition, the following folders within the Docker container store certificates, secrets and settings and should be mapped and persisted (-v argument in Docker command line) to the Docker host to encrypted folders, e.g. protected folders using BitLocker:
* `/app/logs` (log files)
* `/app/pki` (certificates and keys)
* `/app/settings` (WoT Thing Descriptions)
* `/app/nodesets` (OPC UA nodesets for referenced companion specifications)
* `/app/drivers` (protocol driver DLLs, see above)

E.g. -v c:/uaedgetranslator/pki:/app/pki, etc.

Client certificates need to be manually moved from the /pki/rejected/certs folder to the /pki/trusted/certs folder to trust an OPC UA client trying to connect.

## Mandatory Environment Variables

* `OPCUA_USERNAME` - OPC UA username to connect to UA Edge Translator.
* `OPCUA_PASSWORD` - OPC UA password to connect to UA Edge Translator.

## Optional Environment Variables

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

## Developer Quick Start Guide

The following guide will help you get started with adding protocol drivers to UA Edge Translator and onboard your first asset using the httpclient WoT driver. 

This should give you a fast way to get to a state that you can modify with other drivers and WoT files.

1) Publish the HttpClient driver. This will copy the httpclient.dll and its debug file in the "drivers" folder under "UAServer"

2) Copy the WoT File "SimpleHTTPClient.td.jsonld" in the "settings" folder under "UAServer"

3) Load the UAEdgeTranslator project and run it.

4) Connect to the opc server of the UAEdgeTranslator using your favorite OPC UA Client (i.e. UAExpert). The default credentials are "myUsername" and "myPassword". 

You can change these credentials in the launchSettings.json file under "Properties" of the UAEdgeTranslator project:
```
"OPCUA_USERNAME": "myUsername",
"OPCUA_PASSWORD": "myPassword",
```

To test your setup before provisioning the UAEdgeTranslator with the proper certificates you can also set this in the launchSettings.json:
```
"IGNORE_PROVISIONING_MODE": "1"
```

Once connected, you will see the OPC UA address space with a node called "WoTAssetConnectionManagement"

5) Open this node and you will find another node called "SimpleHTTPClient.td"

In this branch you will find a variable "IPAddress" that was defined in the "SimpleHTTPClient.td.jsonld". The variable is read every 60 seconds, although it probably does not change since it just calls a service on the internet determining your external IP address.

For more details on the Web of Things file format and description see https://www.w3.org/TR/wot-thing-description-2.0/

## Generating WoT Thing Descriptions from PLC Engineering Tools

The `WoTThingModelGenerator` tool in this repository converts data exported from common PLC engineering tools into WoT Thing Model files (`*.tm.jsonld`) that UA Edge Translator can consume after the placeholders (e.g. `{{address}}`, `{{port}}`, `{{name}}`) have been filled in.

It currently supports input from:

| Vendor / Source | Input file | Produced binding |
|---|---|---|
| Beckhoff TwinCAT | `*.tmc` (TwinCAT Module Class) | ADS / `GenericForm` |
| Rockwell Studio 5000 / RSLogix 5000 | `*.csv` (tag / UDT export) | EtherNet/IP (`EIPForm`) |
| Generic Modbus point list (Azure IoT format) | `*.csv` | Modbus TCP (`ModbusForm`) |
| Siemens TIA Portal V18..V21 | `*.ap18` .. `*.ap21` (project file) | S7Comm (`S7Form`) |
| OPC UA | `*.NodeSet2.xml` | OPC UA (`GenericForm`) |
| AutomationML | `*.aml` | `GenericForm` |
| Asset Administration Shell — Asset Interface Description | `*.aas.json` | Modbus or `GenericForm` |

The tool scans its **current working directory**, processes every recognised file it finds, and writes a `<inputName>.tm.jsonld` next to it (Siemens projects emit one file per PLC: `<projectName>_<plcName>.tm.jsonld`).

### Building the tool

`WoTThingModelGenerator` targets `net8.0-windows` / x64 because the Siemens TIA Openness API is x64‑only. The other importers also run on the same build.

```powershell
cd UA-EdgeTranslator
dotnet build WoTThingModelGenerator\WoTThingModelGenerator.csproj -c Release /p:Platform=x64
```

Run it from any directory containing input files:

```powershell
cd <folder containing your engineering exports>
& "<repo>\WoTThingModelGenerator\bin\x64\Release\net8.0-windows\WoTThingModelGenerator.exe"
```

Each generated `*.tm.jsonld` can then be uploaded to UA Edge Translator via the OPC UA File API exposed under the asset node, or copied into `/app/settings` for it to be picked up at start‑up (after replacing the `{{...}}` placeholders with the real values for your asset).

### Beckhoff (TwinCAT) — exporting a `.tmc` file

1. Open the project in **TwinCAT XAE / Visual Studio**.
2. In the Solution Explorer, expand the PLC project node.
3. Right‑click the PLC project → **Properties** → **TMC File** (or **Build → TwinCAT Build → Build TMC File**) — the `*.tmc` is regenerated on every PLC build and lives next to the `*.tsproj` or under `<project>\_Boot\TwinCAT RT (x64)\Plc\`.
4. Copy that `*.tmc` next to `WoTThingModelGenerator.exe` and run the tool.
5. The tool emits `<plcName>.tm.jsonld` with one Property per published symbol (those exposed in the ADS data area).

> Only symbols that appear in a TwinCAT data area (`<DataArea>`) are exported. Variables you want to read over ADS must therefore have the `{attribute 'TcLinkTo'}` / publish flag set in TwinCAT.

### Rockwell (Studio 5000 / RSLogix 5000) — exporting a tag CSV

1. Open the controller project in **Studio 5000 Logix Designer** (or RSLogix 5000).
2. Open the **Tags** editor for the controller / program scope you want to expose.
3. Use **Tools → Export → Tags and Logic Comments…** and choose **CSV** as the output format. Make sure both **Tags** and **Comments** are included — the tool reads `COMMENT` rows to infer UDT field names.
4. Drop the resulting `*.csv` next to `WoTThingModelGenerator.exe` and run it.
5. The tool emits `<csvName>.tm.jsonld` containing one Property per primitive tag and one structured Property per UDT‑typed tag (the field offsets inside the UDT are resolved at runtime by the Rockwell driver).

> The Rockwell driver also implements `BrowseAndGenerateTD`, so you can alternatively let UA Edge Translator browse a connected controller live (no CSV needed) when the controller is reachable on the network.

### Siemens (TIA Portal V18..V21) — using the project file directly

The Siemens importer drives the **TIA Portal Openness** API to walk the project's `PlcSoftware → BlockGroup → DataBlock` hierarchy and emit one Property per leaf interface member of every standard‑access (non‑optimized) data block, including byte and bit offsets.

#### Prerequisites (on the machine that runs the tool)

1. **TIA Portal V18, V19, V20 or V21** installed locally. The project must be openable in that TIA version (older STEP 7 Classic projects must be migrated into TIA first).
2. The current Windows user must be a member of the local **`Siemens TIA Openness`** group. Add the user (e.g. via `lusrmgr.msc`) and sign out / in.
3. In TIA Portal, on every FB / DB you want to read:
   - Properties → **Attributes** → uncheck **"Optimized block access"** — without this there are no stable byte offsets and S7Comm classic cannot address individual variables. Optimized blocks are skipped by the importer with a warning.
4. On the CPU itself:
   - Properties → **Protection & Security** → **Connection mechanisms** → enable **"Permit access with PUT/GET communication from remote partner"** (this is a runtime requirement for the S7 driver, not for the import).

#### Build configuration

By default the project file references TIA V21 at:

```
C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\Siemens.Engineering.dll
```

If you have a different version installed, override the path on the command line:

```powershell
dotnet build WoTThingModelGenerator\WoTThingModelGenerator.csproj `
  -c Release /p:Platform=x64 `
  /p:SiemensTIAPortalPath="C:\Program Files\Siemens\Automation\Portal V20"
```

The Openness assemblies are referenced from the local TIA install with `<Private>false</Private>` and **never copied** into the output (Siemens forbids redistribution). At runtime the tool resolves them from the same install path; override with the `SIEMENS_TIA_PATH` environment variable if needed.

#### Running it

1. Copy your TIA project file (`MyProject.ap21`) — the project root file, not its enclosing folder — next to `WoTThingModelGenerator.exe`.
2. Run the tool:

   ```powershell
   cd WoTThingModelGenerator\bin\x64\Release\net8.0-windows
   .\WoTThingModelGenerator.exe
   ```

3. For every PLC in the project, the tool emits `<projectName>_<plcName>.tm.jsonld` containing one Property per leaf data block member, addressed by `S7DBNumber`, `S7Start`, `S7Pos`, `S7Size` and `S7MaxLen`. The PLC's IPv4 address (read from the PROFINET interface) is baked into the `base` field as `s7://<ip>:0:1`.

> Files with extensions `.ap18`, `.ap19`, `.ap20` and `.ap21` are all recognised; pick the one that matches your installed TIA version.

## Generating a Thing Description for a Fixed‑Function Asset

Many industrial assets — power meters, drives, gateways, sensors, scanners, RFID readers, soft starters, IO‑Link masters, weighing terminals, etc. — are *fixed‑function*: their data model is hard‑wired by the vendor and shipped as a Modbus / EtherNet/IP / S7 / HTTP register or object map in the user manual. There is no engineering project to export, so the two practical paths to a Thing Description are:

### Option A (preferred): get the Thing Description directly from the vendor

Always check whether the vendor already publishes a machine‑readable description before generating one yourself. In order of preference:

1. A WoT Thing Description (`*.td.jsonld` / `*.tm.jsonld`) on the product page or GitHub.
2. An **Asset Administration Shell** (AAS) submodel **Asset Interface Description (AID)** package (`*.aas.json`, `*.aasx`). UA Edge Translator's `WoTThingModelGenerator` can convert AID JSON files directly to WoT Thing Models — see the table above.
3. An **OPC UA companion specification NodeSet2** for the device class (`*.NodeSet2.xml`), e.g. from the [UA Cloud Library](https://uacloudlibrary.opcfoundation.org/). Also supported by `WoTThingModelGenerator`.
4. A vendor‑provided **register / point list** (CSV, XLSX, EDS for EtherNet/IP, GSDML for PROFINET). For a generic Modbus point list in the Azure IoT format, the tool already imports it directly. For other CSV layouts, a small adapter in `WoTThingModelGenerator` is usually a few minutes' work.

A vendor‑provided file is authoritative, has correct register addresses and scaling factors, and removes the risk of hallucinated fields. It also tends to be re‑usable across every customer of that device.

### Option B: generate the Thing Description from the user manual using an LLM

When no machine‑readable description is available, the asset's **user / reference manual PDF** almost always contains the full register or object map — Modbus tables, EtherNet/IP assembly definitions, OPC UA NodeIds, HTTP endpoints — together with data types, units and scaling factors. A modern multimodal LLM (ChatGPT, Microsoft Copilot, Claude, Gemini, etc.) can read the PDF and emit a WoT Thing Model in one step.

Recommended workflow:

1. Download the official **user manual / reference manual PDF** for your specific firmware revision from the vendor's website. Manuals labelled "Modbus reference", "Communication manual", "EDS file documentation" or similar are best — they contain the register tables you need.
2. Open a chat session with an LLM that supports file upload (e.g. ChatGPT, Microsoft 365 Copilot, Claude). Upload the PDF as an attachment.
3. Send the prompt below, replacing the angle‑bracketed values. Treat the prompt as a starting point — for unusual devices you may need to clarify which register table the LLM should focus on (some manuals contain several).

   > You are an industrial connectivity engineer. From the attached user manual for **\<vendor> \<product> \<firmware/rev>** generate a single WoT 1.1 Thing Model JSON document for use with the OPC Foundation UA Edge Translator.
   >
   > Use the `<protocol>` binding (one of: `modbus+tcp`, `modbus`, `eip`, `s7`, `http`, `opc.tcp`).
   >
   > Requirements:
   > - Output **only** the JSON, no prose.
   > - `@context` must be `["https://www.w3.org/2022/wot/td/v1.1"]`.
   > - `@type` must be `["tm:ThingModel"]`.
   > - `securityDefinitions` must be `{ "nosec_sc": { "scheme": "nosec" } }` and `security` must be `["nosec_sc"]`.
   > - `name` = `"{{name}}"`, `base` = `"<protocol>://{{address}}:{{port}}"`, `title` = product name.
   > - For each variable in the manual's register / object table, emit one entry under `properties` with: `type` (`number`/`integer`/`boolean`/`string`), `readOnly`, `observable: true`, and one form whose binding fields match the protocol.
   > - For Modbus use `ModbusForm` fields: `href` (e.g. `"40001?quantity=2"`), `op: ["readproperty","observeproperty"]`, `modv:type` (`xsd:float`/`xsd:integer`/`xsd:boolean`/`xsd:string`), `modv:entity` (`HoldingRegister`/`InputRegister`), `modv:pollingTime` (ms), `modv:mostSignificantByte`, `modv:mostSignificantWord`, and `modv:multiplier` if the manual specifies a scaling factor.
   > - For EtherNet/IP use `EIPForm` fields: `href` (tag name), `op`, `type` (`xsd:REAL`, `xsd:DINT`, …), `pollingTime`.
   > - For Siemens S7 use `S7Form` fields: `href`, `op`, `s7:target` (`DB`/`MB`/`EB`/`AB`), `s7:dbnumber`, `s7:start`, `s7:pos`, `s7:size`, `s7:maxlen` (for STRING), `type`, `pollingTime`.
   > - Do **not** invent registers that are not in the manual. If a value in the manual is unclear, omit it rather than guessing.
   > - Use the original variable / register names from the manual as property keys, replacing spaces with underscores.

4. Save the LLM's response as `<assetName>.tm.jsonld` and **review it manually** against the manual:
   - Spot‑check a handful of register addresses, data types, byte/word order and scaling factors.
   - Confirm that read‑only registers are flagged `readOnly: true`.
   - Trim out anything you don't actually need to expose.
5. Replace the `{{name}}`, `{{address}}` and `{{port}}` placeholders with the real values for your asset (Eclipse's Edi{td}or WoT-file editor does this automatically for you).
6. Upload the file to UA Edge Translator using the OPC UA File API exposed under the asset node (or drop it into `/app/settings`) and let the matching protocol driver onboard the asset.

> **Important**: an LLM can misread tables, especially in scanned PDFs, multi‑column layouts or manuals with several variants of the same register map. Always validate the produced Thing Description against the manual and against a live test read from the asset before deploying it to production.

## Threat Model and Security Considerations

UA Edge Translator uses a zero trust security model and implements the following security features:
* UA Edge Translator runs within a Docker container in a restricted network environment and with limited permissions to the host system.
* UA Edge Translator comes with extensive logging to the console and to disk, but does not log any sensitive information such as passwords or private keys.
* OPC UA SHA256 sign & encrypt server security policy and username/passowrd user authentication for secure communication between clients and the UA Edge Translator OPC UA server as well as between the UA Edge Translator OPC UA client protocol driver and OPC UA assets.
* OPC UA GDS Server Push provisioning mechanism for secure provisioning of the UA Edge Translator with issuer certificates and client certificates.
* Secure Websockets using TLS for secure communication with LoRaWAN Network Server and OCPP Central System.
* Matter Fabric persistency of certificates and keys in the /app/pki folder for secure communication with Matter assets.
* Protocol drivers are loaded as DLLs at runtime and drivers considered insecure can be easily turned off by removing the respective DLL from the "drivers" folder.

> **Note**: If the /app/pki folder is mapped to a folder on the Docker host, make sure to protect this folder since it contains private keys and certificates. For example, you can use BitLocker to encrypt the folder on the Docker host.

### STRIDE Analysis of OPC UA server interface
* Spoofing: Mitigated by OPC UA username/password authentication and client certificate authentication.
* Tampering: Mitigated by OPC UA message signing and encryption.
* Repudiation: Mitigated by OPC UA message signing and encryption, as well as append-only logging.
* Information Disclosure: Mitigated by OPC UA message encryption.
* Denial of Service: Mitigated by OPC UA secure channels and session management with maximums set for sessions, subscriptions, monitored items and message size limits.
* Elevation of Privilege: Mitigated by OPC UA user authentication as well as a provisioning mode preventing read/write access to variables before GDS Push is carried out.

### STRIDE Analysis of LoRaWAN and OCPP Secure Websocket server interfaces
* Spoofing: Mitigated by TLS client certificate authentication for the LoRaWAN Network and communication for OCPP Central System.
* Tampering: Mitigated by TLS encryption for the LoRaWAN Network and OCPP Central System.
* Repudiation: Mitigated by TLS encryption for the LoRaWAN Network and OCPP Central System, as well as append-only logging.
* Information Disclosure: Mitigated by TLS encryption for the LoRaWAN Network and OCPP Central System.
* Denial of Service: Mitigated by secure Websocket communication and retry/backoff mechanisms in the code.
* Elevation of Privilege: Mitigated by TLS client certificate authentication for the LoRaWAN Network and secure Websocket communication for OCPP.
