
### D08.2 — Branch Sold Out Override — ยืนยัน

- `SoldOut` เป็น Branch Operational Override ที่สาขากำหนดเอง แยกจาก Master Data และไม่คำนวณอัตโนมัติจากยอดสต็อกหรือวัตถุดิบ
- วัตถุดิบเป็น 0 หรือติดลบไม่ทำให้สินค้า `SoldOut` อัตโนมัติ สินค้ายังขายได้จนกว่าสาขาจะตั้งสถานะเอง
- สถานะต้องผูกกับ `branchId + sellableItemId` เพื่อให้แต่ละสาขากำหนด Sold Out ต่างกันได้ และการ Publish Master Data ต้องไม่ลบ Branch Override โดยไม่ตั้งใจ
- ผู้ใช้ PC POS ที่สาขาสามารถตั้งและยกเลิก Sold Out ด้วย identity ของพนักงานที่ Login อยู่ โดยไม่ต้องกรอก PIN เพิ่ม
- Mobile แสดงสถานะ Sold Out และสามารถตั้ง/ยกเลิกได้เฉพาะพนักงาน Active ที่ส่วนกลางกำหนดสิทธิ์ `SetSoldOut`; Mobile ที่ไม่มีสิทธิ์เป็น Read-only
- สิทธิ์ Mobile ใช้ค่าล่าสุดที่ Branch Service ซิงก์และ cache ได้ตาม D11 เพื่อรองรับการทำงานใน LAN ขณะอินเทอร์เน็ตภายนอกขาด
- Audit ต้องเก็บผู้เปลี่ยน เวลา Branch, device, สินค้า และสถานะก่อน/หลังทุกครั้ง
