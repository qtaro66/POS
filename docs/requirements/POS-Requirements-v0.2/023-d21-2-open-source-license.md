
#### D21.2 — Open-source License — ยืนยัน

- Source Code ที่โครงการเป็นเจ้าของใน Monorepo ใช้ **Mozilla Public License 2.0 (MPL-2.0)**
- Repository ต้องมีไฟล์ `LICENSE` ซึ่งใช้ข้อความ MPL 2.0 ฉบับทางการครบถ้วนโดยไม่ดัดแปลง และระบุ SPDX Identifier เป็น `MPL-2.0`
- ไฟล์ Source ที่อยู่ภายใต้ License ต้องมี License Notice ตาม Exhibit A หรือรูปแบบ Header/REUSE ที่ให้ผลเทียบเท่าและตรวจสอบอัตโนมัติได้
- เมื่อแจกจ่าย Executable หรือ Source ที่แก้ไขไฟล์ซึ่งอยู่ภายใต้ MPL ผู้แจกจ่ายต้องเปิดเผย Source ของไฟล์ที่อยู่ภายใต้ MPL และไฟล์ที่แก้ไขตามเงื่อนไขของ MPL; ขอบเขต Copyleft อยู่ในระดับไฟล์ ไม่บังคับให้ไฟล์ใหม่ทั้งหมดใน Larger Work ต้องใช้ MPL
- Code จากบุคคลภายนอกยังคงใช้ License เดิมของเจ้าของสิทธิ์ ห้ามเปลี่ยนเป็น MPL โดยไม่มีสิทธิ์ และต้องบันทึกไว้ใน `THIRD_PARTY_NOTICES` กับ SBOM
- ก่อน Merge Dependency ใหม่ ต้องตรวจ License Compatibility กับ MPL-2.0 รวมถึงข้อจำกัดในการแจกจ่ายบน Windows, Linux, Android และ iOS
- Build Artifact และ Release Package ต้องแนบหรือชี้ไปยัง License Notice และวิธีเข้าถึง Corresponding Source ตามที่ MPL กำหนด
- MPL-2.0 ไม่ให้สิทธิ์ใช้ชื่อโครงการ โลโก้ หรือเครื่องหมายการค้าโดยอัตโนมัติ; Policy ของชื่อและเครื่องหมายการค้าต้องกำหนดแยกก่อน Public Release
- License ของเอกสาร รูปภาพ ตัวอย่างข้อมูล และทรัพย์สินที่ไม่ใช่ Source Code ต้องกำหนดแยกใน Public-release Checklist; ห้ามถือว่า MPL ครอบคลุมโดยอัตโนมัติโดยไม่มีประกาศชัดเจน
