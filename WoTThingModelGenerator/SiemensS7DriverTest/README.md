# Siemens S7 Driver Test Project (TIA Portal V21, S7-1500)

This sample is a self-contained test rig for the
[`SiemensProtocolDriver`](../../ProtocolDrivers/Siemens/SiemensProtocolDriver.cs)
and the
[`SiemensTIAImporter`](../../WoTThingModelGenerator/Siemens.cs) in this
solution. It exercises **every `TypeString` value** the driver supports
(`Boolean`, `Byte`, `Short`, `Integer`, `Long`, `UnsignedLong`, `Float`,
`Double`, `String`, `DateTime`, `Duration`) and **every `S7Target` value**
(`DataBlock`, `Merker`, `IPIProcessInput`, `IPUProcessInput`, `Timer`,
`Counter`).

All variables change continuously, driven by four pattern generators:

| Pattern    | Period | Variables                                                                                         |
|-----------:|:------:|----------------------------------------------------------------------------------------------------|
| Sine wave  | 4 s    | `Sine_Real`, `Sine_LReal`, `Sine_Int`, `Sine_SInt`, `Sine_DInt`, `Sine_DWord`, `Sine_LInt`, `Sine_TIME`, `Sine_IntArray[0..7]`, `Sine_RealArray[0..3]` |
| Sawtooth   | 5 s    | `Saw_Byte`, `Saw_USInt`, `Saw_Word`, `Saw_UInt`, `Saw_UDInt`, `Saw_ULInt`, `Saw_LWord`, `Saw_LTIME`, `Saw_S5TIME` |
| Square     | 2 s    | `Pulse_Bool`, `Pulse_Char`, `Pulse_WChar`                                                          |
| Random     | every cycle | `Random_Real` (xorshift32 PRNG, state in `PRNG_State`)                                        |
| Rotation   | 1.6 s  | `Bits_Array[0..15]` (single `TRUE` bit rotates through the array)                                  |
| Live clock | every cycle | `Now_DTL`, `Now_DT`, `Now_DATE`, `Now_TOD`, `Now_LTOD`, `Now_LDT`                             |

`Pattern_String` carries the live `Tick=<ms>` value, `Pattern_WString` carries
`"S7DriverTest <tickMs>"`. A subset is mirrored to `%MB100 / %MW102 / %MD104 /
%M120.0` and `%QB0 / %QW2 / %QD4` so the M and Q areas are also exercised.

## 1. Files in this folder
SiemensS7DriverTest/
├── README.md            ← this document
├── AllTypesDB.scl       ← DB1 declaration (standard access)
├── Main_OB1.scl         ← cyclic OB driving all patterns
└── Optional_S5Timer.scl ← optional, only needed to test S7Target.Timer / S7Target.Counter

> TIA Portal projects (`*.ap21`) are a binary, proprietary format that cannot
> be checked into a Git repository as a single file. The recipe below uses
> TIA's built-in **External source files → Generate blocks from source**
> mechanism to recreate the program 1:1 in a fresh project.

## 2. Type coverage matrix
The `AllTypesDB` data block declares one variable per Siemens elementary type
that the importer (`WoTThingModelGenerator/Siemens.cs::MapType`) and the
runtime (`ProtocolDrivers/Siemens/SiemensAsset.cs`) understand. Every
variable changes value continuously.

