
#### D12.2 เมื่อ PC/Branch Service ดับ — ยืนยัน

- Mobile สร้าง/แก้ Draft และเก็บคำขอเป็น `Pending` ใน IndexedDB ได้ แต่ยังไม่ถือว่า Branch รับแล้ว ไม่ตัดสต็อก และไม่ส่งพิมพ์
- เมื่อ Branch Service กลับมา Mobile ต้อง query ด้วย `requestId` เดิมก่อน หาก Branch ยังไม่มีจึงส่งคำขอเดิม
- Phase 0 ไม่มี Mobile เครื่องใดทำหน้าที่เป็น server สำรอง
