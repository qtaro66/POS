
## 6. Back Office และค่าตั้งรายสาขา

**ยืนยัน:** เป็นระบบแยก ส่วนกลางไม่ต้องผ่าน POS เพื่อแก้ค่าตั้ง ผู้จัดการหลักเป็นส่วนกลาง

ขอบเขตที่ระบุ:

- ข้อมูลร้าน/สาขา
- โซนและจำนวนโต๊ะในแต่ละโซน ต้องปรับได้ยืดหยุ่นกว่าระบบเดิม
- ประเภทการชำระเงิน
- อุปกรณ์ Mobile ของสาขา
- พนักงานและ PIN สำหรับ POS
- การผูกสถานีพิมพ์กับเครื่องพิมพ์จริงในแต่ละสาขา
- Master Data เมนูร่วมหลายสาขา

ค่าตั้งจากภาพสำหรับพิจารณารายละเอียด: ค่าบริการ เวลาเปิด–ปิด การปัดเศษ การแสดงส่วนลด ภาษี ช่องทางรับรายงาน ตัวจับเวลา การแสดงผล และบัญชีผู้ใช้/สิทธิ์ ไม่ถือว่าผู้ใช้ได้ยืนยันทุกช่องและทุกพฤติกรรมในภาพแล้ว

PIN พนักงานสต็อกไม่อยู่ในขอบเขตเบื้องต้น การที่ระบบเดิมไม่ใช้ authorization tool สำหรับงานนี้ไม่ได้หมายความว่าระบบใหม่ควรเปิด Back Office โดยไม่มีการตรวจตัวตน การออกแบบสิทธิ์รวมยังเป็นประเด็นเปิด

### 6.1 การส่งยอดตามช่องทางไป Google Sheets — ยืนยัน

- งานส่ง Google Sheets เป็นความรับผิดชอบของบริการส่วนกลาง ไม่ใช่ Branch Service และไม่อยู่ใน transaction ของ `CloseStore`
- `CloseStore` ต้อง commit local closing/report snapshot และ persistent outbox ได้สำเร็จแม้อินเทอร์เน็ตหรือ Google ใช้งานไม่ได้ จากนั้นสาขาซิงก์ snapshot ไปส่วนกลางตาม D12
- เมื่อส่วนกลางรับ `CloseStore` snapshot แบบ durable แล้ว Central Integration Worker จึงรวมยอดตาม `branchId + businessDate + paymentMethodId` และเขียนหรืออัปเดต Google Sheets
- ยอดของแต่ละช่องทางต้องรวมจาก `PaymentLine.appliedAmount`; เงินสดห้ามใช้ `tenderedAmount` และห้ามนับ `changeAmount` เป็นยอดขาย กรณี Split Tender ต้องจัดจำนวนของแต่ละ Payment Line ให้ช่องทางของตนเท่านั้น
- รูปแบบข้อมูลใช้หนึ่งแถวต่อสาขา/วันธุรกิจ/Payment Method เพื่อรองรับ Custom Payment Method โดยมีอย่างน้อย Business Date, Branch ID/Name, Payment Method ID/Name, Amount, Payment Count และเวลาส่งออก
- หากหนึ่งสาขามี `CloseStore` มากกว่าหนึ่ง `storeSessionId` ใน business date เดียว ส่วนกลางต้อง aggregate snapshot ที่ไม่ซ้ำทั้งหมดแล้ว upsert ยอดรวม ห้าม append จนเกิดการนับซ้ำ
- Integration Worker ต้องเก็บสถานะ `Pending`, `Exported` หรือ `Failed`, รองรับ retry และ manual replay แบบ idempotent โดยอ้างอิง source snapshot IDs/export version
- Google credentials ต้องเก็บและใช้งานที่ส่วนกลางเท่านั้น ห้ามกระจายไป Branch PC; Google Sheets ไม่ใช่ source of truth ข้อมูลหลักยังเป็น closing snapshots/ฐานข้อมูลส่วนกลาง
- Google outage หรือ export failure ไม่ย้อน `CloseStore` และไม่แก้ยอดขาย ระบบต้องเก็บข้อผิดพลาดและลองส่งใหม่ได้
