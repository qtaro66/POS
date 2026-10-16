
### Phase 0 — Architecture Spike

1. Branch Service แบบ cross-platform + SQLite WAL + health check โดย build ทั้ง `win-x64` และ `linux-x64`; Phase 0 เริ่มบน Windows เครื่องเดิม
2. Mobile PWA บน Android/iOS เชื่อมผ่าน LAN ด้วย HTTPS จาก Private CA ที่อุปกรณ์ทดสอบติดตั้งและ trust แล้ว
3. persistent outbox, `requestId`, status query และ idempotent order endpoint
4. Direct Network PrintGateway ที่บันทึก `printJobId`/ผลตอบกลับและส่งไป Thermal Printer ทดสอบผ่าน protocol ที่ D13 ยืนยัน
5. ทดสอบ failure matrix: อินเทอร์เน็ตขาด, Wi-Fi หลุด, process restart, socket timeout, printer offline, paper out และผลไม่ทราบหลังเริ่มส่ง
6. ทดลอง Linux บน Mini PC อย่างน้อยหนึ่งสาขาหลัง Direct Print ผ่าน compatibility test แล้ว

Phase 0 ยังไม่ต้องมีหน้าจอครบ เป้าหมายคือพิสูจน์โครงสร้าง offline-first, Direct Network Printing และ recovery ก่อนขยาย business feature
