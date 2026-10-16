
#### D21.3 — Public-release Checklist — ยืนยัน

- ก่อนเปลี่ยน Repository เป็น Public ต้องตรวจ Secret ทั้ง Working Tree และ Git History รวมถึง Token, Password, `.env`, Private Key, Certificate Key และ Signing Key; หากข้อมูลลับเคยถูก Commit ต้องลบออกจาก History และ Rotate Credential เดิมก่อนเปิด Public
- ห้ามมีข้อมูลลูกค้า พนักงาน ธุรกรรม ยอดขาย Production Database, Backup, Runtime Log, IP, Hostname หรือข้อมูลสาขาจริง; Test Fixture และ Example Configuration ต้องเป็นข้อมูลจำลองหรือผ่านการทำให้ไม่สามารถระบุตัวจริงได้
- ต้องมี `LICENSE` ฉบับ MPL 2.0, License Notice/SPDX ที่ตรวจสอบได้, `THIRD_PARTY_NOTICES` และผลตรวจ License ของ Dependency
- ต้องสร้าง SBOM ที่ผูกกับ Release Artifact ของ PC POS, Branch Service, Central Service, PrintGateway, Android, iOS และ Container Image ตามองค์ประกอบที่ Release จริง
- ทุกองค์ประกอบต้อง Build ได้จาก Clean Clone โดยไม่พึ่งไฟล์เฉพาะในเครื่องผู้พัฒนา; Android/iOS Build ห้ามรวม Signing Key และ Configuration ตัวอย่างต้องไม่มี Credential จริง
- Unit, Contract, Integration และ End-to-End Test ที่เกี่ยวข้องต้องผ่านก่อน Release; Hardware Test ต้องแยกจาก CI ปกติและห้ามติดต่อเครื่องพิมพ์หรือลิ้นชักเงินจริงโดยปริยาย
- ต้องทำ Dependency, Source, Secret และ Container Scan; ช่องโหว่ระดับ Critical/High ต้องได้รับการแก้ไข หรือมี Risk Acceptance พร้อมเหตุผล เจ้าของความเสี่ยง มาตรการควบคุม และกำหนดแก้ไขก่อน Release
- เอกสารขั้นต่ำประกอบด้วย `README.md`, `SECURITY.md`, `CONTRIBUTING.md`, วิธีติดตั้ง/พัฒนา, Architecture, ขอบเขต Component และช่องทางรายงานช่องโหว่แบบไม่เปิดเผยต่อสาธารณะก่อนแก้ไข
- `main` ต้องมี Branch Protection; Merge ผ่าน Pull Request และ CI พร้อมกำหนดผู้อนุมัติสำหรับ Contract, Payment, Security และ Printing ตามความเสี่ยง
- แต่ละ Component มี Version ของตนเอง และ Release ต้องมี Changelog, Checksum, Migration Note (ถ้ามี) รวมถึงการเชื่อมโยงกลับไปยัง Commit และ SBOM ที่ใช้สร้าง Artifact
- License ของเอกสาร รูปภาพ ตัวอย่างข้อมูล ชื่อโครงการ โลโก้ และเครื่องหมายการค้าให้กำหนดแยกใน D21.4
- การอนุมัติเปิด Public ต้องบันทึก Checklist, ผู้ตรวจ, วันที่, Commit SHA และผลการตรวจให้ตรวจย้อนหลังได้ ก่อนเปลี่ยน Visibility ของ Repository
