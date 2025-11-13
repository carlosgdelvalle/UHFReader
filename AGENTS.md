# UHF Prime Reader – Protocol notes (field‑verified)

This summarizes what we verified against the Ethernet UHF reader on 192.168.1.200. Source docs live in `UHF Prime Reader user manual-EN.docx` and `API/*` in this folder.

## Connectivity
- IP: 192.168.1.200
- Default TCP server port: 2022 (verified open)
- We talked to the reader via a plain TCP socket. No TLS.

## Frame format (request/response)
- Request: `HEAD (0xCF) | ADDR | CMD_hi | CMD_lo | LEN | Data[LEN] | CRC_hi | CRC_lo`
- Response: `HEAD (0xCF) | ADDR | CMD_hi | CMD_lo | LEN | STATUS | Data[LEN-1] | CRC_hi | CRC_lo`
- ADDR: 0x00–0xFE, broadcast 0xFF
- LEN: number of bytes in the Data area (for responses, LEN includes the 1‑byte STATUS + Data)
- CRC: CRC‑16/IBM (init 0xFFFF, poly 0x8408, bit‑reflected), output big‑endian (high byte first)

Python CRC reference used:
```python
def crc16_ibm(data: bytes):
    crc = 0xFFFF
    for b in data:
        crc ^= b
        for _ in range(8):
            crc = (crc >> 1) ^ 0x8408 if (crc & 1) else (crc >> 1)
    return crc & 0xFFFF
```

## Commands we used (and confirmed)
- Stop inventory (continuous read): CMD 0x0002 (RFM_INVENTORYISO_STOP)
  - TX (hex): `cf ff 00 02 00 e7 61`
  - RX (hex): `cf 00 00 02 01 00 9e ea`  → STATUS=0x00 (success)

- Read all parameters: CMD 0x0072 (RFM_GET_ALL_PARAM)
  - TX: `cf ff 00 72 00 17 a5`
  - RX example (hex):
    - `cf 00 00 72 1a 00 00 00 01 80 04 00 01 01 03 86 02 ee 01 f4 31 21 01 04 00 00 0c 00 03 01 05 2c 68`
    - LEN=0x1a (26) → 1 byte STATUS + 25 bytes payload

- Set all parameters: CMD 0x0071 (RFM_SET_ALL_PARAM)
  - We set WorkMode=0 (answer mode) and BuzzerTime=0 (mute). Full frame we sent:
  - TX: `cf ff 00 71 19 00 00 00 80 04 00 01 01 03 86 02 ee 01 f4 31 21 01 04 00 00 0c 00 03 00 05 f6 81`
  - RX: `cf 00 00 71 01 00 f1 56` → STATUS=0x00

Other relevant command IDs from the manual in this repo (not all tested here):
- 0x0001: Start inventory (continuous)  
- 0x0070: Get device info (SN/versions)  
- 0x0064: Set/Get network params (device IP/port; default port 2022)  
- 0x0050: Module init  
- 0x0052: Reboot / restore defaults  
- 0x0053: Set RF power  
- 0x0059: Set/Get RF protocol (manual says only ISO 18000‑6C)
- 0x0077: Relay control (example frames are in `ChafonCF69xRfidAdapter.cs`)

## RFM_SET_ALL_PARAM (0x0071) payload layout (per manual; 25 bytes observed)
Index → Name (size)
- 0: Addr (1)
- 1: RFIDPRO (1) — 0x00 = ISO 18000‑6C
- 2: WorkMode (1) — 0: answer, 1: active (continuous), 2: trigger
- 3: Interface (1) — 0x80 RS232, 0x40 RS485, 0x20 RJ45, 0x10 WiFi
- 4: Baudrate (1) — 0→9600, 1→19200, 2→38400, 3→57600, 4→115200
- 5: WGSet (1)
- 6: Ant (1) — bitmask for antennas
- 7–14: RfidFreq (8) — region and frequency plan (see manual)
- 15: RfidPower (1) — dBm (0–30)
- 16: InquiryArea (1) — 0x01 EPC (default), 0x02 TID, 0x03 User
- 17: QValue (1) — 0–15 (default 4)
- 18: Session (1) — 0→S0, 1→S1, 2→S2, 3→S3
- 19: AcsAddr (1)
- 20: AcsDataLen (1)
- 21: FilterTime (1) — seconds (0–255)
- 22: TriggerTime (1) — seconds (0–255)
- 23: BuzzerTime (1) — units of 10 ms; 0 disables sound
- 24: PollingInterval (1)

Example payload observed from device (25 bytes):
`00 00 01 80 04 00 01 01 03 86 02 ee 01 f4 31 21 01 04 00 00 0c 00 03 01 05`
- We changed byte 2 (WorkMode) from 0x01 → 0x00 and byte 23 (BuzzerTime) from 0x01 → 0x00.

## Minimal Python examples
Stop inventory:
```python
import socket

def crc16_ibm(d: bytes):
    c = 0xFFFF
    for b in d:
        c ^= b
        for _ in range(8):
            c = (c >> 1) ^ 0x8408 if c & 1 else c >> 1
    return c & 0xFFFF

host=("192.168.1.200",2022)
frame=bytes([0xCF,0xFF,0x00,0x02,0x00])
crc=crc16_ibm(frame)
frame+=bytes([(crc>>8)&0xFF, crc & 0xFF])
with socket.create_connection(host,timeout=2) as s:
    s.sendall(frame)
    print(s.recv(1024).hex())
```

Disable beep and set answer mode:
```python
import socket

def crc16_ibm(d: bytes):
    c = 0xFFFF
    for b in d:
        c ^= b
        for _ in range(8):
            c = (c >> 1) ^ 0x8408 if c & 1 else c >> 1
    return c & 0xFFFF

host=("192.168.1.200",2022)
# GET_ALL_PARAM (0x0072)
req=bytes([0xCF,0xFF,0x00,0x72,0x00]); crc=crc16_ibm(req); req+=bytes([(crc>>8)&0xFF, crc&0xFF])
with socket.create_connection(host,timeout=2) as s:
    s.sendall(req); resp=s.recv(2048)
ln=resp[4]; payload=bytearray(resp[6:6+(ln-1)])
# WorkMode -> 0, BuzzerTime -> 0
payload[2]=0x00
payload[23]=0x00
setf=bytes([0xCF,0xFF,0x00,0x71,len(payload)])+payload
crc=crc16_ibm(setf); setf+=bytes([(crc>>8)&0xFF, crc&0xFF])
with socket.create_connection(host,timeout=3) as s:
    s.sendall(setf); print(s.recv(1024).hex())
```

## Notes
- Manual states default device TCP port is 2022 and remote reporting port default 5000 when configured as client.
- LEN and CRC rules above matched every transaction tested.
- The repo also contains C# examples and relay control frames (`ChafonCF69xRfidAdapter.cs`).
