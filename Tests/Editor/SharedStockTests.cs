using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static Temnorid.Village.Tests.TestKit;

namespace Temnorid.Village.Tests
{
	/// <summary>The shared stock against a fake save: the write body, the field mask, the precondition, merging with Lords' own changes, the capacity clamp, retries.</summary>
	public class SharedStockTests
	{
		long _now;
		InMemoryProgressStore _store;
		SharedStock _stock;
		JObject _original;

		[SetUp]
		public void SetUp()
		{
			_now = T0;
			_store = new InMemoryProgressStore(LordsDoc());
			_original = (JObject)_store.Data.DeepClone();
			_stock = Open();
		}

		SharedStock Open()
		{
			var document = _store.LoadAsync().GetAwaiter().GetResult();
			Assert.IsTrue(document.IsUsableLordsSave);
			return SharedStock.ForLords(document.Progress, _store, () => _now);
		}

		SyncOutcome Sync() => _stock.SyncAsync(0).GetAwaiter().GetResult();

		[Test]
		public void The_stock_starts_as_saved_and_changes_only_by_what_the_game_does()
		{
			foreach (var id in Res.All) Assert.AreEqual(750, _stock.Get(id), 1e-9, id);
			Assert.AreEqual(5, _stock.Add("wood", 5));
			Assert.AreEqual(755, _stock.Count("wood"));
			Assert.IsTrue(_stock.Dirty);
			Assert.IsTrue(_stock.TrySpend("stone", 50));
			Assert.AreEqual(700, _stock.Count("stone"));
			Assert.AreEqual(0, _store.Writes.Count, "nothing is written until a sync");
		}

		[Test]
		public void An_addition_is_clamped_by_the_capacity_and_the_excess_is_not_added()
		{
			// Warehouse level 3: 800 * 1.45^3 = 2439, rounded to hundreds.
			Assert.AreEqual(2400, _stock.Capacity("wood"));
			Assert.AreEqual(1650, _stock.Add("wood", 5000), "room for 2400 - 750");
			Assert.AreEqual(2400, _stock.Count("wood"));
			Assert.AreEqual(0, _stock.Add("wood", 1), "full");
			// The well (level 1) holds 1200.
			Assert.AreEqual(1200, _stock.Capacity("water"));
			Assert.AreEqual(450, _stock.Add("water", 1000));
			Assert.AreEqual(0, _stock.Add("water", 0));
			Assert.AreEqual(0, _stock.Add("gold", 5));
		}

		[Test]
		public void Spending_takes_a_whole_cost_or_nothing()
		{
			var cost = new Amounts { ["wood"] = 100, ["stone"] = 800 };
			Assert.IsFalse(_stock.TrySpend(cost), "800 stone is not there");
			Assert.AreEqual(750, _stock.Count("wood"), "nothing was taken");
			Assert.IsFalse(_stock.Dirty);
			cost["stone"] = 700;
			Assert.IsTrue(_stock.TrySpend(cost));
			Assert.AreEqual(650, _stock.Count("wood"));
			Assert.AreEqual(50, _stock.Count("stone"));
			Assert.IsFalse(_stock.TrySpend("wood", -1));
		}

		[Test]
		public void A_write_patches_only_the_stock_with_the_document_time_as_precondition()
		{
			_stock.Add("wood", 20);
			_stock.TrySpend("clay", 30);
			_now = T0 + 1000;
			Assert.AreEqual(SyncOutcome.Saved, Sync());

			Assert.AreEqual(1, _store.Writes.Count);
			var write = _store.Writes[0];
			Assert.AreEqual("t1", write.UpdateTime, "the precondition is the time stamp that was read");
			Assert.IsTrue(write.Applied);
			CollectionAssert.AreEquivalent(new[] { "amounts", "at" }, write.Stock.Properties().Select(p => p.Name), "the body is {amounts, at}");
			Assert.AreEqual(T0 + 1000, (long)write.Stock["at"]);
			Assert.AreEqual(770, (double)write.Stock["amounts"]["wood"], 0.01, "770 plus a second of production");
			Assert.AreEqual(720, (double)write.Stock["amounts"]["clay"], 0.01);
			Assert.AreEqual(750, (double)write.Stock["amounts"]["iron"], 0.01);

			// Every other field of the document is exactly as it was.
			foreach (var property in _original.Properties().Where(p => p.Name != "stock"))
				Assert.IsTrue(JToken.DeepEquals(property.Value, _store.Data[property.Name]), property.Name + " was not touched");
			Assert.AreEqual(_original.Properties().Count(), _store.Data.Properties().Count());
			Assert.IsFalse(_stock.Dirty, "the change is now in the saved stock");
			Assert.AreEqual(770, _stock.Count("wood"));
			Assert.AreEqual(720, _stock.Count("clay"));
		}

