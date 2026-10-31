using System;
using System.Linq;
using System.Threading.Tasks;
using ITKamianets.Engine.Account;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static Temnorid.Village.Tests.TestKit;

namespace Temnorid.Village.Tests
{
	public class VillageSourceTests
	{
		// Run off the editor's main thread: a load that hits its timeout continues on the thread pool, and waiting for it here must not need the main thread.
		static VillageSession Load(IProgressStore store, bool signedIn, bool guest, Amounts local = null, TimeSpan? timeout = null)
			=> Task.Run(() => new VillageSource(store, new FakeAccount(signedIn, guest), () => T0).LoadAsync(local, timeout)).GetAwaiter().GetResult();

		[Test]
		public void A_google_player_with_a_lords_save_gets_that_village_and_the_shared_stock()
		{
			var store = new InMemoryProgressStore(LordsDoc());
			var session = Load(store, true, false);
			Assert.IsTrue(session.IsLords);
			Assert.IsTrue(session.Data.IsLords);
			Assert.AreEqual("Ann", session.LordName);
			Assert.AreEqual("Ann's village", session.Title);
			Assert.AreEqual(StockMode.Lords, session.Stock.Mode);
			Assert.IsTrue(session.Stock.IsShared);
			Assert.AreEqual(12, session.Data.Plots.Count);
			Assert.AreEqual(750, session.Stock.Count("wood"));
			Assert.AreEqual(1, store.Loads);
			Assert.AreEqual(0, store.Writes.Count, "loading never writes");
		}

		[Test]
		public void A_guest_and_a_signed_out_player_get_the_default_village_without_touching_the_save()
		{
			var store = new InMemoryProgressStore(LordsDoc());
			var guest = Load(store, true, true);
			Assert.IsFalse(guest.IsLords);
			Assert.AreEqual(StockMode.Local, guest.Stock.Mode);
			StringAssert.Contains("Guest", guest.Status);
			Assert.AreEqual("Standard village", guest.Title);

			var signedOut = Load(store, false, false);
			Assert.IsFalse(signedOut.IsLords);
			StringAssert.Contains("Not signed in", signedOut.Status);
			Assert.AreEqual(0, store.Loads, "the save is not even read");
		}

		[Test]
		public void The_default_stock_starts_at_the_games_own_amount_when_it_has_one()
		{
			var own = Amounts.Filled(100);
			own["wood"] = 321;
			var session = Load(null, true, true, own);
			Assert.AreEqual(321, session.Stock.Count("wood"));
			Assert.AreEqual(100, session.Stock.Count("clay"));
			Assert.AreEqual(750, Load(null, false, false).Stock.Count("clay"), "Lords' start stock otherwise");
		}

		[Test]
		public void Offline_a_missing_save_a_save_not_ready_or_a_broken_one_all_fall_back_to_the_default_with_a_reason()
		{
			var offline = Load(new InMemoryProgressStore(LordsDoc()) { Offline = true }, true, false);
			Assert.IsFalse(offline.IsLords);
			StringAssert.Contains("Offline", offline.Status);

			var missing = Load(new InMemoryProgressStore(null), true, false);
			Assert.IsFalse(missing.IsLords);
			StringAssert.Contains("No Lords save", missing.Status);

			var noVillage = LordsDoc();
			noVillage.Remove("village");
			var notReady = Load(new InMemoryProgressStore(noVillage), true, false);
			Assert.IsFalse(notReady.IsLords);
			StringAssert.Contains("not ready", notReady.Status);

			var noFarm = LordsDoc();
			noFarm.Remove("farm");
			Assert.IsFalse(Load(new InMemoryProgressStore(noFarm), true, false).IsLords);

			var noStock = LordsDoc();
			noStock.Remove("stock");
			Assert.IsFalse(Load(new InMemoryProgressStore(noStock), true, false).IsLords, "no stock: nothing to share");

			var broken = LordsDoc();
			broken["village"] = "garbage";
			var unreadable = Load(new InMemoryProgressStore(broken), true, false);
			Assert.IsFalse(unreadable.IsLords);
			StringAssert.Contains("could not be read", unreadable.Status);
		}

		sealed class Throwing : IProgressStore
		{
			public Task<ProgressDocument> LoadAsync() => throw new InvalidOperationException("boom with a uid in it");
			public Task<StockWriteResult> SaveStockAsync(JObject stock, string updateTime) => throw new InvalidOperationException();
		}

		sealed class Never : IProgressStore
		{
			public Task<ProgressDocument> LoadAsync() => new TaskCompletionSource<ProgressDocument>().Task;
			public Task<StockWriteResult> SaveStockAsync(JObject stock, string updateTime) => Task.FromResult(StockWriteResult.Failed);
		}

