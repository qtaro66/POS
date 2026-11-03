# PC POS Branch readiness

D18.10 uses an explicit, authenticated Branch response to show that the service answered and whether its configured Ordering database passed the existing read-only schema probe. The timestamp records probe completion. A failed response leaves both observations unknown. A successful probe checks schema accessibility, with overall health, write readiness, backup, and checkpoint observations outside its scope.
