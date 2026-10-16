
#### D21.5 — Local Workspace และ Repository Bootstrap — ยืนยัน

- จากการตรวจเครื่องพัฒนา ณ วันที่กำหนดข้อ requirement พบว่า `C:` เป็น Fixed Drive เพียงไดรฟ์เดียว ส่วน `D:` เป็น FAT32 Removable Drive และ `E:` เป็น NTFS Removable Drive ที่ Windows รายงานสถานะ `Warning`; จึงห้ามใช้ `D:` หรือ `E:` เป็น Workspace หลัก
- รอเพิ่ม Internal SSD ก่อนกำหนด Drive Letter จริง โดยวางโครงสร้างเป้าหมายเป็น `<InternalDrive>:\Dev\pos-platform`, `<InternalDrive>:\NicePOSData\dev` และ `<InternalDrive>:\NicePOSCache`
- Source Code ห้ามอ้างอักษรไดรฟ์ตายตัว ให้กำหนดรากผ่าน `POS_DEV_ROOT`, `POS_DATA_ROOT` และ `POS_CACHE_ROOT` หรือ Configuration ที่ให้ผลเทียบเท่า
- Remote Repository ที่สร้างไว้แล้วต้องนำมาเป็น Working Copy ด้วย `git clone`; ห้ามใช้ `git init` สร้างประวัติใหม่ซ้ำ
- `C:\Users\qtaro\Downloads\Niceloop_dev` เป็นพื้นที่จัดเตรียม Requirements ชั่วคราว ไม่ใช่ตำแหน่ง Monorepo หลัก
- ก่อนเริ่มพัฒนาใน Working Copy หลัก ต้องตรวจ Remote URL และ Default Branch, Clone Repository, สร้างโครงสร้าง Monorepo, เพิ่ม License/เอกสารพื้นฐาน, ทำ Secret Scan ก่อน Baseline Commit และเปิด Branch Protection หลัง Push Baseline
- Source, Runtime Data และ Backup ต้องแยกทางตรรกะอย่างชัดเจน; แม้ Development Source/Data/Cache จะอยู่บน Internal SSD ลูกเดียวกันในระยะแรก SSD ลูกนั้นไม่ถือเป็น Backup
- เมื่อติดตั้ง Internal SSD แล้ว ต้องบันทึก Drive Letter, Volume Label, File System, ตำแหน่ง Workspace จริง และผลทดสอบ Clone/Build จาก Clean Working Copy ก่อนปิดรายการ D21.5 ทางปฏิบัติการ
