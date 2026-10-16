
### D10.4 — การเปิดโต๊ะและส่งออร์เดอร์จาก Mobile — ยืนยัน

- ใช้งานได้เฉพาะอุปกรณ์ที่ Pair กับสาขาแล้วและพนักงาน Active ที่ Login สำเร็จ; พนักงาน Active ทุกคนสามารถ `OpenTable` และ `SubmitOrder` ได้โดยไม่ต้องมีสิทธิ์พิเศษหรือกรอก PIN ซ้ำ
- Mobile ดูโต๊ะและ table session ที่กำลังดำเนินการทั้งหมดของสาขาได้ ไม่จำกัดว่าใครเป็นผู้เปิด และไม่ล็อก session ไว้กับพนักงานหรืออุปกรณ์ใด
- หากโต๊ะเป็น `Available`, `OpenTable` ต้องสร้าง `tableSessionId` ใหม่และตรึง `sessionMasterDataVersion`; หากโต๊ะมี session เปิดอยู่แล้วให้คืน session เดิมและห้ามสร้างซ้ำ
- หลาย Mobile สร้าง Draft และ `SubmitOrder` เข้า session เดียวกันได้ตามกฎ Auto-merge; Mobile แก้หรือลบได้เฉพาะ Draft ที่ Branch ยังไม่รับ
- หลัง `BranchAccepted` ห้าม Mobile แก้หรือลบ Order Batch เดิม ต้องใช้ cancellation request ตาม D10.3
- `SubmitOrder` ทำได้เฉพาะ session สถานะ `Ordering` และทุก Batch ต้องบันทึก `deviceId`, `employeeId`, `tableSessionId`, `requestId` และ `orderBatchId`
- หากติดต่อ Branch Service ไม่ได้ Mobile เก็บ Draft/Pending ได้ แต่ห้ามแสดงว่าออร์เดอร์สำเร็จ ห้ามตัดสต็อก และห้ามส่งพิมพ์
- Mobile ไม่มีสิทธิ์ `ChangeTable`, `TransferItems`, `CloseTable`, `CancelBill`, ใส่ส่วนลด, Override ราคา, รับชำระ, Reprint หรือดูประวัติที่ปิดแล้ว
- `SetSoldOut` เป็นข้อยกเว้นที่ทำได้เฉพาะพนักงานที่ส่วนกลางกำหนดสิทธิ์ตาม D08.2
