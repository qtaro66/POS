
#### D17 — Technology Stack และรูปแบบติดตั้ง — ยืนยัน

Technology Stack ที่ล็อกสำหรับเริ่มโครงสร้างโปรเจกต์:

| ส่วน | ข้อสรุป |
|---|---|
| PC POS | Web application เปิดด้วย Edge/Chromium แบบ kiosk; ไม่ใช้ Electron |
| Mobile | Phase 0 ใช้ React + TypeScript แบบ PWA เพื่อพิสูจน์ workflow/offline ได้; เป้าหมายต้องรองรับ Mobile application บน Android และ iOS โดยรูปแบบ Native, Cross-platform หรือ Installable PWA ยังต้องตัดสินใจก่อนล็อก production stack |
| Back Office | React + TypeScript |
| Frontend build | Vite; ใช้ OpenAPI-generated TypeScript client และ IndexedDB สำหรับ Mobile draft/outbox |
| Branch/Central backend | C# + ASP.NET Core บน .NET 10 LTS |
| Branch database | SQLite แบบ WAL บน local RAID 1; Branch Service เป็นผู้เปิดฐานข้อมูลเพียง process เดียว |
| Central database | PostgreSQL 18 |
| API | REST/JSON ผ่าน HTTPS, OpenAPI, schema/API version และ `requestId` สำหรับคำสั่งเปลี่ยนข้อมูล |
| Printing | Direct Network PrintGateway; ไม่ใช้ `PrinterInterface_335.exe` |
| Central deployment | Linux server/VM หรือ container; database อยู่บน application/database host และส่ง backup ไป NAS |
| Branch deployment | Cross-platform self-contained build; Windows Service สำหรับเครื่องเดิม และ Linux systemd package สำหรับ pilot/new Mini PC |

ข้อกำหนดเชิงโครงสร้าง:

- Domain/Application layer ต้องไม่อ้าง Windows Registry, WMI, drive letter, systemd, Windows Service หรือคำสั่งเครื่องพิมพ์เฉพาะรุ่น
- แยก `Pos.Printing.Abstractions`, `Pos.Printing.EscPosTcp`, `Pos.Printing.Ipp`, `Pos.Platform.Windows` และ `Pos.Platform.Linux`
- UI และ domain logic ห้ามเปิด SQLite หรือส่งข้อมูลไปเครื่องพิมพ์โดยตรง ทุกคำสั่งต้องผ่าน Branch API/Application layer
- Branch Service ต้องบันทึก transaction, audit และ persistent outbox ภายใน transaction boundary ที่กำหนดใน D12/D20
- SQLite ต้องอยู่บน local storage เท่านั้น ห้ามวาง live database บน NAS/network share; ใช้ WAL, durability policy, checkpoint, integrity check และ SQLite Backup API
- เปลี่ยน Branch database เป็น client/server database เมื่อมี Branch Service หลาย instance, writer หลาย process, ต้องทำ database HA หรือผล load test ไม่ผ่าน

Printing stack:

- `IPrintGateway` ต้องรองรับ adapter อย่างน้อย `EscPosTcpPrintGateway` และ `IppPrintGateway`; LPR ใช้ได้เฉพาะรุ่นที่จำเป็น
- ค่า IP, port, protocol, paper width, DPI, Thai encoding, cutter, cash drawer, status command และ timeout/retry อยู่ใน Printer Profile รายรุ่น ห้าม hard-code ใน business logic
- เส้นทางหลักที่เสนอคือ ESC/POS ผ่าน TCP port 9100 เฉพาะเครื่องที่พิสูจน์ว่ารองรับ; การมี RJ45 เพียงอย่างเดียวไม่ถือเป็นหลักฐานว่าใช้ ESC/POS/9100 ได้
- Printer อยู่ใน VLAN/segment ของสาขาและ firewall อนุญาตเฉพาะ Branch PC; Mobile ห้ามเชื่อม Printer โดยตรง
- การส่งลง socket สำเร็จรับรองได้เพียง `PrintDispatched`; ห้ามถือว่า `Completed` หากไม่มี status protocol ที่ยืนยันผลจริง

รูปแบบระบบปฏิบัติการและ rollout:

- Core, Branch API, SQLite, sync, backup และ PrintGateway ต้อง build/test ได้ทั้ง `win-x64` และ `linux-x64`
- เครื่อง AIO เดิมใช้ Windows 11 Pro ใน Phase 0; Mini PC ใหม่ทดลอง Linux แบบ pinned LTS distribution อย่างน้อยหนึ่งสาขาก่อนกำหนดเป็น production standard
- Linux ใช้ systemd, Chromium kiosk, mdadm RAID 1 และ LUKS; Windows ใช้ Windows Service, Edge kiosk, RAID ที่ผู้ผลิตรองรับ และ BitLocker
- ไม่ใช้ Docker ที่ Branch ในระยะแรก; Central สามารถใช้ container ได้
- Linux จะถือว่า officially supported หลังผ่าน printer compatibility, 20-Mobile load, HTTPS/Private CA, RAID degraded/rebuild, power-loss, backup/restore, update/rollback และ operational pilot
- การรองรับสอง OS ไม่ลดข้อกำหนด QA: release ต้องมี artifact, signature/checksum, application/API/schema/printer-profile version, migration, backup ก่อน upgrade และ rollback policy แยกตาม OS

ข้อกำหนด Mobile เพิ่มเติม:

- Android และ iOS ต้องเป็น first-class supported clients ของระบบ ไม่ใช่รองรับแบบ best-effort ผ่าน browser เท่านั้น
- ฟังก์ชันหลักและกฎทางธุรกิจต้องอยู่ที่ Branch API/Application layer ห้ามแยก implementation จน Android และ iOS ตัดสินผลธุรกิจต่างกัน
- ต้องมี automated contract/integration tests ชุดเดียวกันสำหรับ client ทั้งสองระบบ และทดสอบอุปกรณ์จริงอย่างน้อยหนึ่งรุ่นต่อระบบตั้งแต่ Phase 0
- ก่อนล็อก production stack ต้องเปรียบเทียบ Installable PWA, Cross-platform app และ Native app ตาม offline persistence, background/resume, QR pairing, certificate/private CA, device credential, deployment/update, printer isolation และต้นทุนดูแลระยะยาว
