using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ITKamianets.Engine.Account;
using Newtonsoft.Json.Linq;

namespace Temnorid.Village
{
	/// <summary>The Lords save document as read: its time stamp (the precondition of the next write), its fields and what was understood of them.</summary>
	public sealed class ProgressDocument
	{
		public bool Exists { get; private set; }
		/// <summary>Why nothing could be read (offline, signed out, refused), empty when the read worked (even when the document is missing).</summary>
		public string Error { get; private set; } = "";
		/// <summary>The document's last write time, as Firestore reports it.</summary>
		public string UpdateTime { get; private set; } = "";
		/// <summary>The document's fields as plain JSON.</summary>
		public JObject Data { get; private set; }
		/// <summary>The save as the games read it; null when the fields have an unexpected shape.</summary>
		public LordsProgress Progress { get; private set; }
		public string ParseError { get; private set; } = "";

		public bool Failed => Error.Length > 0;

		public static ProgressDocument Failure(string error) => new ProgressDocument { Error = string.IsNullOrEmpty(error) ? "Could not read the save." : error };

		public static ProgressDocument Missing() => new ProgressDocument();

		public static ProgressDocument Found(JObject data, string updateTime)
		{
			var document = new ProgressDocument { Exists = true, Data = data ?? new JObject(), UpdateTime = updateTime ?? "" };
			if (LordsProgress.TryParse(document.Data, out var progress, out var error)) document.Progress = progress;
			else document.ParseError = error;
			return document;
		}

		/// <summary>
		/// True when the stock is in the exact shape Lords writes: <c>stock.amounts</c> with all six resources as numbers and a numeric
		/// <c>stock.at</c>. Craft writes only into a stock like that.
		/// </summary>
		public bool HasValidStock
		{
			get
			{
				if (!Exists || Progress == null || !(Data["stock"] is JObject stock)) return false;
				if (!Amounts.IsComplete(stock["amounts"])) return false;
				var at = stock["at"];
				return at != null && (at.Type == JTokenType.Integer || at.Type == JTokenType.Float) && at.Value<double>() > 0;
			}
		}

		/// <summary>True when the save has what it takes to draw the lord's land: a valid stock, a village with places and a farm with plots.</summary>
		public bool IsUsableLordsSave => HasValidStock && Progress.HasMaps;
	}

	public enum StockWriteResult
	{
		Saved,
		/// <summary>The document changed since it was read: read it again and retry.</summary>
		Conflict,
		/// <summary>Offline, signed out, refused or missing: nothing was written.</summary>
		Failed,
	}

	/// <summary>Where the Lords save lives. Only the stock can be written, and only into an existing document.</summary>
	public interface IProgressStore
	{
		/// <summary>Reads the document fresh.</summary>
		Task<ProgressDocument> LoadAsync();

		/// <summary>
		/// Replaces the document's <c>stock</c> field with <paramref name="stock"/> (<c>{"amounts":{...},"at":ms}</c>) and nothing else,
		/// provided the document is still at <paramref name="updateTime"/>.
		/// </summary>
		Task<StockWriteResult> SaveStockAsync(JObject stock, string updateTime);

		/// <summary>
		/// Replaces the document's <c>hero</c> field (a trip into a dungeon, or its end) and nothing else, provided the document is still at
		/// <paramref name="updateTime"/>.
		/// </summary>
		Task<StockWriteResult> SaveHeroAsync(JObject hero, string updateTime);
	}

	/// <summary>
	/// The signed-in player's <c>users/{uid}/progress/lords</c> in Firestore. The Firestore rules already let only that player read and
	/// write it. It patches the single field <c>stock</c> with an <c>updateTime</c> precondition: it cannot create the document
	/// and cannot change another field.
	/// </summary>
	public sealed class FirestoreProgressStore : IProgressStore
	{
		public const string Field = "stock";

		readonly FirestoreClient _client;
		readonly Func<string> _uid;

		public FirestoreProgressStore(FirestoreClient client, Func<string> uid)
		{
			_client = client;
			_uid = uid;
		}

		/// <summary>The store of the signed-in <see cref="GameAccount"/>.</summary>
		public static FirestoreProgressStore ForAccount() => new FirestoreProgressStore(FirestoreClient.ForAccount(), () => GameAccount.Uid);

		string Path
		{
			get
			{
				var uid = _uid();
				return string.IsNullOrEmpty(uid) ? null : "users/" + uid + "/progress/lords";
			}
		}

