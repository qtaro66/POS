
### D09 — ผลของ Master Data ต่อ Table Session และ Notify ล่าช้า — ยืนยัน

- ตอน `OpenTable` Branch ต้องบันทึก `sessionMasterDataVersion` จาก Active Package ล่าสุด และตรึงชื่อสินค้า ราคา โครงสร้าง Topping/Option ภาษี ส่วนลด และกฎคำนวณของ table session นั้นจนเป็น `Completed`, `Cancelled` หรือ `Closed`
- `tableSessionId` ที่เปิดใหม่ใช้ Active Master Data Version ล่าสุดของ Branch; คำว่า “รายการใหม่ใช้ Version ล่าสุด” หมายถึงรายการใน table session ที่เปิดใหม่ ไม่ใช่ Order Batch ใหม่ที่เพิ่มเข้า session เดิม
- Order Batch ใหม่ที่เพิ่มเข้า `tableSessionId` เดิมยังใช้ราคาและกฎคำนวณของ `sessionMasterDataVersion` เดิม แม้ Branch จะ Activate Package ใหม่แล้ว; Order Line ที่ Branch รับต้องเก็บ Snapshot และห้ามเปลี่ยนย้อนหลัง
- Branch ต้องตรวจทุก `SubmitOrder` โดยไม่ขึ้นกับอายุของ session: ตรวจว่า session ยังเป็น `Ordering`, `requestId` ไม่ซ้ำ, Option ผ่าน `minSelections`/`maxSelections`, จำนวนและ payload ถูกต้อง และ Item/Option ยังได้รับอนุญาตให้ขาย
- `CentralActive`, `BranchCatalogEnabled` และ Branch `SoldOut` เป็นสถานะการขายปัจจุบันที่ต้องตรวจทุก `SubmitOrder`; การตรึงราคาไม่ใช่การอนุญาตให้ขายสินค้าที่ถูกปิดหรือ Sold Out
- หาก Draft อยู่ใน session เดิม Branch ไม่ต้องเปลี่ยนราคาให้ตรง Package ล่าสุด แต่ยังต้อง Validation ตามข้อข้างต้น; หากไม่ผ่านให้ปฏิเสธทั้ง Batch และเก็บ Draft ไว้พร้อมระบุรายการที่ต้องแก้
- Draft ที่อ้าง `tableSessionId` ซึ่งปิดแล้วหรือถูกแทนด้วย session ใหม่ต้องถูกปฏิเสธ แม้เลขโต๊ะเดิมจะถูกเปิดใช้งานอีกครั้ง
- `ChangeTable` คง `tableSessionId` จึงคง `sessionMasterDataVersion`; `TransferItems` ต้องคง Snapshot ของ Order Line ที่ย้าย ห้ามคำนวณราคาใหม่ตาม session ปลายทาง ส่วน Order Batch ใหม่ใน session ปลายทางใช้ Version ที่ตรึงกับ session ปลายทาง
- Price Override ตาม D01.2 ยังทำได้ก่อน `PaymentPending` และมีผลเฉพาะ Order Line โดยไม่เปลี่ยน `sessionMasterDataVersion`
- Recipe และ Stock Movement ยังคง Snapshot ตอน `SubmitOrder` ตาม D07; D09 ไม่เปลี่ยนจุด Snapshot Recipe จนกว่าจะตัดสินใจ D07.2–D07.3
- Notify เป็นเพียงสัญญาณว่ามี Master Data รุ่นใหม่ ไม่ใช่คำสั่งแก้ฐานข้อมูลโดยตรง; Branch ต้องเปรียบเทียบ Version, ดาวน์โหลด ตรวจ Schema/Checksum และ Activate Package แบบ Atomic ตาม D08/D12
- Notify ที่มีอายุเกิน 72 ชั่วโมง โดยคำนวณจาก `issuedAt` ของ Server ต้องไม่ Apply Payload เก่าโดยตรง แต่ต้องทำ Full Version Check และดึง Package ล่าสุด; Notify ซ้ำหรือลำดับย้อนต้องไม่ทำให้ Branch rollback ไป Version เก่า
- หาก Branch ออฟไลน์เกิน 72 ชั่วโมง ให้ขายต่อด้วย Active Package ที่มีอยู่และแสดง Version/เวลาที่ Sync สำเร็จล่าสุด; เมื่อเชื่อมต่อกลับให้ Full Version Check ทันที โดยข้อจำกัดคือการปิดขายจากส่วนกลางไม่อาจมีผลระหว่างที่สาขาไม่มีเส้นทางสื่อสาร
