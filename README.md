# temnorid-village

A Temnorid lord's **village and farm** for any game. It reads the player's [Lords](https://lords.temnorid.com) save, or builds a standard default village, ports Lords' stock rules exactly, shares the stock with Lords, and draws the land in 3D with Unity primitives.

Package id `com.itkamianets.temnorid-village`, assembly `Temnorid.Village`, Unity 6000.6. It needs `com.unity.nuget.newtonsoft-json` and the package `3d-unity-account` (Firebase sign-in and the Firestore client).

## What it holds

| Part | What it does |
| --- | --- |
| `LordsProgress` and friends (`Models.cs`) | C# mirror of the Firestore document `users/{uid}/progress/lords`: lord name, `stock {amounts, at}`, `village {seed, layout, slots[], buildings{}}`, `farm {seed, layout, plots[], levels{}}`. Unknown fields are ignored, missing ones tolerated. |
| `LordsRules` | Lords' economy rules, ported exactly from `building.timing.ts` and `economy.ts`: `LevelAt`, `RunningUpgrade`, `WorkEndsAt`, `TargetLevel`, `PlotProduction`, `ProductionPerHour`, `Capacity`, `StockAt`. Time is epoch milliseconds. Tested with numbers computed by node from the TypeScript. |
| `IProgressStore` | `LoadAsync()` reads the save fresh; `SaveStockAsync(stock, updateTime)` writes the stock. `FirestoreProgressStore` is the real one, `InMemoryProgressStore` a fake for tests. |
| `VillageSource` | Decides which village to show: signed in with Google and a usable Lords save, then that save (`IsLords = true`); anything else (guest, signed out, offline, no save, a refusal, a timeout), then the standard village, with the reason in `Status`. Never throws, never waits longer than its timeout (10 s). |
| `DefaultVillage` | The standard village, built in code: 12 places on an ellipse, the lord's hall in the centre, warehouse 3, granary 2, well 1 and a few more buildings, a farm of 12 plots (two per resource, levels 2 and 1), stock 750 of each. |
| `SharedStock` | The six stocks as a game sees them, with `Add` (clamped by capacity), `TrySpend`, and `SyncAsync` that writes back to Lords. |
| `StockSyncer` | A component that runs the sync: at most one write every 5 s, a read now and then, and a flush on pause and quit. |
| `VillageLayout`, `VillageData` | Turn a save into plain data in metres at one moment (levels at `now`). It is JSON, so an online host can send it to guests. |
| `VillageView` | A component that draws a `VillageData` with primitives and lets the game ask where things are. |

## How a game uses it

```csharp
// 1. Which village? (the account is the signed-in GameAccount of 3d-unity-account)
VillageSession session = await VillageSource.ForAccount().LoadAsync(localStock);   // localStock: the game's own saved stock, or null

// 2. Draw it. The view lives in the scene; set its two materials in the scene builder so the shaders are in the build.
view.Build(session.Data);
view.PlotById(3);              // PlotHandle: Root (with a trigger box), Visual (decoration a game may hide), Data
view.BuildingBySlot(1);        // the Transform of the building on place 1 (0 is the centre)
view.StorePosition;            // world position of the lord's stores (first warehouse, else the hall, else the centre)
view.SpawnPosition;            // a free spot near the stores

// 3. Use the stock. The same stock Lords has.
session.Stock.Count("wood");           // what Lords would show now (production keeps running)
session.Stock.Capacity("wood");        // what the stores hold at most
session.Stock.Add("wood", 5);          // returns how many fitted under the capacity; the excess is not added
session.Stock.TrySpend("stone", 20);
gameObject.AddComponent<StockSyncer>().Stock = session.Stock;   // only when session.Stock.IsShared
```

For an online game the host sends `session.Data.ToJson()` to its guests; a guest builds `VillageSession.ForClient(VillageData.FromJson(json))`, whose stock mirrors what the host reports (`SetMirrored`) and cannot change anything.

For a guest account the stock is *local*: the game keeps `session.Stock.ToLocal()` in its own save and passes it back to `LoadAsync` next time. A local stock never reaches any store and does not produce.

## What is written to Lords: only `stock`

Craft changes the stock and nothing else. Lords stays authoritative for construction, farm levels and everything else.

- The amount the player sees is `stockAt(saved, now)` computed locally plus the game's net change since its last write (`Add` and `TrySpend` change only that net change).
- A write reads the document fresh and takes its `updateTime`; recomputes the stock Lords has now (Lords may have spent resources in the browser meanwhile); applies the net change (a gain stops at the capacity, a loss at 0); and patches **only the field `stock`** (`{amounts, at: now}`) with `updateMask.fieldPaths=stock` and the precondition `currentDocument.updateTime`. If the document changed in between, it reads again and retries (4 attempts), then keeps the change for later.
- It never creates the document, never writes another field, never writes for a guest, and writes nothing when the document is missing or `stock` is not exactly the shape Lords writes (all six resources as numbers, numeric `at`); then sharing is switched off for the session and the game goes on with its own stock.
- Writes are coalesced (at most one every 5 s). A hard quit can lose the last few seconds of changes.
- Lords' own `updatedAt` is not touched. Lords reads the saved stock and works out production from `at`, so a Craft write simply becomes a new moment of the same stock.

### Firestore rules

Nothing to change: the rules in `temnorid.com` already let the signed-in owner read and write `users/{uid}/progress/lords`. The ID token comes from `GameAccount.GetIdTokenAsync()`.

## Scale and layout

Lords draws the village on a 1800 x 1000 map and the farm on another 1800 x 1000 map where the village is a small blob in the middle, so they cannot share one scale. The village uses 0.04 m per map unit (the 170-unit spacing of its places becomes 6.8 m, a village of about 50 x 40 m), the farm 0.1 m per unit (a ring of plots about 125 x 50 m round the village). The village centre is the origin, +x is east, +z north. Approximations (documented in `VillageLayout`): the village centre is the middle of its places, the farm centre the middle of the farm map (Lords' small random nudge is ignored), layout 1 villages are stretched like Lords does, places bought later get a ring spot, and trees and rocks are scattered from the seed with a simple generator (not Lords' own).

Resource colours are Lords': wood `#3f6b2f`, stone `#7a746b`, iron `#8a4b33`, water `#2f78a0`, grain `#b8892a`, clay `#a0613a`.

## Safety

Tokens and uids are never logged or put in a status text. Tests never reach the real Firestore: they use `InMemoryProgressStore` and a fake sender for the Firestore client.

## Status

v0.1: models, rules, stores, source, default village, shared stock, view. 45 edit-mode tests (rules against node-computed numbers, the shared stock against a fake save, the source, the default village, the layout, the view).
