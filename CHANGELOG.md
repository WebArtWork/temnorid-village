# Changelog

## 0.1.0

- Models of the Lords save (`LordsProgress`, stock, village, farm, construction).
- `LordsRules`: Lords' level, production, capacity and stock formulas, ported exactly and tested against numbers computed from the TypeScript.
- `IProgressStore` with a Firestore store (patches only `stock`, with an `updateTime` precondition) and an in-memory fake.
- `VillageSource`: the player's Lords village for a Google account with a save, the standard village otherwise, with a reason.
- `DefaultVillage`: a deterministic standard village and farm, stock 750 of each.
- `SharedStock` and `StockSyncer`: the shared Lords stock, clamped by capacity, merged with Lords' own changes, written back at most every 5 s.
- `VillageLayout`, `VillageData` and `VillageView`: a graybox 3D village and farm drawn from plain data.
