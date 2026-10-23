---
testId: AGBM_300
module: AgentBridge.ReleaseGovernance
testType: EngineeringVerifier
target: Prepared consumer preimage restoration and owned process-job cleanup
testFile: Tools/AgentBridge/Fixtures/RuntimeReleaseDiscoveryProbe.py
testMethod: PreparedMutation.prepare/restore; start_owned/finish_owned
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_300

## Requirement
Use the existing fixed B under the shared A/B lease for local candidate consumer verification, with no new Unity project or Library copying/deletion. Restore exact manifest, lockfile and fixture preimages only after owned processes are terminated. Candidate fixture evidence cannot certify a production remote tag or authorize publication.

## Risk
Incomplete original-file capture could cause restoration to delete unbacked existing files. A timed-out Unity/source-builder parent can leave descendants accessing the prepared project unless owned process-tree cleanup is confirmed.

## Steps
Capture complete preimages before activating mutation restoration. Review the Windows suspended child -> job assignment -> resume sequence and job cleanup. Extract only these helper functions via Python AST and exercise normal parent exit with a living child and timeout of a living parent/child, using owned short-lived Python processes and small temporary files only. Subsequently run the explicit prepared candidate mode against fixed B under the pair lease.

## Assertions
No cleanup runs for incomplete preimage capture. Normal execution and timeout terminate owned descendants; failed cleanup blocks project restoration. Prepared mode uses absent isolated UPM/machine/source-build cache roots and retains the existing Library without claiming a cold Library. All nine production identity/discovery/build/install/offline/startup gates remain intact; candidate fixture results remain engineering-only.

## Latest Run Result
2026-10-23: independent static review and local owned-process smoke checks passed on 64-bit Windows Python. Normal-parent-exit used owned PIDs 20668/30424; timeout-parent used 25824/33516. Both original process handles eventually signaled and unsafe_process_cleanup remained false. The normal-exit case observed a brief child-handle WAIT_TIMEOUT immediately after job active count reached zero, followed by an immediate successful bounded handle wait; this was not job escape. Accounting structure is 48 bytes and HANDLE arguments preserve pointer width. Python and PowerShell AST checks passed. Preimage activation now occurs only after complete prepare succeeds. No Unity or build was run by the reviewer.

Root prepared local 1.5.0-rc.1 candidate consumer verification passed: actual Unity discovery, CLI and Roslyn source build, installation, offline reuse, bad-checksum rejection and runtime startup all completed; all nine checks are true. consumer-summary.json records fixture=true and isolated source fixture commit 3698e8bddacc685df9027abfb67c33355730547a, prepared=true, projectsCreated=0, Library retained, and 9 exact file preimages restored. Stable evidence is collected under workbench Documentation~/AgentBridge/acceptance/release-1.5.0-rc.1-local-20261023/LocalConsumer/. GitHub remote tag/release authentication and the production remote gate remain pending; this fixture result does not certify a published tag or authorize publication.

## Boundaries
The legacy non-prepared entry remains for compatibility; this mainline must explicitly select prepared fixed B. Finally-based owned-job cleanup covers normal/error/timeout paths. At review time the job did not set KILL_ON_JOB_CLOSE, so abrupt coordinator termination was not certified by these checks. Neither a 32-bit Python runtime nor real Unity/build behavior is certified by the helper-only smoke check.