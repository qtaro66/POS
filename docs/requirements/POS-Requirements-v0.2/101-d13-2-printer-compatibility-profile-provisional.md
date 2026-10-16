
### D13.2 — Printer Compatibility Profile — ยืนยันแบบ Provisional

- เครื่องเป้าหมายระยะแรกคือ **Epson TM-T82X, Model M352A**, Firmware `13.07 ESC/POS`, Network Firmware `01.04`, Ethernet ในตัว ตามป้ายเครื่องและ Self-test ที่ตรวจจากเครื่องจริง
- Self-test แสดง Resident Character `Thai 1 pass` และ `Thai 3 pass` แต่ Production Profile ยังคงใช้ Bitmap/Raster เป็นหลัก ไม่ใช้ข้อมูลดังกล่าวเป็นหลักฐานรับรอง Native Thai Code Page
- Network Test ที่ยืนยันล่าสุดใช้ Printer IP `192.168.2.3` และ PC LAN IP `192.168.2.2` แบบเชื่อมต่อสาย LAN โดยตรง; ค่านี้เป็น Test Configuration ไม่ใช่ Production Address และยังไม่ได้พิสูจน์ DHCP/Static persistence หลัง reboot บนเครือข่ายสาขาจริง
- Profile `epson-tm-t82x-m352a-v1` ผ่านผลทดสอบแบบ **Provisional** ดังนี้: RAW TCP 9100, Thai Raster ด้วย Noto Sans Thai Looped ที่ความกว้าง 576 dots/threshold 180, Paper Cut, Drawer Pulse, Drawer Open/Closed Status, Cover Open/Closed ผ่าน SNMP, Paper Out/Restored ผ่าน SNMP และการล้าง Active Paper-Out Alert หลังใส่กระดาษกลับ
- `DLE EOT 2` ไม่ตอบ byte ขณะฝาเปิดในการทดสอบ จึงห้ามใช้เป็นวิธีเดียวสำหรับตรวจ Cover Status ของ Profile นี้; วิธีหลักที่พิสูจน์แล้วคือ SNMP Printer MIB
- ผล SNMP ที่พิสูจน์กับเครื่องทดสอบ: Cover `3 = open`, `4 = closed`; Paper Out รายงาน Current Level `0`/Status `19`; เมื่อใส่กระดาษกลับรายงาน Current Level `-3`/Status `0`; Alert Code `808` พร้อมคำอธิบาย `Roll Paper End` ต้องหายเมื่อสถานะกลับปกติ
- Drawer Pulse บน drive pin 2 เปิดลิ้นชักจริง และ `GS r 2` ให้สถานะ pin 3 `HIGH = closed`, `LOW = open` กับลิ้นชักที่ทดสอบ
- ระยะ Phase 0 ให้สถานะอัตโนมัติสูงสุดเป็น `PrintDispatched`; ห้ามใช้ `Completed` จนกว่าจะพิสูจน์ Status/ACK ที่เชื่อมโยงกับ Print Attempt นั้นและยืนยันผลตาม D13/D20 ได้
- รายการที่ยังไม่ผ่านการรับรองและต้องปิดก่อน Production-certified ได้แก่ Cutter Error/Alert Mapping, Windows Production Service Path, Linux Production Service Path, ความเทียบเท่า Output ระหว่าง Windows/Linux, DHCP/Static persistence หลัง reboot, Offline/Restart และ Timeout แบบ End-to-End กับ persistent queue รวมถึง Job-specific Print Completion
- Serial Number และ MAC Address ใช้ในทะเบียนอุปกรณ์ภายในได้ แต่ต้อง Redact หรือแทนด้วยข้อมูลจำลองใน Public Repository, Test Fixture, Screenshot และเอกสารสาธารณะ
- หาก Status Protocol ไม่สามารถยืนยันว่ากระดาษของงานนั้นออกจริง ให้คง `PrintDispatched`; เมื่อขาดการเชื่อมต่อหรือไม่ทราบผลหลังเริ่มส่งให้ใช้ `Unknown` และห้าม Retry อัตโนมัติ
