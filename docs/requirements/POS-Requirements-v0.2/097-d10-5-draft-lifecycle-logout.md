
### D10.5 — Draft lifecycle เมื่อ Logout เปลี่ยนพนักงาน หรือปิดแอป — ยืนยัน

- Draft และ Pending Outbox ต้องเก็บแบบ Persistent บนอุปกรณ์ เช่น IndexedDB ห้ามเก็บเฉพาะในหน่วยความจำ และต้องผูก `deviceId`, `employeeId`, `tableSessionId` และ `sessionMasterDataVersion`
- การปิดแอป Process/เครื่อง Restart หรือเครือข่ายหลุดต้องไม่ทำให้ Draft/Pending สูญหาย
- `Draft` ที่ยังไม่เริ่มส่งสามารถแก้ไขหรือลบได้; `Pending` หรือคำสั่งที่เริ่มส่งแล้วห้ามลบและต้องติดตามผลด้วย `requestId` เดิมจนได้ `BranchAccepted` หรือ `Rejected`
- เมื่อ Logout หากมี Draft/Pending ต้องแสดงจำนวนรายการค้าง แต่ไม่บังคับลบและไม่ห้าม Logout
- พนักงานเดิมที่ Login กลับบนเครื่องเดิมทำ Draft ต่อได้; พนักงาน Active คนอื่นรับช่วง Draft ได้เมื่อกดยืนยันอย่างชัดเจน โดยระบบต้องเก็บผู้สร้างเดิมและผู้รับช่วง
- Pending ที่เริ่มส่งแล้วห้ามเปลี่ยนเจ้าของและต้องคง `employeeId` ของผู้ส่งเดิม
- หาก session เดิมเป็น `PaymentPending`, `Completed`, `Cancelled` หรือ `Closed` ให้ Draft เป็น `Blocked/NeedsReview` และห้าม Retry เข้า session เดิม
- การ Copy รายการจาก Draft ที่ Blocked ไปสร้าง Draft ใน session ใหม่ต้องให้ผู้ใช้ยืนยัน และตรวจราคา Option และสิทธิ์ขายใหม่ทั้งหมด
- หากพนักงานถูกเพิกถอนก่อน Branch รับคำสั่ง Branch ต้องปฏิเสธคำสั่งนั้น; Draft ที่ยังไม่เริ่มส่งสามารถให้พนักงาน Active คนอื่นรับช่วงได้
- Draft ที่ยังไม่เคยส่งเป็นข้อมูลเฉพาะอุปกรณ์และอาจสูญหายหากถอนแอป ล้างข้อมูล หรืออุปกรณ์เสีย จึงห้ามแสดงว่าเป็นออร์เดอร์ที่ Branch รับแล้ว
- ระยะเวลาเก็บและการลบ Draft เก่ากำหนดภายหลังใน Retention Policy
