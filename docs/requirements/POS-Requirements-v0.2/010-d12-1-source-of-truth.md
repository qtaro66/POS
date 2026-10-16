
#### D12.1 Source of Truth — ยืนยัน

- Branch Database เป็น source of truth ของธุรกรรมขายภายในสาขา
- Cloud เป็น source of truth ของ Master Data และค่าตั้งส่วนกลาง
- Cloud ห้ามเขียนทับธุรกรรมสาขาด้วย last-write-wins
- ตอน `OpenTable` Branch ต้องตรึง `sessionMasterDataVersion` สำหรับชื่อสินค้า ราคา โครงสร้าง Topping/Option ภาษี ส่วนลด และสูตรคำนวณของ table session นั้นจนจบบิลตาม D09; แต่ทุก `SubmitOrder` ยังต้องตรวจสถานะ session, Option และสิทธิ์ขายปัจจุบัน
