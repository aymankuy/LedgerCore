# Azure Data Factory pipeline

`PL_LoadWarehouse` is the cloud counterpart of the SSIS package in `data/ssis`: it copies the safe
window of ledger entries and the account status history into the warehouse staging tables, then
calls the same `etl.usp_Load*` procedures (see ADR 0012). The files are in the layout Data Factory
uses for Git integration: `linkedService/`, `dataset/`, `pipeline/`, `trigger/`.

Both linked services sign in with the factory's system-assigned managed identity, so no password or
key exists anywhere. The identity needs a database user in each database:

- ledger: `EXECUTE` on `usp_GetSafeEntryWatermark`, `usp_ExtractLedgerEntries` and `usp_ExtractAccountStatusHistory`
- warehouse: `EXECUTE`, `SELECT`, `INSERT` on schema `etl`, and `SELECT`, `INSERT`, `ALTER` on schema `stg`

Deploy with the Azure CLI (`az extension add -n datafactory`), after creating the factory
and applying `data/warehouse/*.sql` to the warehouse database. `--properties` takes the content of
each file's `properties` object:

```powershell
az datafactory create -g <rg> --factory-name <factory> --location <region>
# for every file in linkedService/, dataset/ (in that order):
az datafactory linked-service create -g <rg> --factory-name <factory> --name <name> --properties "@<properties.json>"
az datafactory dataset create        -g <rg> --factory-name <factory> --name <name> --properties "@<properties.json>"
az datafactory pipeline create       -g <rg> --factory-name <factory> --name PL_LoadWarehouse --pipeline "@<properties.json>"
az datafactory pipeline create-run   -g <rg> --factory-name <factory> --name PL_LoadWarehouse
```

`TR_Nightly` is left stopped on purpose. The factory used for the recorded run was deleted
afterwards; see ADR 0012 for the result.
