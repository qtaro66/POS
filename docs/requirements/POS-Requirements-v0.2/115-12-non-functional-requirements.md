
## 12. Non-functional Requirements ที่ต้องกำหนดค่า

ตารางนี้กำหนดหัวข้อที่ต้องมี แต่ค่าตัวเลขยังต้องตอบใน D02/D19 ไม่ให้ผู้พัฒนาสมมติเอง

| ID | หัวข้อ | ค่าที่ต้องกำหนด |
|---|---|---|
| NFR-01 | Concurrent usage | ยืนยัน 1 PC POS/Branch Service และ Mobile พร้อมกันไม่เกิน 20 เครื่องต่อสาขา ต้องทดสอบการเชื่อมต่อและการส่งคำขอพร้อมกันครบ 20 เครื่องโดยไม่สูญหายหรือสร้างรายการซ้ำ |
| NFR-02 | Performance | ใช้ Baseline ชั่วคราวตาม D19.1: p95 เพิ่มรายการ ≤ 100 ms, เปิด Sub Item/Topping ≤ 200 ms, เปิด/เลือกโต๊ะและ Payment Workspace ≤ 500 ms, `SubmitOrder`/ยืนยัน Payment Line/ค้นประวัติวันนี้ ≤ 1 วินาที, เริ่มส่ง Print Job/`CompletePayment` ≤ 2 วินาที; ต้องทบทวนบน Hardware จริงก่อนกำหนด Production SLO |
| NFR-03 | Throughput | ใช้ Test Baseline ชั่วคราวตาม D19.2: 1,000 บิล/วัน, 20 Batch พร้อมกัน, Batch ละ 20 Order Line, Burst 20 Batch/10 วินาที, Sustained 30 Batch/นาที 15 นาที, 10 `CompletePayment`/นาที และ Offline backlog 8 ชั่วโมง; ต้องแทนด้วยผลทดสอบจริงก่อน Production |
| NFR-04 | Availability | วัดช่วง `OpenStore`–`CloseStore`; Software Availability ของ PC POS/Branch Service ≥ 99.9% เมื่อ Hardware/OS/LAN ปกติ; Process recovery ≤ 5 นาที, OS restart/ไฟดับปกติ ≤ 15 นาที และ Hardware failure recovery ≤ 4 ชั่วโมงเมื่อมีเครื่อง/Backup พร้อม; Cloud outage ไม่ทำให้ Local POS unavailable; End-to-end Branch SLA รอกำหนดหลังมี Hardware/LAN redundancy |
| NFR-05 | Recovery และ backup capacity | Service crash: RPO 0/RTO ≤ 5 นาที; OS restart/ไฟดับปกติ: RPO 0/RTO ≤ 15 นาที; SSD/PC เสียขณะออนไลน์: RPO ≤ 5 นาที/RTO ≤ 4 ชั่วโมง; SSD/PC เสียขณะ offline: RPO ≤ 15 นาที/RTO ≤ 4 ชั่วโมง; encrypted local backup อย่างน้อยทุก 15 นาทีลง Internal physical disk ที่แยกจาก live RAID; planning baseline หลังเผื่อ 15% คือข้อมูลใหม่ 120–350 MB/เดือนหรือ 1.5–4.2 GB/ปี, Local backup ขั้นต่ำ 25 GB และ Cloud backup เริ่มต้น 40–60 GB ต่อสาขา; ต้องพิสูจน์ด้วย restore test และข้อมูลอย่างน้อย 10,000 บิล |
| NFR-06 | Retention | ตาม D19.4: Branch transaction/searchable history 24 เดือน; Central transaction/receipt/payment/cancellation/transfer/stock/audit อย่างน้อย 7 ปี; Receipt Snapshot และ deduplication เท่าธุรกรรมต้นทาง; Print metadata 90 วัน; Branch log 30 วัน/Central log 90 วัน; Mobile unsent Draft 30 วันก่อน `Expired/NeedsReview`; Backup retention ยังรอกำหนด |
| NFR-07 | Security | authentication, authorization, session offline, encryption, secret rotation และ audit |
| NFR-08 | Deployment | Windows 11 Pro สำหรับเครื่องเดิม; pinned Linux LTS สำหรับ pilot/new Mini PC; Windows Service/systemd auto-start, signed package, update/rollback, RAID 1 และ Internal Backup disk แยก; development workspace อยู่บน non-system volume และ runtime path แยกจาก Git working copy |
| NFR-09 | Observability | health check, structured logs, metrics, alert และ log rotation |
| NFR-10 | Compatibility | Android/iOS client และ OS รุ่นต่ำสุด, browser/runtime ที่เกี่ยวข้อง, Windows/Linux support matrix และ Thermal Printer รุ่น/firmware/protocol ที่รับรอง |
| NFR-11 | Time | timezone, business day, clock drift และลำดับ event |
| NFR-12 | Data integrity | unique constraints, transaction boundary, idempotency และ reconciliation |
