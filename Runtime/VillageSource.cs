using System;
using System.Threading.Tasks;
using ITKamianets.Engine.Account;

namespace Temnorid.Village
{
	/// <summary>The village a game shows, and its stock. Made by <see cref="VillageSource"/> (host, single player) or <see cref="ForClient"/> (an online guest).</summary>
	public sealed class VillageSession
	{
		/// <summary>True when this is a Lords player's own village and its stock is the shared Lords stock.</summary>
		public bool IsLords { get; private set; }
		public string LordName => Data != null ? Data.LordName : "";
		/// <summary>Why this village is shown, in English: for the player, never an id or a token.</summary>
		public string Status { get; internal set; } = "";
		/// <summary>The save the village was read from; null for a guest of an online game.</summary>
		public LordsProgress Progress { get; private set; }
		public VillageData Data { get; private set; }
		public SharedStock Stock { get; private set; }

		/// <summary>A short title for the screen: the lord's village or the standard village.</summary>
		public string Title => IsLords && LordName.Length > 0 ? LordName + "'s village" : IsLords ? "Your village" : "Standard village";

		internal static VillageSession Lords(LordsProgress progress, VillageData data, SharedStock stock, string status)
			=> new VillageSession { IsLords = true, Progress = progress, Data = data, Stock = stock, Status = status };

		internal static VillageSession Default(LordsProgress progress, VillageData data, SharedStock stock, string status)
			=> new VillageSession { IsLords = false, Progress = progress, Data = data, Stock = stock, Status = status };

		/// <summary>An online guest shows the host's village and mirrors the host's stock; it has no save of its own.</summary>
		public static VillageSession ForClient(VillageData data, string status = "")
		{
			return new VillageSession { IsLords = data.IsLords, Data = data, Stock = SharedStock.ForMirror(data.Capacity), Status = status };
		}
	}

	/// <summary>What <see cref="VillageSource"/> needs to know about the player's account.</summary>
	public interface IAccountView
	{
		bool IsSignedIn { get; }
		bool IsGuest { get; }
	}

	/// <summary>The signed-in <see cref="GameAccount"/>.</summary>
	public sealed class GameAccountView : IAccountView
	{
		public bool IsSignedIn => GameAccount.IsSignedIn;
		public bool IsGuest => GameAccount.IsGuest;
	}

	/// <summary>
	/// Decides which village to show. Signed in with Google and a usable Lords save exists: that save (<see cref="VillageSession.IsLords"/>).
	/// Anything else (a guest, signed out, offline, no save, a save that is not ready, a refusal, a timeout) gives the standard
	/// default village, with the reason in <see cref="VillageSession.Status"/>. It never throws and never waits longer than the timeout.
	/// </summary>
	public sealed class VillageSource
	{
		public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

		readonly IProgressStore _store;
		readonly IAccountView _account;
		readonly Func<long> _clock;

		public VillageSource(IProgressStore store, IAccountView account, Func<long> clock = null)
		{
			_store = store;
			_account = account;
			_clock = clock ?? SharedStock.SystemClock;
		}

		/// <summary>A source for the signed-in <see cref="GameAccount"/> and its Firestore save.</summary>
		public static VillageSource ForAccount() => new VillageSource(FirestoreProgressStore.ForAccount(), new GameAccountView());

		/// <summary>
		/// Loads the village. <paramref name="localStock"/> is the stock a default village starts with (the game's own save); without it the
		/// stock is Lords' start stock.
		/// </summary>
		public async Task<VillageSession> LoadAsync(Amounts localStock = null, TimeSpan? timeout = null)
		{
			try
			{
				if (!_account.IsSignedIn) return Default(localStock, "Not signed in: showing the standard village.");
				if (_account.IsGuest) return Default(localStock, "Guest account: showing the standard village. Sign in with Google to see your own.");

				var load = _store.LoadAsync();
				var first = await Task.WhenAny(load, Task.Delay(timeout ?? DefaultTimeout));
				if (first != load) return Default(localStock, "The Lords save took too long to load: showing the standard village.");

				var document = await load;
				if (document.Failed) return Default(localStock, "Offline: showing the standard village.");
				if (!document.Exists) return Default(localStock, "No Lords save yet: showing the standard village. Start your village in Lords first.");
				if (document.Progress == null) return Default(localStock, "The Lords save could not be read: showing the standard village.");
				if (!document.IsUsableLordsSave) return Default(localStock, "Your Lords village is not ready yet: showing the standard village.");

				var progress = document.Progress;
				var now = _clock();
				var data = VillageLayout.Build(progress, true, now);
				var stock = SharedStock.ForLords(progress, _store, _clock);
				return VillageSession.Lords(progress, data, stock, "Your Lords village.");
			}
			catch (Exception exception)
			{
				return Default(localStock, "Could not load the Lords save (" + exception.GetType().Name + "): showing the standard village.");
			}
		}

		VillageSession Default(Amounts localStock, string why) => CreateDefault(localStock, why, _clock());

		/// <summary>The standard village, for a game that wants it straight away.</summary>
		public static VillageSession CreateDefault(Amounts localStock, string status, long now)
		{
			var progress = DefaultVillage.Create(now);
			var data = VillageLayout.Build(progress, false, now);
			var stock = SharedStock.ForLocal(progress, localStock ?? DefaultVillage.StartAmounts());
			return VillageSession.Default(progress, data, stock, status);
		}
	}
}