		public async Task<ProgressDocument> LoadAsync()
		{
			var path = Path;
			if (path == null) return ProgressDocument.Failure("Not signed in.");
			var result = await _client.GetAsync(path);
			switch (result.Status)
			{
				case FirestoreStatus.Ok: return ProgressDocument.Found(result.Document.Data, result.Document.UpdateTime);
				case FirestoreStatus.NotFound: return ProgressDocument.Missing();
				default: return ProgressDocument.Failure(result.Message);
			}
		}

		public async Task<StockWriteResult> SaveHeroAsync(JObject hero, string updateTime)
		{
			var path = Path;
			if (path == null || string.IsNullOrEmpty(updateTime) || hero == null) return StockWriteResult.Failed;
			var result = await _client.PatchAsync(path, new JObject { ["hero"] = hero }, new[] { "hero" }, updateTime);
			switch (result.Status)
			{
				case FirestoreStatus.Ok: return StockWriteResult.Saved;
				case FirestoreStatus.Conflict: return StockWriteResult.Conflict;
				default: return StockWriteResult.Failed;
			}
		}

		public async Task<StockWriteResult> SaveStockAsync(JObject stock, string updateTime)
		{
			var path = Path;
			// No precondition, no write: without it a concurrent change of Lords could be overwritten.
			if (path == null || string.IsNullOrEmpty(updateTime) || stock == null) return StockWriteResult.Failed;
			var result = await _client.PatchAsync(path, new JObject { [Field] = stock }, new[] { Field }, updateTime);
			switch (result.Status)
			{
				case FirestoreStatus.Ok: return StockWriteResult.Saved;
				case FirestoreStatus.Conflict: return StockWriteResult.Conflict;
				default: return StockWriteResult.Failed;
			}
		}
	}

	/// <summary>
	/// A Lords save kept in memory, for tests of games and of this package: it behaves like the Firestore document (a time stamp that changes with
	/// every write, a precondition) and records what was written. It never touches the network.
	/// </summary>
	public sealed class InMemoryProgressStore : IProgressStore
	{
		public sealed class Write
		{
			public JObject Stock;
			public string UpdateTime;
			public bool Applied;
		}

		/// <summary>The fields of the document, or null when it does not exist.</summary>
		public JObject Data;
		public int Version = 1;
		/// <summary>Reads and writes fail, as when offline.</summary>
		public bool Offline;
		/// <summary>Called inside a write before the precondition is checked: a test uses it to make Lords change the save at the worst moment.</summary>
		public Action BeforeSave;
		public int Loads;
		public readonly List<Write> Writes = new List<Write>();

		public InMemoryProgressStore(JObject data = null) { Data = data; }

		public string UpdateTime => "t" + Version;

		/// <summary>Another writer (the Lords app) changes the document: the time stamp moves on.</summary>
		public void ExternalWrite(Action<JObject> change)
		{
			change(Data);
			Version++;
		}

		public Task<ProgressDocument> LoadAsync()
		{
			Loads++;
			if (Offline) return Task.FromResult(ProgressDocument.Failure("Offline."));
			if (Data == null) return Task.FromResult(ProgressDocument.Missing());
			return Task.FromResult(ProgressDocument.Found((JObject)Data.DeepClone(), UpdateTime));
		}

		public Task<StockWriteResult> SaveHeroAsync(JObject hero, string updateTime)
		{
			if (Offline || Data == null) return Task.FromResult(StockWriteResult.Failed);
			if (updateTime != UpdateTime) return Task.FromResult(StockWriteResult.Conflict);
			Data["hero"] = hero.DeepClone();
			Version++;
			return Task.FromResult(StockWriteResult.Saved);
		}

		public Task<StockWriteResult> SaveStockAsync(JObject stock, string updateTime)
		{
			var write = new Write { Stock = (JObject)stock.DeepClone(), UpdateTime = updateTime };
			Writes.Add(write);
			if (Offline || Data == null) return Task.FromResult(StockWriteResult.Failed);
			BeforeSave?.Invoke();
			if (updateTime != UpdateTime) return Task.FromResult(StockWriteResult.Conflict);
			Data["stock"] = stock.DeepClone();
			Version++;
			write.Applied = true;
			return Task.FromResult(StockWriteResult.Saved);
		}
	}
}