		[Test]
		public void An_exception_or_a_save_that_never_answers_never_blocks_the_game()
		{
			var thrown = Load(new Throwing(), true, false);
			Assert.IsFalse(thrown.IsLords);
			StringAssert.DoesNotContain("uid", thrown.Status, "the message names the kind of problem, not the text of it");

			var slow = Load(new Never(), true, false, null, TimeSpan.FromMilliseconds(50));
			Assert.IsFalse(slow.IsLords);
			StringAssert.Contains("too long", slow.Status);
		}

		[Test]
		public void A_guest_of_an_online_game_shows_the_hosts_village_and_mirrors_its_stock()
		{
			var data = VillageSource.CreateDefault(null, "x", T0).Data;
			var guest = VillageSession.ForClient(VillageData.FromJson(data.ToJson()));
			Assert.AreEqual(StockMode.Mirror, guest.Stock.Mode);
			Assert.AreEqual(2400, guest.Stock.Capacity("wood"), "the capacity travels with the village");
			Assert.IsNull(guest.Progress, "a guest has no save of its own");
		}

		[Test]
		public void The_firestore_store_patches_only_the_stock_with_the_precondition_and_never_without_one()
		{
			FirestoreRequest last = null;
			var calls = 0;
			FirestoreResponse next = new FirestoreResponse { Status = 200, Text = "{\"name\":\"n\",\"updateTime\":\"U2\"}" };
			var client = new FirestoreClient(() => "temnorid-arena", () => Task.FromResult("tok"), r => { last = r; calls++; return Task.FromResult(next); });
			var store = new FirestoreProgressStore(client, () => "uid1");
			var stock = new JObject { ["amounts"] = Amounts.Filled(5).ToJson(), ["at"] = T0 };

			Assert.AreEqual(StockWriteResult.Saved, store.SaveStockAsync(stock, "2026-10-31T10:00:00.123456Z").GetAwaiter().GetResult());
			Assert.AreEqual("PATCH", last.Method);
			StringAssert.Contains("/users/uid1/progress/lords?updateMask.fieldPaths=stock&currentDocument.updateTime=", last.Url);
			StringAssert.DoesNotContain("updateMask.fieldPaths=village", last.Url);
			var body = JObject.Parse(last.Body);
			CollectionAssert.AreEqual(new[] { "stock" }, ((JObject)body["fields"]).Properties().Select(p => p.Name).ToArray(), "only the stock is in the body");
			Assert.AreEqual(5d, (double)body["fields"]["stock"]["mapValue"]["fields"]["amounts"]["mapValue"]["fields"]["wood"]["doubleValue"]);
			Assert.AreEqual(T0.ToString(), (string)body["fields"]["stock"]["mapValue"]["fields"]["at"]["integerValue"]);

			calls = 0;
			Assert.AreEqual(StockWriteResult.Failed, store.SaveStockAsync(stock, "").GetAwaiter().GetResult(), "no precondition, no write");
			Assert.AreEqual(StockWriteResult.Failed, new FirestoreProgressStore(client, () => "").SaveStockAsync(stock, "t").GetAwaiter().GetResult(), "signed out");
			Assert.AreEqual(0, calls, "nothing was sent");

			next = new FirestoreResponse { Status = 400, Text = "{\"error\":{\"status\":\"FAILED_PRECONDITION\"}}" };
			Assert.AreEqual(StockWriteResult.Conflict, store.SaveStockAsync(stock, "t").GetAwaiter().GetResult());
			next = new FirestoreResponse { Status = 404, Text = "{\"error\":{\"status\":\"NOT_FOUND\"}}" };
			Assert.AreEqual(StockWriteResult.Failed, store.SaveStockAsync(stock, "t").GetAwaiter().GetResult(), "a missing document is never created");
			next = new FirestoreResponse { Status = 403, Text = "{\"error\":{\"status\":\"PERMISSION_DENIED\"}}" };
			Assert.AreEqual(StockWriteResult.Failed, store.SaveStockAsync(stock, "t").GetAwaiter().GetResult());

			// Reading: a document, a missing document, an error.
			next = new FirestoreResponse { Status = 200, Text = "{\"name\":\"n\",\"updateTime\":\"U9\",\"fields\":{\"lord\":{\"mapValue\":{\"fields\":{\"name\":{\"stringValue\":\"Ann\"}}}}}}" };
			var read = store.LoadAsync().GetAwaiter().GetResult();
			Assert.IsTrue(read.Exists);
			Assert.AreEqual("U9", read.UpdateTime);
			Assert.AreEqual("Ann", read.Progress.LordName);
			next = new FirestoreResponse { Status = 404, Text = "{\"error\":{\"status\":\"NOT_FOUND\"}}" };
			var missing = store.LoadAsync().GetAwaiter().GetResult();
			Assert.IsFalse(missing.Exists);
			Assert.IsFalse(missing.Failed);
			next = new FirestoreResponse { Status = 0, Error = "no network" };
			Assert.IsTrue(store.LoadAsync().GetAwaiter().GetResult().Failed);
		}
	}
}
