
### D13.3 — Printer Status และผลของ Print Attempt — ยืนยัน

- ก่อนส่ง byte แรก หากตรวจพบ `CoverOpen`, `PaperOut`, `CutterError` หรือ `Offline` ให้คงงานเป็น `PrintQueued`, บันทึก `blockedReason` และยังไม่เพิ่มจำนวน Print Attempt; Worker ส่งต่อได้เมื่อเครื่องกลับเป็น `Ready`
- หากเชื่อมต่อไม่ได้ก่อนส่ง byte แรกให้เป็น `Failed` และ Retry อัตโนมัติได้ตาม Backoff Policy
- เมื่อเริ่มส่ง byte แรกให้เปลี่ยนเป็น `Dispatching`; หาก Connection หลุดหรือ Timeout หลังจากจุดนี้ให้เป็น `Unknown` และห้าม Retry อัตโนมัติ
- เมื่อส่ง Payload ครบโดยไม่มี Transport Error ให้เป็น `PrintDispatched` ซึ่งยืนยันเฉพาะผลระดับ Transport ไม่ใช่หลักฐานว่าเอกสารถูกพิมพ์หรือกระดาษออกจริง
- `DLE EOT` หรือ Automatic Status Back ใช้ตรวจ Printer Condition เช่น Cover, Paper และ Error เท่านั้น ห้ามใช้ยกระดับเป็น `Completed` เว้นแต่พิสูจน์ได้ว่า Status/ACK นั้นผูกกับ `printAttemptId` ของงานโดยเฉพาะ
- หากเกิด Printer Error ระหว่างหรือทันทีหลัง Dispatch และไม่ทราบว่างานออกบางส่วนหรือทั้งหมดหรือไม่ ให้เป็น `Unknown` และต้องตรวจเครื่องจริงก่อน Reprint
- หน้าสถานะระบบต้องแสดงอย่างน้อย `Ready`, `Offline`, `Cover Open`, `Paper Out`, `Cutter Error`, `Status Unsupported` และ `Unknown`
- หลังตรวจเครื่องจริง การตัดสินของพนักงานให้บันทึกเป็น Resolution แยกจากสถานะต้นฉบับ ได้แก่ `ConfirmedPrinted`, `ReprintRequested` หรือ `DuplicateRiskAccepted` พร้อมผู้ดำเนินการและเวลา
- ห้ามแก้หรือลบ Print Attempt เดิม; Reprint ต้องสร้าง Print Job/Attempt ใหม่และอ้างอิงงานต้นทางเพื่อรักษา Audit Trail
- Profile `epson-tm-t82x-m352a-v1` ยังไม่รองรับสถานะอัตโนมัติ `Completed` จนกว่าจะผ่านการพิสูจน์ Job-specific ACK ตามเกณฑ์ข้างต้น
