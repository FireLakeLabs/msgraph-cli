# Phase 4 Validation Report

Date: 2026-03-24

## Scope

Validated the Phase 4 Excel and document features in the current `msgraph-cli` implementation using:

- Live Microsoft Graph auth from the current session
- Live OneDrive assets discovered during the session
- Direct CLI execution against the current source tree
- Targeted phase 4 unit tests

Live assets used:

- Excel workbook: `test-phase4.xlsx` (`01UPEVY6E6PQ2I22FVSFGZLH2VPR4GGMEY`)
- Excel workbook with table for append validation: `Book.xlsx` (`012MBENSIG6JSJUPCYVZFK6BBB4N2IQCG5`)
- Word document: `test-phase4.docx` (`01UPEVY6B7F6EYD2AMLVEYCZHJS7JBMNHJ`)
- PowerPoint document: none found during validation

Auth state at test time:

- `isAuthenticated = true`
- `userEmail = miranda.redpath@firelakelabs.com`
- Granted scopes included `Files.Read` and `Files.ReadWrite`

## What Passed

The following behaviors worked during live validation:

- `excel --help`
- `docs --help`
- `excel sheets --help`
- `excel get --help`
- `excel update --help`
- `excel append --help`
- `docs export --help`
- `docs cat --help`
- `--read-only` correctly blocked `excel update` with exit code `10`
- `--read-only` correctly blocked `excel append` with exit code `10`
- `--dry-run excel update` produced the expected no-op output
- `--dry-run excel append` produced the expected no-op output
- Live `excel sheets` returned `Sheet1`
- Live `excel get` returned the expected workbook data from `Sheet1!A1:D10`
- Live `excel update` succeeded for `Sheet1!Z1`
- Live `excel append` succeeded against `Book.xlsx` after the workbook table schema was aligned with the 3-value payload
- Live `docs export` succeeded and wrote a PDF of `62422` bytes
- Live `docs cat` succeeded in both text and `--json` modes
- Relevant unit tests passed: `ExcelServiceTests`, `DocumentServiceTests`, and `CommandGuardTests` (`69` tests total)

## Findings

### 1. The phase 4 validation script does not run a live `excel append` test

Severity: medium

The script is intended to validate Phase 4 completion, but the live Excel section never executes `excel append`. It only runs live `excel sheets`, `excel get`, `excel update`, and `excel sheets (beta)`.

Evidence:

- `scripts/test-phase4.sh:144` runs live `excel update`
- `scripts/test-phase4.sh:149` runs live `excel sheets (beta)`
- There is no corresponding live `run_test "excel append ..."` entry in the Excel live section
- The only `excel append` coverage in the script is help output and dry-run coverage at `scripts/test-phase4.sh:101` and `scripts/test-phase4.sh:125`

Impact:

- A key Phase 4 write path can be broken in production while the script still reports success.

Repro steps:

1. Set a valid `EXCEL_ITEM_ID` and run `./scripts/test-phase4.sh`.
2. Observe the `PHASE 4 TEST: EXCEL OPERATIONS` section.
3. Confirm that no live `excel append` command is executed.

### 2. The original workbook fixture `test-phase4.xlsx` is not suitable for append validation

Severity: medium

The originally discovered workbook `test-phase4.xlsx` does not contain any OOXML table definitions. A live append attempt against `Table1` fails with Graph `ItemNotFound`, but this turned out to be a fixture problem rather than an `excel append` implementation problem.

Observed command:

```bash
dotnet run --project src/MsGraphCli -- --json excel append 01UPEVY6E6PQ2I22FVSFGZLH2VPR4GGMEY --sheet Sheet1 --table Table1 --values '[["Charlie",91,"A-"]]'
```

Observed result:

```json
{
  "error": {
    "code": "ItemNotFound",
    "message": "The requested resource doesn't exist."
  }
}
```

Verification of workbook contents:

- Downloaded the workbook and inspected the OOXML package
- Archive contained `xl/workbook.xml`, `xl/worksheets/sheet1.xml`, and `xl/_rels/workbook.xml.rels`
- No `xl/tables/` entries were present

Follow-up validation outcome:

- Downloaded `Book.xlsx` from the authenticated account and confirmed it contains `xl/tables/table1.xml`
- Confirmed `Table1` was first defined as a 1-column table, which caused a dimension mismatch for a 3-value append payload
- After the table was expanded to 3 columns, the original append payload succeeded

Successful append command:

```bash
dotnet run --project src/MsGraphCli -- --json excel append 012MBENSIG6JSJUPCYVZFK6BBB4N2IQCG5 --sheet Sheet1 --table Table1 --values '[["Charlie",91,"A-"]]'
```

Successful result:

```json
{
  "status": "appended",
  "table": "Table1",
  "rowsAdded": 1
}
```

Impact:

- The original workbook fixture is insufficient to validate the append path.
- The append implementation itself is working when the workbook contains a real table whose column count matches the payload.

Repro steps:

1. Run:

   ```bash
   dotnet run --project src/MsGraphCli -- --json excel append 01UPEVY6E6PQ2I22FVSFGZLH2VPR4GGMEY --sheet Sheet1 --table Table1 --values '[["Charlie",91,"A-"]]'
   ```

2. Observe the `ItemNotFound` response.
3. Run:

   ```bash
   dotnet run --project src/MsGraphCli -- drive download 01UPEVY6E6PQ2I22FVSFGZLH2VPR4GGMEY --out /tmp/test-phase4-workbook.xlsx
   unzip -l /tmp/test-phase4-workbook.xlsx | grep -i 'table\|sheet\|workbook'
   ```

4. Confirm there are no `xl/tables/*` entries in the workbook archive.
5. Compare with a table-backed workbook such as `Book.xlsx`, where `Table1` exists and append succeeds once the table has 3 columns.

### 3. Live PowerPoint coverage is still missing

Severity: low

The code advertises `docs cat` support for PowerPoint, and unit coverage exists for `.pptx` extraction, but there was no live `.pptx` file available in the current OneDrive account to validate that path end-to-end.

Observed command:

```bash
dotnet run --project src/MsGraphCli -- --json drive search pptx -n 10
```

Observed result:

```json
{
  "items": [],
  "query": "pptx"
}
```

Impact:

- Phase 4 PowerPoint extraction is only unit-tested in this session, not live-validated.

Repro steps:

1. Run `dotnet run --project src/MsGraphCli -- --json drive search pptx -n 10`.
2. Observe that no `.pptx` files are returned.
3. Without a `PPTX_ITEM_ID`, the live PowerPoint path remains untested.

## Overall Assessment

Phase 4 is mostly working for:

- Excel worksheet discovery
- Excel range reads
- Excel range writes
- Excel table row append when the target table exists and matches the payload shape
- Document export
- Document content extraction for `.docx`
- Guardrails and dry-run behavior

The main remaining gaps are in validation completeness, not broad feature failure:

- The bundled phase 4 script does not actually live-test `excel append`
- The original `test-phase4.xlsx` fixture is not capable of validating append
- Live `.pptx` coverage is absent in the current account