| S7 type                | DB member          | Pattern    | Importer maps to             | Runtime returns           |
|------------------------|--------------------|------------|------------------------------|---------------------------|
| `Bool`                 | `Pulse_Bool`       | square     | `xsd:boolean`                | `bool`                    |
| `Byte`                 | `Saw_Byte`         | sawtooth   | `xsd:byte` + `s7:s7type=BYTE`  | `byte`                  |
| `SInt`                 | `Sine_SInt`        | sine       | `xsd:byte` + `s7:s7type=SINT`  | `sbyte`                 |
| `USInt`                | `Saw_USInt`        | sawtooth   | `xsd:byte` + `s7:s7type=USINT` | `byte`                  |
| `Char`                 | `Pulse_Char`       | square     | `xsd:string` + `s7:s7type=CHAR`| `string` (1 char)       |
| `Word`                 | `Saw_Word`         | sawtooth   | `xsd:short` + `s7:s7type=WORD` | `ushort`                |
| `Int`                  | `Sine_Int`         | sine       | `xsd:short` + `s7:s7type=INT`  | `short`                 |
| `UInt`                 | `Saw_UInt`         | sawtooth   | `xsd:short` + `s7:s7type=UINT` | `ushort`                |
| `WChar`                | `Pulse_WChar`      | square     | `xsd:string` + `s7:s7type=WCHAR` | `string` (1 char)     |
| `DWord`                | `Sine_DWord`       | sine       | `xsd:integer` + `s7:s7type=DWORD` | `uint`                |
| `DInt`                 | `Sine_DInt`        | sine       | `xsd:integer` + `s7:s7type=DINT`  | `int`                 |
| `UDInt`                | `Saw_UDInt`        | sawtooth   | `xsd:integer` + `s7:s7type=UDINT` | `uint`                |
| `Real`                 | `Sine_Real`, `Random_Real` | sine / random | `xsd:float` + `s7:s7type=REAL` | `float`         |
| `LReal`                | `Sine_LReal`       | sine       | `xsd:double` + `s7:s7type=LREAL`  | `double`              |
| `LInt`                 | `Sine_LInt`        | sine       | `xsd:long` + `s7:s7type=LINT`     | `long`                |
| `ULInt`                | `Saw_ULInt`        | sawtooth   | `xsd:unsignedLong` + `s7:s7type=ULINT` | `ulong`          |
| `LWord`                | `Saw_LWord`        | sawtooth   | `xsd:unsignedLong` + `s7:s7type=LWORD` | `ulong`          |
| `String[80]`           | `Pattern_String`   | live tick  | `xsd:string` + `s7:s7type=STRING` | `string`              |
| `WString[64]`          | `Pattern_WString`  | live tick  | `xsd:string` + `s7:s7type=WSTRING` | `string` (UTF-16 BE) |
| `DTL`                  | `Now_DTL`          | live clock | `xsd:dateTime` + `s7:s7type=DTL`  | ISO-8601 string       |
| `Date_And_Time` (`DT`) | `Now_DT`           | live clock | `xsd:dateTime` + `s7:s7type=DT`   | ISO-8601 string (BCD) |
| `Date`                 | `Now_DATE`         | live clock | `xsd:dateTime` + `s7:s7type=DATE` | `yyyy-MM-dd`          |
| `Time_Of_Day` (`TOD`)  | `Now_TOD`          | live clock | `xsd:dateTime` + `s7:s7type=TOD`  | `HH:mm:ss.fff`        |
| `LTime_Of_Day` (`LTOD`)| `Now_LTOD`         | live clock | `xsd:dateTime` + `s7:s7type=LTOD` | `HH:mm:ss.fffffff`    |
| `LDT`                  | `Now_LDT`          | live clock | `xsd:dateTime` + `s7:s7type=LDT`  | ISO-8601 UTC          |
| `Time`                 | `Sine_TIME`        | sine       | `xsd:duration` + `s7:s7type=TIME` | `TimeSpan` (ms)       |
| `LTime`                | `Saw_LTIME`        | sawtooth   | `xsd:duration` + `s7:s7type=LTIME` | `TimeSpan` (ns)      |
| `S5Time`               | `Saw_S5TIME`       | sawtooth   | `xsd:duration` + `s7:s7type=S5TIME` | `TimeSpan` (BCD)    |
| `Array[0..15] of Bool` | `Bits_Array`       | rotation   | one `xsd:boolean` per element with bit-packed `s7:start`/`s7:pos` | `bool`     |
| `Array[0..7] of Int`   | `Sine_IntArray`    | sine (phase shift) | one `xsd:short` per element                                         | `short`     |
| `Array[0..3] of Real`  | `Sine_RealArray`   | sine (varying period) | one `xsd:float` per element                                      | `float`     |

## 3. Create the TIA Portal V21 project
Prerequisites:
- TIA Portal V21 (Engineering or Professional) installed.
- Optional: **S7-PLCSIM V21** for software-only testing (no real CPU needed).

Steps in TIA Portal:

1. **Project → New** → name it `S7DriverTest_V21`.
2. **Add new device → Controllers → SIMATIC S7-1500 → CPU 1516-3 PN/DP**
   (or any S7-1500 CPU with firmware ≥ V2.0; the SCL is independent of the
   article number, but `LDT` / `LTime_Of_Day` need V2.0+).
