
### D08.1 — Source of Truth และการ Publish Master Data — ยืนยัน

- Master Data หลักมี Source of Truth ที่ระบบส่วนกลาง สร้าง แก้ ปิดใช้งาน และ Publish ได้เฉพาะผู้ใช้ Back Office ส่วนกลางที่มีสิทธิ์
- PC POS และ Mobile อ่าน Master Data หลักอย่างเดียว ข้อยกเว้นคือ Branch Operational Override เช่น `SoldOut` ตาม D08.2 ซึ่งไม่ถือเป็นการแก้ Master Data
- ข้อมูลเฉพาะสาขาที่ส่วนกลางกำหนดสามารถรวมใน Package เป้าหมายของสาขานั้นได้ แต่ Branch ห้ามแก้ Canonical Master Data และส่งกลับไปทับส่วนกลาง
- Branch Service รับ Master Data ทางเดียวจาก Central Service/API และเก็บ Local Cache เพื่อใช้งานขณะอินเทอร์เน็ตหรือ VPN ขาด; ห้ามเปิดไฟล์ฐานข้อมูลบน NAS ผ่าน VPN โดยตรง
- Package ต้องมี `masterDataVersion`, กลุ่มสาขาเป้าหมาย, เวลา Publish, Schema Version และ Checksum
- Branch ต้องดาวน์โหลด Package ให้ครบ ตรวจ Schema/Checksum แล้ว Activate ทั้ง Package แบบ Atomic หากตรวจไม่ผ่านให้ใช้ Active Version เดิมต่อและห้าม Activate บางส่วน
- Branch ต้องเก็บ Active Version และ Previous Version อย่างน้อยหนึ่งชุดสำหรับ Rollback
- Master Data Version ใหม่ต้องไม่ลบหรือเขียนทับ Branch Operational Override เช่น `SoldOut`
- เมื่อ Branch Offline ส่วนกลางต้องแสดงว่าสาขายังไม่ได้รับหรือ Activate Version ใหม่ ห้ามแสดงว่าใช้งานแล้วก่อน Branch ยืนยันผล
- ทุกการ Publish ต้องเก็บผู้ดำเนินการ เวลา Version สาขาเป้าหมาย และรายการเปลี่ยนแปลงใน Audit Log
- NAS ส่วนกลางใช้เก็บฐานข้อมูลหรือ Backup ได้ แต่การกระจาย Master Data ให้สาขาต้องผ่าน Central Service/API
