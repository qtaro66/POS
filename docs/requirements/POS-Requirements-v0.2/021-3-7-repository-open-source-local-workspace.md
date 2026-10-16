
### 3.7 Repository, Open Source และ Local Workspace — ยืนยัน

- ทุกองค์ประกอบของโครงการต้องอยู่ภายใต้ Git และมี Remote Repository เดียวในรูปแบบ Monorepo ตาม D21.1
- หาก remote repository มีอยู่แล้ว ให้สร้าง working copy ด้วย `git clone`; ห้าม `git init` ซ้ำแล้วสร้างประวัติอิสระในโฟลเดอร์เดียวกัน
- เผยแพร่ Source Code ของโครงการภายใต้ **Mozilla Public License 2.0 (MPL-2.0)** ตาม D21.2; การทำ Repository เป็น Public อย่างเดียวไม่ทดแทนการใส่ License และ Copyright Notice ที่ถูกต้อง
- ก่อน Public release ต้องตรวจ license ของ dependency, สร้าง SBOM และกำหนด policy สำหรับ source/binary redistribution, copyright notice และ third-party notices
- ห้าม commit runtime database, WAL/SHM, transaction/customer/employee data, backup, log จริง, private key, certificate secret, token, password, `.env`, signing key หรือ binary ภายนอกที่ไม่มีสิทธิ์แจกจ่าย
- repository ต้องมี sanitized example configuration และ test fixture แทนข้อมูลจริง; IP, hostname, credential และข้อมูลสาขาจริงต้องเป็น deployment configuration ภายนอก Git
- Development workspace หลักต้องอยู่บน non-system volume เช่น `D:\Dev\NicePOS` หรือ volume อื่นที่กำหนด ไม่ใช้ `C:\Downloads`, user temporary folder หรือ runtime deployment directory เป็น working repository หลัก
- Source code ห้าม hard-code drive letter; path ของ source, runtime data, log, certificate และ backup ต้องกำหนดผ่าน configuration/environment และรองรับ Windows/Linux
- ตำแหน่งติดตั้ง/runtime ต้องแยกจาก Git working copy เช่น source อยู่ `D:\Dev\NicePOS` ขณะที่ runtime data อยู่ใน volume/path ที่กำหนดสำหรับบริการ
- การวาง source บน `D:` ช่วยให้ติดตั้งระบบปฏิบัติการใหม่โดยไม่กระทบ working copy เมื่อ volume ยังสมบูรณ์ แต่ไม่ถือเป็น Backup หาก `C:` และ `D:` เป็น partition ของ physical disk ลูกเดียวกัน; source กู้จาก Git ได้ ส่วน runtime data ยังต้องใช้ RAID และ Local/Offsite Backup ตาม D12/NFR-05
- `main` ต้องเป็น protected branch; งานพัฒนาใช้ feature branch, Pull Request และ CI ที่ผ่านก่อน merge โดยห้ามเก็บ build artifact หรือข้อมูล runtime ใน Git history
