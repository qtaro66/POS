
#### D12.8 Backup, Restore, RPO และ RTO — ยืนยัน

- ธุรกรรมที่ตอบ `BranchAccepted` แล้วต้องไม่สูญหายเมื่อ process crash หรือระบบปฏิบัติการ restart
- สำรองอัตโนมัติไป Cloud เมื่อออนไลน์ และมี encrypted/versioned local backup บน Internal physical disk ลูกที่ 3 ซึ่งแยกจาก live RAID; ไม่ใช้ USB storage เป็นสื่อ Local Backup ประจำระบบ
- Backup บนดิสก์เดียวกับฐานข้อมูลหลักไม่ถือว่าป้องกัน disk failure
- ต้องทดสอบ restore จริงและตรวจจำนวนออร์เดอร์ ยอดเงิน outbox และ audit log
- ชุดข้อมูลที่อยู่ภายใต้ RPO ต้องกู้คืนอย่างสอดคล้องกัน ได้แก่ ธุรกรรมที่ `BranchAccepted`, payment/cancellation state, table session, request deduplication, audit log, print outbox, cloud-sync outbox และผลกระทบต่อสต็อก ห้ามกู้เฉพาะยอดขายโดยสูญเสียข้อมูลประกอบเหล่านี้
- เป้าหมายเมื่อ Branch Service/process ล่ม: `RPO = 0`, `RTO <= 5 นาที`
- เป้าหมายเมื่อระบบปฏิบัติการ restart หรือไฟดับตามปกติ: `RPO = 0`, `RTO <= 15 นาที`
- เป้าหมายเมื่อ SSD หรือ PC เสียทั้งเครื่องขณะออนไลน์: `RPO <= 5 นาที`, `RTO <= 4 ชั่วโมง` โดยต้องมี Cloud backup/sync ที่ตรวจสอบได้และมี hardware ทดแทนพร้อม
- เป้าหมายเมื่อ SSD หรือ PC เสียทั้งเครื่องขณะอินเทอร์เน็ตขาด: `RPO <= 15 นาที`, `RTO <= 4 ชั่วโมง` โดยต้องสำรองอย่างน้อยทุก 15 นาทีไปยังสื่อคนละ physical device และมี hardware ทดแทนพร้อม
- หากไม่มี hardware ทดแทนหรือสื่อสำรองตามเงื่อนไข จะไม่สามารถรับรอง `RTO <= 4 ชั่วโมง` หรือ RPO ของกรณีเครื่องเสียทั้งเครื่องได้ ต้องแสดงข้อจำกัดนี้ในแผนปฏิบัติการสาขา
- รุ่น/ชนิด/ความจุของ Internal Backup disk ยังต้องเลือกและทดสอบก่อน production แต่ต้องเป็นคนละ physical device และไม่เป็นสมาชิกของ live RAID

ประมาณการ Capacity ต่อสาขาจากข้อมูลตัวอย่างปัจจุบัน:

- ฐานคำนวณคือ 8,156 บิลใน 31 วัน หรือเฉลี่ยประมาณ 263 บิล/วัน และ Activity Log ที่ตรวจสอบความถูกต้องได้ 4 วัน ซึ่งมีประมาณ 3.69 activity events และ 5.66 product-detail records ต่อบิล
- ปริมาณฐานข้อมูลก่อนเผื่อ capacity ประมาณ `100–300 MB/เดือน` รวม transaction, order/payment, activity/audit, deduplication, print/cloud outbox, indexes และ database overhead แต่ไม่รวมรูปภาพ/ไฟล์แนบและ application log ที่เก็บเกิน retention
- **ยืนยันเผื่อ Capacity เพิ่ม 15%:** ใช้ค่าประมาณสำหรับวางแผน `115–345 MB/เดือน/สาขา` หรือ `1.38–4.14 GB/ปี/สาขา` สำหรับข้อมูลใหม่
- เพื่อใช้ขนาดจัดสรรที่ปฏิบัติได้ ให้ปัดเป็น `120–350 MB/เดือน` และ `1.5–4.2 GB/ปี` ต่อสาขา
- Local backup usable capacity ขั้นต่ำคำนวณจาก 20 GB + 15% = 23 GB จึงกำหนดขนาดจัดสรรขั้นต่ำ `25 GB/สาขา`
- Cloud backup usable capacity เริ่มต้นคำนวณจาก 30–50 GB + 15% = 34.5–57.5 GB จึงกำหนดขนาดจัดสรรเริ่มต้น `40–60 GB/สาขา`
- Capacity ข้างต้นเป็น planning baseline ไม่ใช่เพดานถาวร ต้องวัดจากฐานข้อมูลและไฟล์ backup จริงหลังใส่ข้อมูลจำลองอย่างน้อย 10,000 บิล และทบทวนใหม่เมื่อกำหนด retention, compression, จำนวน full snapshot และการเก็บ application log

**หมายเหตุสถานะการตัดสินใจ Backup:** แนวทางระดับสถาปัตยกรรมได้รับการยืนยันแล้ว แต่ Backup Specification ยังไม่พร้อมสำหรับการจัดซื้อหรือติดตั้งจริง และให้พักการตัดสินใจรายละเอียดต่อไปนี้ไว้ก่อน

- ระยะเวลาเก็บ Daily, Weekly และ Monthly Backup
- อุปกรณ์และสถานที่สำหรับ Offline/Offsite copy
- จำนวน HDD, RAID level และ usable capacity ของ NAS
- RPO/RTO ของระบบ Master Data ส่วนกลาง
- ผู้ถือกุญแจเข้ารหัสและขั้นตอนกู้คืนกุญแจ
- ความถี่และขั้นตอนการทดสอบ Restore
- Database engine, backup format และเครื่องมือ Backup ที่ใช้จริง
- วิธีทำ immutable snapshot และการป้องกันไม่ให้สาขาแก้ไขหรือลบ Backup เก่า

ข้อสรุปที่คงไว้ระหว่างพักรายละเอียดคือ NAS ส่วนกลางเก็บ Master Data Backup และ Branch Recovery Bundle แยกคนละ Storage Pool; แต่ละ Pool ใช้ดิสก์แบบ Mirror/RAID; มี Offline หรือ Offsite copy เพิ่มอีกหนึ่งชุด; สาขาสื่อสารผ่าน VPN พร้อม TLS และแยกสิทธิ์รายสาขา; Master Data ที่ Publish แล้วไหลจากส่วนกลางไปสาขาทางเดียว โดยสาขาเก็บ Active และ Previous Version สำหรับ Offline operation/rollback และไม่มีรูปสินค้า/รูปเมนูในขอบเขต
