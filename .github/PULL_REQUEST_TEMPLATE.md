**What this changes, and why**

<!-- The why matters more than the what; the diff already says the what. -->

**How it was verified**

<!--
Not "tests pass" — which tests, and what would have caught the bug if the fix
were wrong? If it is a rendering change, a screenshot. If it is a performance
change, the numbers.
-->

**Checklist**

- [ ] `dotnet build` and `dotnet test` pass (warnings are errors)
- [ ] New behaviour is covered by a test that fails without the change
- [ ] If this fixes one site, its twins were checked (paste and drop, create
      and update, importer and renderer) and fixed or tested for parity too
- [ ] Public members are documented
- [ ] `CHANGELOG.md` updated if the change is user-visible
- [ ] `docs/architecture.md` is still accurate, or the discrepancy is called
      out here rather than edited away