		[Test]
		public void When_Lords_changed_the_stock_meanwhile_the_games_change_is_applied_to_Lords_stock()
		{
			_stock.Add("wood", 20);
			// In the browser the lord spent most of the wood, two hours before the game writes.
			_store.ExternalWrite(doc => doc["stock"]["amounts"]["wood"] = 500);
			_now = T0 + 2 * 3_600_000L;
			Assert.AreEqual(SyncOutcome.Saved, Sync());

			// Wood plots at levels 2 and 1 make 9 + 5 = 14 an hour: 500 + 28 + the game's 20.
			Assert.AreEqual(548, Wood(_store.Data), 1e-9);
			Assert.AreEqual(2, _store.Loads, "the document was read fresh for the write");
			Assert.AreEqual("t3", _store.UpdateTime, "Lords' change and ours each moved the time stamp");
		}

		[Test]
		public void A_loss_never_goes_below_zero_and_a_gain_stops_at_the_capacity()
		{
			Assert.AreEqual(0, SharedStock.Merge(100, -500, 800), "spent more than Lords has left");
			Assert.AreEqual(2400, SharedStock.Merge(2300, 500, 2400), "capped");
			Assert.AreEqual(2600, SharedStock.Merge(2600, 500, 2400), "already over the cap: kept, not added to");
			Assert.AreEqual(2350, SharedStock.Merge(2300, 50, 2400));

			// The same through a write: Lords raised wood to 2300 after the game added 500.
			_stock.Add("wood", 500);
			_store.ExternalWrite(doc => doc["stock"]["amounts"]["wood"] = 2300);
			Assert.AreEqual(SyncOutcome.Saved, Sync());
			Assert.AreEqual(2400, Wood(_store.Data), 1e-9);
		}

		[Test]
		public void A_conflict_reads_the_document_again_and_retries_with_the_same_change()
		{
			_stock.Add("stone", 40);
			var first = true;
			_store.BeforeSave = () =>
			{
				if (!first) return;
				first = false;
				_store.ExternalWrite(doc => doc["stock"]["amounts"]["stone"] = 100);
			};
			Assert.AreEqual(SyncOutcome.Saved, Sync());

			Assert.AreEqual(2, _store.Writes.Count);
			Assert.IsFalse(_store.Writes[0].Applied, "the first write met a changed document");
			Assert.IsTrue(_store.Writes[1].Applied);
			Assert.AreEqual("t1", _store.Writes[0].UpdateTime);
			Assert.AreEqual("t2", _store.Writes[1].UpdateTime, "the retry carries the new time stamp");
			Assert.AreEqual(140, Stone(_store.Data), 1e-9, "Lords' 100 plus the game's 40");
			Assert.IsFalse(_stock.Dirty);
		}

		[Test]
		public void A_save_that_keeps_changing_is_tried_a_few_times_then_the_change_is_kept_for_later()
		{
			_stock.Add("stone", 40);
			_store.BeforeSave = () => _store.ExternalWrite(doc => doc["stock"]["amounts"]["iron"] = 10);
			Assert.AreEqual(SyncOutcome.Failed, Sync());
			Assert.AreEqual(SharedStock.MaxAttempts, _store.Writes.Count);
			Assert.IsTrue(_store.Writes.All(w => !w.Applied));
			Assert.IsTrue(_stock.Dirty, "the change is not lost");
			Assert.AreEqual(790, _stock.Count("stone"));

			_store.BeforeSave = null;
			Assert.AreEqual(SyncOutcome.Saved, Sync());
			Assert.AreEqual(790, Stone(_store.Data), 1e-9);
		}

		[Test]
		public void Changes_made_while_a_write_is_in_flight_stay_for_the_next_one()
		{
			_stock.Add("wood", 10);
			_store.BeforeSave = () => _stock.Add("wood", 3);
			Assert.AreEqual(SyncOutcome.Saved, Sync());
			Assert.AreEqual(760, Wood(_store.Data), 1e-9, "only the first 10 were sent");
			Assert.IsTrue(_stock.Dirty);
			Assert.AreEqual(763, _stock.Count("wood"));
			_store.BeforeSave = null;
			Assert.AreEqual(SyncOutcome.Saved, Sync());
			Assert.AreEqual(763, Wood(_store.Data), 1e-9);
		}

