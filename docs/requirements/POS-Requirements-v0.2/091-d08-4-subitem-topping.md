
### D08.4 — กฎการเลือก SubItem และ Topping — ยืนยัน

- ใช้ `OptionGroup` สำหรับ SubItem และ Topping โดยมีอย่างน้อย `optionGroupId`, ชื่อ, `optionType`, `minSelections`, `maxSelections`, `allowRepeatedOption` และ `maxQuantityPerOption`
- `minSelections = 0` หมายถึง Optional และ `minSelections > 0` หมายถึง Required โดยไม่ต้องมี Required Boolean แยก
- จำนวน Selection ให้นับตามจำนวนจริง ไม่ใช่จำนวนชนิด เช่น ไข่ดาว ×2 และชีส ×1 นับเป็น 3 Selections
- `allowRepeatedOption = false` จำกัดปริมาณต่อ Option ไม่เกิน 1; หากอนุญาตให้ซ้ำต้องไม่เกิน `maxQuantityPerOption` และผลรวมทั้ง Group ต้องไม่เกิน `maxSelections`
- PC POS และ Mobile ต้องตรวจตั้งแต่หน้าจอเลือก และ Branch Service ต้องตรวจซ้ำตอน `SubmitOrder` โดย Branch เป็นผู้ตัดสินขั้นสุดท้าย
- หากเลือกต่ำกว่าขั้นต่ำ เกินเพดาน หรือ Option ไม่สามารถขายได้ ให้ปฏิเสธทั้ง Submit batch และเก็บ Draft บน Mobile ไว้ให้แก้
- Option ที่ `CentralActive = false`, `BranchCatalogEnabled = false` หรือ `SoldOut = true` ห้าม Submit; หากสถานะเปลี่ยนหลังเพิ่มลง Draft ต้องปฏิเสธพร้อมระบุรายการที่ต้องแก้
- กำหนด Default Option ได้ แต่ Default ต้องยัง Active และขายได้
- ราคา Item/SubItem/Topping ต้อง Snapshot ตอน Branch รับ Order Line และหมายเหตุข้อความไม่ถูกนับรวมกับ Option Selection
- ตอน Publish ต้องตรวจว่า `minSelections >= 0`, `maxSelections >= minSelections`, `maxQuantityPerOption > 0` และ Required Group มีตัวเลือกที่ขายได้เพียงพอต่อขั้นต่ำ; ห้าม Publish Group ที่ไม่ผ่าน Validation

แนวคิดเพิ่มเติมจากภาพสำหรับออกแบบ: กลุ่มเมนู การเรียงลำดับ รหัสสินค้า ส่วนลดเริ่มต้น การส่งออก CSV และการจัดกลุ่ม Topping ต้องยืนยันรายละเอียด ไม่ถือข้อจำกัดจำนวน 50/100 หรือชื่อข้อมูลในภาพเป็นข้อจำกัดระบบใหม่

กฎหลักของ Master Data, Sold Out, ราคา/เมนูรายสาขา และ Option Selection ใช้ D08.1–D08.4; รายละเอียด Recipe/หน่วยสต็อกยังเลื่อนตาม D07.2–D07.3
