
### D19.3 — Availability Target เบื้องต้น — ยืนยัน

- วัด Availability เฉพาะช่วง `OpenStore` ถึง `CloseStore` และแยกอย่างน้อยเป็น PC POS/Branch Service, Mobile ผ่าน LAN/Wi-Fi, ระบบพิมพ์ภายในร้าน และ Cloud/Central connectivity
- Cloud หรืออินเทอร์เน็ตภายนอกขาดไม่ถือว่า POS สาขา Unavailable หากยังเปิดโต๊ะ รับออร์เดอร์ ชำระเงิน และพิมพ์ผ่าน LAN ได้; ต้องเก็บ Outbox และซิงก์ตามหลังตาม D12
- เป้าหมาย Software Availability ของ PC POS/Branch Service คืออย่างน้อย 99.9% ในช่วงเปิดร้าน ภายใต้เงื่อนไขว่า Hardware, ระบบปฏิบัติการ และ LAN ของสาขายังปกติ; ค่านี้ไม่ใช่ End-to-end Branch SLA
- Process ล่มต้อง Auto-restart และกลับมาใช้งานภายใน 5 นาที; OS restart หรือไฟดับตามปกติต้องกลับมาใช้งานภายใน 15 นาที; SSD/PC เสียต้องกู้ภายใน 4 ชั่วโมงเมื่อมี Hardware ทดแทนและ Backup พร้อม ตาม D12.8
- เมื่ออินเทอร์เน็ต/Cloud ขาด งานขายภายในสาขาต้องทำต่อได้โดยไม่กำหนดเวลาสูงสุดล่วงหน้า ตราบเท่าที่ Hardware/LAN และพื้นที่ฐานข้อมูลสาขายังปกติ
- เมื่อ LAN/Wi-Fi ขาด PC POS ที่อยู่บน Branch PC ยังขายได้ แต่ Mobile และ Network Printer อาจใช้งานไม่ได้; UI ต้องแสดงขอบเขตผลกระทบและห้ามรายงานว่าระบบสาขาทั้งหมดปกติ
- Planned Maintenance และการ Update ที่ต้องหยุดบริการต้องทำนอกช่วง `OpenStore`; Update ล้มเหลวต้อง Rollback ได้โดยไม่สูญเสียธุรกรรมที่ commit แล้ว
- เนื่องจาก Phase 0 มี Branch PC เดียวและยังไม่มี Hardware/LAN redundancy จึงยังไม่กำหนด End-to-end Availability SLA ของสาขา; ต้องทบทวนหลังเลือก UPS, Network, Hardware สำรอง และกระบวนการเปลี่ยนเครื่องจริง