		[Test]
		public void Nothing_is_written_when_the_save_is_missing_or_has_an_unexpected_shape()
		{
			_stock.Add("wood", 5);

			_store.Data["stock"]["amounts"]["clay"].Parent.Remove();
			Assert.AreEqual(SyncOutcome.Disabled, Sync(), "a resource is missing from the stock");
			Assert.IsFalse(_stock.Writable);
			Assert.AreEqual(0, _store.Writes.Count);
			StringAssert.Contains("unexpected shape", _stock.Status);
			Assert.AreEqual(SyncOutcome.Disabled, Sync(), "and it stays off");

			_store = new InMemoryProgressStore(LordsDoc());
			_stock = Open();
			_stock.Add("wood", 5);
			_store.Data["stock"] = "not a map";
			Assert.AreEqual(SyncOutcome.Disabled, Sync());
			Assert.AreEqual(0, _store.Writes.Count);

			_store = new InMemoryProgressStore(LordsDoc());
			_stock = Open();
			_stock.Add("wood", 5);
			_store.Data["stock"]["at"] = "yesterday";
			Assert.AreEqual(SyncOutcome.Disabled, Sync());
			Assert.AreEqual(0, _store.Writes.Count);

			_store = new InMemoryProgressStore(LordsDoc());
			_stock = Open();
			_stock.Add("wood", 5);
			_store.Data = null;
			Assert.AreEqual(SyncOutcome.Disabled, Sync(), "the document is gone: it is never created");
			Assert.AreEqual(0, _store.Writes.Count);
			StringAssert.Contains("gone", _stock.Status);
			Assert.AreEqual(755, _stock.Count("wood"), "the game still shows its own stock");
		}

		[Test]
		public void Offline_keeps_the_change_and_writes_nothing()
		{
			_stock.Add("wood", 5);
			_store.Offline = true;
			Assert.AreEqual(SyncOutcome.Failed, Sync());
			Assert.AreEqual(0, _store.Writes.Count);
			Assert.IsTrue(_stock.Dirty);
			Assert.IsTrue(_stock.Writable, "offline is not a reason to stop for good");
			StringAssert.Contains("Offline", _stock.Status);
			_store.Offline = false;
			Assert.AreEqual(SyncOutcome.Saved, Sync());
			Assert.AreEqual(755, Wood(_store.Data), 1e-9);
		}

		[Test]
		public void With_nothing_to_write_a_sync_only_reads_and_the_stock_follows_Lords()
		{
			_store.ExternalWrite(doc => doc["stock"]["amounts"]["iron"] = 123);
			Assert.AreEqual(SyncOutcome.Refreshed, Sync());
			Assert.AreEqual(0, _store.Writes.Count);
			Assert.AreEqual(123, _stock.Count("iron"));
		}

		[Test]
		public void Production_keeps_running_while_the_game_is_open()
		{
			_now = T0 + 3_600_000L;
			// Grain plots at levels 2 and 1 make 14 an hour; wood too.
			Assert.AreEqual(764, _stock.Get("wood"), 1e-9);
			Assert.AreEqual(764, _stock.Get("grain"), 1e-9);
			_stock.Add("wood", 1);
			Assert.AreEqual(765, _stock.Get("wood"), 1e-9);
		}

		[Test]
		public void Writes_are_coalesced_to_one_every_few_seconds()
		{
			Assert.IsTrue(_stock.DueForSync(0), "the first read is due at once");
			Assert.AreEqual(SyncOutcome.Refreshed, _stock.SyncAsync(10).GetAwaiter().GetResult());
			_stock.Add("wood", 1);
			Assert.IsFalse(_stock.DueForSync(12), "less than the write interval");
			Assert.IsTrue(_stock.DueForSync(10 + SharedStock.WriteInterval));
			Assert.AreEqual(SyncOutcome.Saved, _stock.SyncAsync(15).GetAwaiter().GetResult());
			Assert.IsFalse(_stock.DueForSync(20), "nothing pending: only an occasional read");
			Assert.IsTrue(_stock.DueForSync(15 + SharedStock.RefreshInterval));
		}

		[Test]
		public void A_local_stock_never_reaches_any_store_and_a_mirror_cannot_change()
		{
			var village = DefaultVillage.Create(T0);
			var local = SharedStock.ForLocal(village, DefaultVillage.StartAmounts(), () => T0);
			Assert.AreEqual(10, local.Add("wood", 10));
			Assert.AreEqual(760, local.Count("wood"));
			Assert.AreEqual(SyncOutcome.Disabled, local.SyncAsync().GetAwaiter().GetResult());
			Assert.IsFalse(local.DueForSync(1000));
			Assert.AreEqual(760, local.ToLocal()["wood"], 1e-9);

			var mirror = SharedStock.ForMirror(Amounts.Filled(900));
			Assert.AreEqual(0, mirror.Add("wood", 5));
			Assert.IsFalse(mirror.TrySpend("wood", 1));
			mirror.SetMirrored("wood", 321);
			Assert.AreEqual(321, mirror.Count("wood"));
			Assert.AreEqual(900, mirror.Capacity("wood"));
			Assert.AreEqual(SyncOutcome.Disabled, mirror.SyncAsync().GetAwaiter().GetResult());
		}
	}
}
