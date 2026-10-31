using Newtonsoft.Json.Linq;

namespace Temnorid.Village.Tests
{
	/// <summary>Helpers shared by the tests: Lords-shaped documents and a fake account.</summary>
	static class TestKit
	{
		public const long T0 = 1_790_000_000_000L;

		/// <summary>A document like the one Lords saves for <paramref name="progress"/>, with fields this package does not model.</summary>
		public static JObject Doc(LordsProgress progress, string lordName = "Ann")
		{
			progress.Lord = new LordInfo { Name = lordName };
			var doc = JObject.FromObject(progress);
			doc["hero"] = JObject.Parse("{\"name\":\"Hero\",\"level\":3,\"inventory\":[1,2,3]}");
			doc["shipments"] = new JArray();
			doc["faction"] = JValue.CreateNull();
			doc["updatedAt"] = "2026-10-31T10:00:00Z";
			doc["firstLocation"] = JObject.Parse("{\"element\":\"fire\",\"region\":\"north\"}");
			return doc;
		}

		/// <summary>The default village as a Lords document whose stock was saved at <see cref="T0"/>.</summary>
		public static JObject LordsDoc(long at = T0)
		{
			var progress = DefaultVillage.Create(at);
			progress.Stock.At = at;
			return Doc(progress);
		}

		public sealed class FakeAccount : IAccountView
		{
			public bool IsSignedIn { get; set; }
			public bool IsGuest { get; set; }
			public FakeAccount(bool signedIn, bool guest) { IsSignedIn = signedIn; IsGuest = guest; }
		}

		public static double Wood(JObject doc) => (double)doc["stock"]["amounts"]["wood"];
		public static double Stone(JObject doc) => (double)doc["stock"]["amounts"]["stone"];
	}
}
