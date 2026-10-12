# Migrate the temporary checkout to the internal NVMe

Use this procedure after the new internal NVMe has been installed, formatted as NTFS, protected as required, and assigned a stable drive letter.

1. Commit and push every intended change from `C:\POS`.
2. Confirm `git status --short --branch` is clean.
3. Clone the repository from its GitHub URL into the new NVMe, for example `P:\POS`.
4. Run the complete .NET and frontend safe test suites from the new clone.
5. Confirm the new clone has the expected remote, branch, and commit.
6. Update IDE, terminal, service-development, and backup paths to the new clone.
7. Keep `C:\POS` unchanged until the new clone passes validation and at least one backup exists.
8. Remove the temporary checkout only after explicit approval.

Do not copy or move `.git` manually. A fresh clone detects missing, ignored, or unpushed files and avoids carrying stale absolute paths into the permanent checkout.

