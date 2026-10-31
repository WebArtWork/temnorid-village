using UnityEngine;

namespace Temnorid.Village
{
	/// <summary>
	/// Keeps a <see cref="SharedStock"/> linked to Lords while a game runs: writes the game's changes at most every few seconds
	/// (<see cref="SharedStock.WriteInterval"/>), reads the save now and then so the stock follows Lords, and writes what is left
	/// when the game is paused or quits. A game also calls <see cref="Flush"/> when it leaves the game scene.
	/// </summary>
	public sealed class StockSyncer : MonoBehaviour
	{
		public SharedStock Stock;

		void Update()
		{
			if (Stock == null) return;
			var now = Time.unscaledTimeAsDouble;
			if (Stock.DueForSync(now)) _ = Stock.SyncAsync(now);
		}

		/// <summary>Writes pending changes now (best effort: the write goes on in the background).</summary>
		public void Flush()
		{
			if (Stock != null && Stock.IsShared && Stock.Dirty) _ = Stock.SyncAsync(Time.unscaledTimeAsDouble);
		}

		void OnApplicationPause(bool paused)
		{
			if (paused) Flush();
		}

		void OnApplicationQuit() => Flush();
	}
}