3. **Device view** → click the CPU → **Properties**:
   - **PROFINET interface [X1] → Ethernet addresses** →
     `IP address = 192.168.0.1`, `Subnet mask = 255.255.255.0`.
     Add the interface to a new subnet `PN/IE_1`.
   - **Protection & Security → Connection mechanisms** →
     **enable** "Permit access with PUT/GET communication from remote partner".
     Without this the S7Comm classic driver in this repo cannot read or write.
   - **Protection & Security → Access level** → "Full access (no protection)"
     for testing (raise this for production).
4. **Compile** the device (Ctrl+B) once to make sure the empty CPU builds.

## 4. Import the SCL sources
For each `.scl` file under `SourceFiles/`:

1. Project tree → expand the PLC → **External source files** →
   double-click **Add new external file** → select the `.scl`.
2. Right-click the imported file → **Generate blocks from source**.
3. TIA creates the corresponding block under **Program blocks**:
   - `AllTypesDB.scl`  → `DB1 "AllTypesDB"` (standard access, see warning).
   - `Main_OB1.scl`    → `OB1 "Main"` (replaces the empty default OB1).
   - `Optional_S5Timer.scl` (only if you want to exercise the T/C areas) →
     `FC1 "S5Timer_FC"`. Add `"S5Timer_FC"();` to OB1's BEGIN section.

> ⚠️ After import, **right-click `DB1 "AllTypesDB"` → Properties → Attributes**
> and verify that **"Optimized block access" is unchecked**. The
> `{ S7_Optimized_Access := 'FALSE' }` pragma in the SCL enforces this, but
> TIA sometimes silently drops the attribute on first generation. Without
> standard (non-optimized) layout, S7Comm classic cannot address the
> individual variables and the importer in `WoTThingModelGenerator/Siemens.cs`
> will skip the DB (it logs `"layout is Optimized, not Standard"`).

## 5. Download / simulate
- **Real CPU**: connect, set the PG/PC interface, **Download to device**.
- **PLCSIM V21**: in the toolbar click **Start simulation**, then download
  the same way. Use the **SIM table** to drive `%I0.0..%I0.7` / `%IB0` if
  you want non-zero values for `S7Target.IPIProcessInput`.

After the CPU goes to RUN, every member of `DB1 "AllTypesDB"` changes
continuously per the matrix in §2.

## 6. Generate the Thing Model with `WoTThingModelGenerator`
Same recipe as the repo's main README. With the changes in this branch the
emitted `*.tm.jsonld` now contains:

- one Property per **leaf primitive** of `AllTypesDB`,
- one Property per **array element** (with `s7:start` adjusted per element
  and bit-packed `s7:pos` for `Array of Bool`),
- an `s7:s7type` field on every `S7Form` so the runtime decodes each value
  with its real Siemens semantics (BCD for `DT`, UTF-16 BE for `WSTRING`,
  signed nanoseconds for `LTIME`, etc.).

