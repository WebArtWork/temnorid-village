using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Temnorid.Village
{
	public enum StockMode
	{
		/// <summary>The default village: the stock is the game's own, kept in its own save, never sent anywhere. No production.</summary>
		Local,
		/// <summary>A Lords village: the stock is the lord's stock in Lords. It produces as in Lords and is written back (only the <c>stock</c> field).</summary>
		Lords,
		/// <summary>An online guest: shows what the host reports; cannot change anything.</summary>
		Mirror,
	}

	public enum SyncOutcome
	{
		/// <summary>The game's changes were written to Lords.</summary>
		Saved,
		/// <summary>Nothing to write; the save was read again and the stock follows Lords.</summary>
		Refreshed,
		/// <summary>Another sync is running.</summary>
		Busy,
		/// <summary>This stock is not written to Lords (local, mirror, or the save is missing or in an unexpected shape).</summary>
		Disabled,
		/// <summary>Offline, refused, or the save kept changing: the changes are kept and tried again later.</summary>
		Failed,
	}

	/// <summary>
	/// The lord's six stocks as a game sees them. The amount shown is what Lords would show now (<see cref="LordsRules.StockAt(StockSave, IEnumerable{FarmPlot}, IDictionary{string, Construction}, IDictionary{string, VillageBuilding}, long)"/>
	/// of the last saved stock, so production keeps running while the game is open) plus the game's own net change since it last wrote.
	/// <para>
	/// <see cref="Add"/> and <see cref="TrySpend"/> change that net change, never the saved stock. <see cref="SyncAsync"/> writes it:
	/// it reads the document fresh, works out the stock Lords has now (Lords may have spent resources in the browser meanwhile), applies
	/// the net change to that, and patches only <c>stock</c> with the document's <c>updateTime</c> as precondition; if the document changed in
	/// between it reads again and retries. It never creates the document and never writes any other field, and writes nothing when the
	/// stock has an unexpected shape.
	/// </para>
	/// </summary>
	public sealed class SharedStock
	{
		/// <summary>How many times a write is tried again after the document changed under it.</summary>
		public const int MaxAttempts = 4;
		/// <summary>The least time between two writes, in seconds.</summary>
		public const double WriteInterval = 5;
		/// <summary>How often the save is read again when there is nothing to write, in seconds.</summary>
		public const double RefreshInterval = 60;

		readonly Func<long> _clock;
		readonly IProgressStore _store;
		StockSave _base;
		LordsProgress _progress;
		readonly Amounts _delta = new Amounts();
		Amounts _mirror;
		Amounts _mirrorCapacity;
		bool _syncing;
		double _lastSync = double.NegativeInfinity;

		public StockMode Mode { get; }
		/// <summary>True while changes are written to Lords. Turns false for good when the save turns out missing or unexpected.</summary>
		public bool Writable { get; private set; }
		/// <summary>The last thing worth telling the player about the link to Lords, in English. Never contains ids.</summary>
		public string Status { get; private set; } = "";
		public event Action Changed;

		public static Func<long> SystemClock => () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		SharedStock(StockMode mode, Func<long> clock, IProgressStore store)
		{
			Mode = mode;
			_clock = clock ?? SystemClock;
			_store = store;
		}

		/// <summary>A stock of the default village: starts at <paramref name="amounts"/>, capped by the village's stores, no production.</summary>
		public static SharedStock ForLocal(LordsProgress village, Amounts amounts, Func<long> clock = null)
		{
			var stock = new SharedStock(StockMode.Local, clock, null) { _progress = village };
			stock._base = new StockSave(amounts.Clone(), stock._clock());
			return stock;
		}

		/// <summary>The stock of a Lords village that was just read from <paramref name="store"/>.</summary>
		public static SharedStock ForLords(LordsProgress progress, IProgressStore store, Func<long> clock = null)
		{
			var stock = new SharedStock(StockMode.Lords, clock, store) { _progress = progress, Writable = true };
			stock._base = new StockSave(progress.Stock.Amounts.Clone(), progress.Stock.At);
			return stock;
		}

		/// <summary>A guest's view of the host's stock: the capacity is fixed, the amounts are whatever the host last said.</summary>
		public static SharedStock ForMirror(Amounts capacity)
		{
			return new SharedStock(StockMode.Mirror, null, null) { _mirror = new Amounts(), _mirrorCapacity = capacity.Clone() };
		}

		public bool IsShared => Mode == StockMode.Lords;
		public bool Dirty => !_delta.IsZero();
		public long Now => _clock();

		// ---------------------------------------------------------------- reading

		/// <summary>How much of a resource there is right now.</summary>
		public double Get(string id) => Get(id, Now);

		public double Get(string id, long now)
		{
			if (Mode == StockMode.Mirror) return _mirror[id];
			return Saved(now)[id] + _delta[id];
		}

		/// <summary>Whole units of a resource, for the screen.</summary>
		public int Count(string id) => (int)Math.Floor(Get(id) + 1e-9);

		/// <summary>How much of a resource the stores hold at most now.</summary>
		public double Capacity(string id) => Capacities(Now)[id];

		public Amounts Current()
		{
			var now = Now;
			var amounts = new Amounts();
			foreach (var id in Res.All) amounts[id] = Get(id, now);
			return amounts;
		}

		// The last saved stock brought to `now` by production (Lords villages only).
		Amounts Saved(long now)
		{
			if (Mode != StockMode.Lords || _progress.Farm == null) return _base.Amounts;
			return LordsRules.StockAt(_base, _progress.Farm.Plots, _progress.Farm.Levels, _progress.Village?.Buildings, now);
		}

		Amounts Capacities(long now)
		{
			if (Mode == StockMode.Mirror) return _mirrorCapacity;
			return LordsRules.Capacity(_progress.Village?.Buildings, now);
		}

		/// <summary>The stock of a local village, to keep in the game's own save.</summary>
		public Amounts ToLocal() => Current();

		// ---------------------------------------------------------------- changing

		/// <summary>Puts up to <paramref name="amount"/> in the stores. What does not fit under the capacity is not added. Returns how much was added.</summary>
		public int Add(string id, int amount)
		{
			if (Mode == StockMode.Mirror || amount <= 0 || !Res.IsKnown(id)) return 0;
			var now = Now;
			var room = Math.Floor(Math.Max(0d, Capacities(now)[id] - Get(id, now)) + 1e-9);
			var added = (int)Math.Min(amount, room);
			if (added <= 0) return 0;
			_delta[id] += added;
			Changed?.Invoke();
			return added;
		}

		/// <summary>Takes <paramref name="amount"/> out when there is that much.</summary>
		public bool TrySpend(string id, int amount)
		{
			var cost = new Amounts();
			cost[id] = amount;
			return TrySpend(cost);
		}

		/// <summary>Takes a whole cost out when every resource of it is there, or nothing at all.</summary>
		public bool TrySpend(Amounts cost)
		{
			if (Mode == StockMode.Mirror) return false;
			var now = Now;
			foreach (var id in Res.All)
			{
				if (cost[id] < 0) return false;
				if (cost[id] > Get(id, now) + 1e-9) return false;
			}
			foreach (var id in Res.All) _delta[id] -= cost[id];
			Changed?.Invoke();
			return true;
		}

		/// <summary>Mirror only: the host says how much there is.</summary>
		public void SetMirrored(string id, double value)
		{
			if (Mode != StockMode.Mirror || !Res.IsKnown(id) || _mirror[id] == value) return;
			_mirror[id] = value;
			Changed?.Invoke();
		}

		// ---------------------------------------------------------------- the link to Lords

		/// <summary>True when a write (or a read, when nothing changed) is due, given a clock in seconds that only goes forward.</summary>
		public bool DueForSync(double seconds)
		{
			if (Mode != StockMode.Lords || !Writable || _syncing) return false;
			var since = seconds - _lastSync;
			return Dirty ? since >= WriteInterval : since >= RefreshInterval;
		}

		/// <summary>
		/// Writes the game's net change to Lords (see the class text), or just reads the save again when there is nothing to write.
		/// Never throws; the changes stay until they are written. <paramref name="seconds"/> is the game's clock, for <see cref="DueForSync"/>.
		/// </summary>
		public async Task<SyncOutcome> SyncAsync(double seconds = 0)
		{
			if (Mode != StockMode.Lords || !Writable) return SyncOutcome.Disabled;
			if (_syncing) return SyncOutcome.Busy;
			_syncing = true;
			_lastSync = seconds;
			try
			{
				for (var attempt = 0; attempt < MaxAttempts; attempt++)
				{
					var document = await _store.LoadAsync();
					if (document.Failed) { Status = "Offline: could not read the Lords save."; return SyncOutcome.Failed; }
					if (!document.Exists) { Disable("The Lords save is gone: the stock is no longer shared."); return SyncOutcome.Disabled; }
					if (!document.HasValidStock) { Disable("The Lords save has an unexpected shape: the stock is no longer shared."); return SyncOutcome.Disabled; }

					var now = Now;
					var fresh = document.Progress;
					// Lords may have built, upgraded or spent since: follow its save.
					_progress = fresh;
					var sent = _delta.Clone();

					if (sent.IsZero())
					{
						_base = new StockSave(fresh.Stock.Amounts.Clone(), fresh.Stock.At);
						Status = "Linked to Lords.";
						Changed?.Invoke();
						return SyncOutcome.Refreshed;
					}

					var current = LordsRules.StockAt(fresh, now);
					var cap = LordsRules.Capacity(fresh.Village?.Buildings, now);
					var next = new Amounts();
					foreach (var id in Res.All) next[id] = Merge(current[id], sent[id], cap[id]);

					var body = new JObject { ["amounts"] = next.ToJson(), ["at"] = now };
					var result = await _store.SaveStockAsync(body, document.UpdateTime);
					if (result == StockWriteResult.Saved)
					{
						_base = new StockSave(next.Clone(), now);
						// Changes made while the write was in flight stay; what was sent is now in the saved stock.
						foreach (var id in Res.All) _delta[id] -= sent[id];
						Status = "Linked to Lords.";
						Changed?.Invoke();
						return SyncOutcome.Saved;
					}
					if (result == StockWriteResult.Failed) { Status = "Offline: could not save to Lords. Will try again."; return SyncOutcome.Failed; }
					// Conflict: Lords wrote in between. Read again and apply the same change to the new stock.
				}
				Status = "Lords keeps changing the save. Will try again.";
				return SyncOutcome.Failed;
			}
			catch (Exception exception)
			{
				Status = "Could not save to Lords (" + exception.GetType().Name + "). Will try again.";
				return SyncOutcome.Failed;
			}
			finally
			{
				_syncing = false;
			}
		}

		/// <summary>The new amount of one resource: Lords' amount now plus the game's net change. A gain stops at the capacity (or at what is already there), a loss at 0.</summary>
		public static double Merge(double lordsNow, double delta, double capacity)
		{
			if (delta >= 0) return Math.Min(Math.Max(lordsNow, capacity), lordsNow + delta);
			return Math.Max(0d, lordsNow + delta);
		}

		void Disable(string status)
		{
			Writable = false;
			Status = status;
			Changed?.Invoke();
		}
	}
}