Build & run:
cd <repo-root> dotnet build WoTThingModelGenerator\WoTThingModelGenerator.csproj ` -c Release /p:Platform=x64

### 5.3 Run the importer against the test project
Save your TIA project somewhere accessible, e.g. C:\TIAProjects\S7DriverTest_V21\
The "project file" is the *.ap21 at the project root, NOT the enclosing folder.
Stage the project file next to the generator binary (the tool processes the
CWD it was launched from):

$tool   = "<repo-root>\WoTThingModelGenerator\bin\x64\Release\net8.0-windows" $apFile = "C:\TIAProjects\S7DriverTest_V21\S7DriverTest_V21.ap21"
Copy-Item $apFile $tool
cd $tool .\WoTThingModelGenerator.exe

For every PLC found in the project, the tool writes
`<projectName>_<plcName>.tm.jsonld` next to the executable, e.g.
S7DriverTest_V21_PLC_1.tm.jsonld

### 5.4 What you should see in the output
Each leaf member of `AllTypesDB` becomes a `Property` whose `forms[0]` is
an `S7Form` with the correct `s7:dbnumber`, `s7:start`, `s7:pos`, `s7:size`
and `type`. A representative excerpt — exact byte/bit offsets are decided
by TIA at compile time and may differ slightly between firmware versions:
{ "@context": [ "https://www.w3.org/2022/wot/td/v1.1" ], "id": "urn:PLC_1", "@type": [ "tm:ThingModel" ], "title": "PLC_1", "base": "s7://192.168.0.1:0:1", "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } }, "security": [ "nosec_sc" ], "properties": { "Pulse_Bool": { "type": "boolean", "observable": true, "readOnly": false, "forms": [{ "href": "DB1?0", "op": [ "readproperty", "observeproperty" ], "pollingTime": 1000, "s7:target": "DB", "s7:dbnumber": 1, "s7:start": 0, "s7:pos": 0, "s7:size": 1, "type": "xsd:boolean" }] }, "Sine_Real": { "type": "number", "observable": true, "readOnly": false, "forms": [{ "href": "DB1?<offset>", "op": [ "readproperty", "observeproperty" ], "pollingTime": 1000, "s7:target": "DB", "s7:dbnumber": 1, "s7:start": <offset>, "s7:pos": 0, "s7:size": 4, "type": "xsd:float" }] }, "Pattern_String": { "type": "string", "observable": true, "readOnly": false, "forms": [{ "href": "DB1?<offset>", "op": [ "readproperty", "observeproperty" ], "pollingTime": 1000, "s7:target": "DB", "s7:dbnumber": 1, "s7:start": <offset>, "s7:pos": 0, "s7:size": 82, "s7:maxlen": 80, "type": "xsd:string" }] } } }

The console log of the tool also prints, for every member it emitted:
PLC 'PLC_1' @ 192.168.0.1 (rack 0, slot 1) DB 'AllTypesDB' (#1) AllTypesDB.Pulse_Bool: Bool @ DB1.0.0 (1 B) AllTypesDB.Saw_Byte:   Byte @ DB1.<x>.0 (1 B) AllTypesDB.Sine_Int:   Int  @ DB1.<x>.0 (2 B) AllTypesDB.Sine_Real:  Real @ DB1.<x>.0 (4 B) AllTypesDB.Sine_LReal: LReal @ DB1.<x>.0 (8 B) AllTypesDB.Pattern_String:  String[80]  @ DB1.<x>.0 (82 B) AllTypesDB.Pattern_WString: WString[64] @ DB1.<x>.0 (132 B) AllTypesDB.Now_DTL:    DTL  @ DB1.<x>.0 (12 B) ...

### 5.5 Adding the M / I / Q / T / C placeholders manually
The Openness importer only walks **DataBlocks**. Tags in the M, I, Q, T or C
areas have to be added by hand to the generated `*.tm.jsonld`. Either:

- run UA Edge Translator's `BrowseAndGenerateTD` against the live CPU — it
  emits one **sample placeholder** per non-DB area
  (`SiemensProtocolDriver.BrowseAndGenerateTD`, lines 88–92), or
- copy the snippets below into the generated TM (these match the `%M` / `%Q`
  addresses written by `Main_OB1`):
  "MB100_Saw_Byte": { "type": "integer", "observable": true, "readOnly": false, "forms": [{ "href": "MB100", "op": [ "readproperty", "observeproperty" ], "pollingTime": 1000, "s7:target": "MB", "s7:start": 100, "s7:pos": 0, "s7:size": 1, "type": "xsd:byte" }] }, "MW102_Saw_Word": { "type": "integer", "observable": true, "readOnly": false, "forms": [{ "href": "MW102", "op": [ "readproperty", "observeproperty" ], "pollingTime": 1000, "s7:target": "MB", "s7:start": 102, "s7:pos": 0, "s7:size": 2, "type": "xsd:short" }] }, "MD104_Sine_DWord": { "type": "integer", "observable": true, "readOnly": false, "forms": [{ "href": "MD104", "op": [ "readproperty", "observeproperty" ], "pollingTime": 1000, "s7:target": "MB", "s7:start": 104, "s7:pos": 0, "s7:size": 4, "type": "xsd:integer" }] }, "M120_0_Pulse_Bool": { "type": "boolean", "observable": true, "readOnly": false, "forms": [{ "href": "M120.0", "op": [ "readproperty", "observeproperty" ], "pollingTime": 1000, "s7:target": "MB", "s7:start": 120, "s7:pos": 0, "s7:size": 1, "type": "xsd:boolean" }] }, "QB0_Saw_Byte": { "type": "integer", "observable": true, "readOnly": false, "forms": [{ "href": "QB0", "op": [ "readproperty", "observeproperty" ], "pollingTime": 1000, "s7:target": "AB", "s7:start": 0, "s7:pos": 0, "s7:size": 1, "type": "xsd:byte" }] }

The generator writes `S7DriverTest_V21_PLC_1.tm.jsonld` next to the executable.

### 6.1 Spot-check a few new entries
  "Sine_LInt": { "type": "integer", "observable": true, "readOnly": false, "forms": [{ "href": "DB1?<offset>", "op": [ "readproperty", "observeproperty" ], "pollingTime": 1000, "s7:target": "DB", "s7:dbnumber": 1, "s7:start": <offset>, "s7:pos": 0, "s7:size": 8, "s7:s7type": "LINT", "type": "xsd:long" }] }, "Now_DT": { "type": "string", "observable": true, "readOnly": false, "forms": [{ "href": "DB1?<offset>", "op": [ "readproperty", "observeproperty" ], "pollingTime": 1000, "s7:target": "DB", "s7:dbnumber": 1, "s7:start": <offset>, "s7:pos": 0, "s7:size": 8, "s7:s7type": "DT", "type": "xsd:dateTime" }] }, "Bits_Array_3": { "type": "boolean", "observable": true, "readOnly": false, "forms": [{ "href": "DB1?<base>", "op": [ "readproperty", "observeproperty" ], "pollingTime": 1000, "s7:target": "DB", "s7:dbnumber": 1, "s7:start": <base>, "s7:pos": 3, "s7:size": 1, "s7:s7type": "BOOL", "type": "xsd:boolean" }] }, "Bits_Array_8": { "type": "boolean", "observable": true, "readOnly": false, "forms": [{ "href": "DB1?<base+1>", "op": [ "readproperty", "observeproperty" ], "pollingTime": 1000, "s7:target": "DB", "s7:dbnumber": 1, "s7:start": <base+1>, "s7:pos": 0, "s7:size": 1, "s7:s7type": "BOOL", "type": "xsd:boolean" }] }

Note how `Bits_Array_8` already crossed the byte boundary (the importer's
`EmitArray` packs `Bool` arrays bit-by-bit, advancing the byte offset every
8 elements).

### 6.2 Testing the Write path
The Openness importer emits `op: [ "readproperty", "observeproperty" ]` only.
To exercise `SiemensAsset.Write` (lines 115–151 of `SiemensAsset.cs`), add
`"writeproperty"` to the `op` array of any property whose target you want to
write — for example `Pulse_Bool` to drive a single coil, or `Saw_Word` to
overwrite the sawtooth value (the OB will overwrite it again on the next
cycle, which is exactly the round-trip you want for a test).

## 7. Wire the Thing Model into UA Edge Translator
1. Replace the `{{name}}` and `{{address}}` placeholders the importer
   leaves behind (the `id` and `base` fields). For this sample:
   - `"name": "S7DriverTest"`
   - `"base": "s7://192.168.0.1:0:1"`  (already filled in if Openness could
     read the IP from the PROFINET interface)
2. Either upload the resulting `*.tm.jsonld` via the **OPC UA File API**
   exposed under the asset node (after calling the `CreateAsset` method),
   or copy it into `/app/settings` and restart UA Edge Translator.
3. Connect with any OPC UA client; you should see one OPC UA variable per
   property, with the values changing according to the four pattern
   generators above.

## 8. Troubleshooting checklist
| Symptom                                                  | Likely cause / fix                                                                 |
|----------------------------------------------------------|-------------------------------------------------------------------------------------|
| `S7 read failed … Address out of range`                  | `Optimized block access` is still on for `AllTypesDB`. Toggle it off and re-download. |
| `S7 read failed … Connection error`                      | PUT/GET not enabled. Re-check CPU → Protection & Security → Connection mechanisms.  |
| Importer logs `layout is Optimized, not Standard`        | Same as above — the DB must be standard-access.                                     |
| Importer logs `No accessible (standard-access) DBs found`| The project was opened correctly but no eligible DB exists. Check the `.ap21` path. |
| Openness raises `RequestPasswordDelegate not set`        | The TIA project is password-protected; remove the protection or extend `Import`.    |
| `Could not load Siemens.Engineering`                     | TIA V21 is not at the default path. Set `SIEMENS_TIA_PATH` or pass `/p:SiemensTIAPortalPath=...`. |
| All M / Q / T / C properties report `0`                  | Expected — they are placeholders. Add the 