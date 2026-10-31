using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace ITKamianets.Engine.Account.Tests
{
	/// <summary>Firestore's typed values and requests. Nothing here reaches the network: the client gets a fake sender.</summary>
	public class FirestoreTests
	{
		[Test]
		public void Plain_values_become_typed_values_and_back()
		{
			var date = new DateTime(2026, 10, 31, 12, 30, 45, 123, DateTimeKind.Utc);
			var plain = new JObject
			{
				["name"] = "Ann \"the lord\"",
				["count"] = 42,
				["big"] = 1790000000000L,
				["ratio"] = 1.5,
				["whole"] = 2.0,
				["on"] = true,
				["nothing"] = JValue.CreateNull(),
				["when"] = new JValue(date),
				["list"] = new JArray(1, "two", 3.5, false, JValue.CreateNull(), new JArray(7), new JObject { ["k"] = "v" }),
				["map"] = new JObject { ["inner"] = new JObject { ["deep"] = new JObject { ["n"] = 5 } }, ["empty"] = new JObject(), ["none"] = new JArray() },
			};

			var fields = FirestoreRest.ToFields(plain);
			Assert.AreEqual("Ann \"the lord\"", (string)fields["name"]["stringValue"]);
			Assert.AreEqual("42", (string)fields["count"]["integerValue"], "integers are strings, as Firestore sends them");
			Assert.AreEqual("1790000000000", (string)fields["big"]["integerValue"]);
			Assert.AreEqual(1.5, (double)fields["ratio"]["doubleValue"]);
			Assert.AreEqual(JTokenType.Float, fields["whole"]["doubleValue"].Type, "a double stays a double even when it is whole");
			Assert.AreEqual(true, (bool)fields["on"]["booleanValue"]);
			Assert.IsNotNull(fields["nothing"]["nullValue"]);
			Assert.AreEqual("2026-10-31T12:30:45.123000Z", (string)fields["when"]["timestampValue"]);
			Assert.AreEqual("5", (string)fields["map"]["mapValue"]["fields"]["inner"]["mapValue"]["fields"]["deep"]["mapValue"]["fields"]["n"]["integerValue"], "nested maps are nested typed values");
			Assert.IsNull(fields["map"]["mapValue"]["fields"]["empty"]["mapValue"]["fields"], "an empty map has no fields");
			Assert.IsNull(fields["map"]["mapValue"]["fields"]["none"]["arrayValue"]["values"], "an empty array has no values");
			Assert.AreEqual(7, ((JArray)fields["list"]["arrayValue"]["values"]).Count);

			var back = FirestoreRest.FromFields(fields);
			Assert.AreEqual("Ann \"the lord\"", (string)back["name"]);
			Assert.AreEqual(JTokenType.Integer, back["count"].Type);
			Assert.AreEqual(42L, (long)back["count"]);
			Assert.AreEqual(1790000000000L, (long)back["big"]);
			Assert.AreEqual(JTokenType.Float, back["ratio"].Type);
			Assert.AreEqual(1.5, (double)back["ratio"]);
			Assert.AreEqual(JTokenType.Float, back["whole"].Type);
			Assert.AreEqual(true, (bool)back["on"]);
			Assert.AreEqual(JTokenType.Null, back["nothing"].Type);
			Assert.AreEqual(JTokenType.Date, back["when"].Type);
			Assert.AreEqual(date, ((JValue)back["when"]).Value);
			Assert.IsTrue(JToken.DeepEquals(plain["list"], back["list"]));
			Assert.AreEqual(5, (int)back["map"]["inner"]["deep"]["n"]);
			Assert.AreEqual(JTokenType.Object, back["map"]["empty"].Type);
			Assert.AreEqual(0, ((JObject)back["map"]["empty"]).Count);
			Assert.AreEqual(JTokenType.Array, back["map"]["none"].Type);
			Assert.AreEqual(0, ((JArray)back["map"]["none"]).Count);
		}

		[Test]
		public void A_typed_document_from_Firestore_is_read_with_its_times_and_a_string_that_looks_like_a_date_stays_a_string()
		{
			var text = "{\"name\":\"projects/p/databases/(default)/documents/users/u/progress/lords\",\"createTime\":\"2026-01-01T00:00:00.000000Z\",\"updateTime\":\"2026-10-31T10:00:00.123456Z\","
				+ "\"fields\":{\"label\":{\"stringValue\":\"2026-10-31T10:00:00Z\"},\"stock\":{\"mapValue\":{\"fields\":{\"at\":{\"integerValue\":\"1790000000000\"},\"amounts\":{\"mapValue\":{\"fields\":{\"wood\":{\"doubleValue\":812.25},\"stone\":{\"integerValue\":\"750\"}}}}}}},"
				+ "\"seen\":{\"timestampValue\":\"2026-10-31T10:00:00.5Z\"},\"nan\":{\"doubleValue\":\"NaN\"}}}";
			var document = FirestoreRest.ParseDocument(text);
			Assert.IsNotNull(document);
			Assert.AreEqual("2026-10-31T10:00:00.123456Z", document.UpdateTime);
			Assert.AreEqual(JTokenType.String, document.Data["label"].Type);
			Assert.AreEqual(1790000000000L, (long)document.Data["stock"]["at"]);
			Assert.AreEqual(812.25, (double)document.Data["stock"]["amounts"]["wood"]);
			Assert.AreEqual(750L, (long)document.Data["stock"]["amounts"]["stone"]);
			Assert.AreEqual(JTokenType.Date, document.Data["seen"].Type);
			Assert.IsTrue(double.IsNaN((double)document.Data["nan"]));
			Assert.IsNull(FirestoreRest.ParseDocument("{\"error\":{\"status\":\"NOT_FOUND\"}}"));
			Assert.IsNull(FirestoreRest.ParseDocument("<html>"));
		}

		[Test]
		public void A_patch_names_its_fields_in_the_mask_and_carries_the_precondition()
		{
			var url = FirestoreRest.PatchUrl("temnorid-arena", "users/u1/progress/lords", new[] { "stock" }, "2026-10-31T10:00:00.123456Z");
			Assert.AreEqual("https://firestore.googleapis.com/v1/projects/temnorid-arena/databases/(default)/documents/users/u1/progress/lords"
				+ "?updateMask.fieldPaths=stock&currentDocument.updateTime=2026-10-31T10%3A00%3A00.123456Z", url);
			StringAssert.DoesNotContain("exists", url, "an updateTime precondition already means the document exists");

			var without = FirestoreRest.PatchUrl("p", "users/u/progress/lords", new[] { "stock", "odd name" });
			StringAssert.Contains("updateMask.fieldPaths=stock&updateMask.fieldPaths=%60odd%20name%60", without);
			StringAssert.EndsWith("&currentDocument.exists=true", without, "a patch never creates the document");
			Assert.Throws<ArgumentException>(() => FirestoreRest.PatchUrl("p", "a/b", new string[0]));

			var data = new JObject { ["stock"] = new JObject { ["amounts"] = new JObject { ["wood"] = 5 }, ["at"] = 7L }, ["other"] = "must not be sent" };
			var body = JObject.Parse(FirestoreRest.PatchBody(data, new[] { "stock" }));
			Assert.AreEqual(1, ((JObject)body["fields"]).Count, "only the masked field is in the body");
			Assert.AreEqual("5", (string)body["fields"]["stock"]["mapValue"]["fields"]["amounts"]["mapValue"]["fields"]["wood"]["integerValue"]);
			Assert.AreEqual("7", (string)body["fields"]["stock"]["mapValue"]["fields"]["at"]["integerValue"]);
			Assert.Throws<ArgumentException>(() => FirestoreRest.PatchBody(data, new[] { "missing" }));
		}

		[Test]
		public void The_client_sends_the_token_and_sorts_the_answers_into_statuses()
		{
			var seen = new List<FirestoreRequest>();
			FirestoreResponse next = null;
			var client = new FirestoreClient(() => "proj", () => Task.FromResult("tok"), request => { seen.Add(request); return Task.FromResult(next); });

			next = new FirestoreResponse { Status = 200, Text = "{\"name\":\"n\",\"updateTime\":\"T\",\"fields\":{\"a\":{\"integerValue\":\"1\"}}}" };
			var got = client.GetAsync("users/u/progress/lords").GetAwaiter().GetResult();
			Assert.IsTrue(got.Ok);
			Assert.AreEqual("T", got.Document.UpdateTime);
			Assert.AreEqual("GET", seen[0].Method);
			Assert.AreEqual("tok", seen[0].Token);

			next = new FirestoreResponse { Status = 404, Text = "{\"error\":{\"status\":\"NOT_FOUND\"}}" };
			Assert.AreEqual(FirestoreStatus.NotFound, client.GetAsync("a/b").GetAwaiter().GetResult().Status);

			var data = new JObject { ["stock"] = new JObject { ["at"] = 1 } };
			next = new FirestoreResponse { Status = 400, Text = "{\"error\":{\"code\":400,\"status\":\"FAILED_PRECONDITION\"}}" };
			Assert.AreEqual(FirestoreStatus.Conflict, client.PatchAsync("a/b", data, new[] { "stock" }, "T").GetAwaiter().GetResult().Status);
			Assert.AreEqual("PATCH", seen[seen.Count - 1].Method);
			StringAssert.Contains("currentDocument.updateTime=T", seen[seen.Count - 1].Url);

			next = new FirestoreResponse { Status = 403, Text = "{\"error\":{\"status\":\"PERMISSION_DENIED\"}}" };
			Assert.AreEqual(FirestoreStatus.Denied, client.PatchAsync("a/b", data, new[] { "stock" }, "T").GetAwaiter().GetResult().Status);

			next = new FirestoreResponse { Status = 0, Error = "Cannot resolve destination host" };
			var offline = client.GetAsync("a/b").GetAwaiter().GetResult();
			Assert.AreEqual(FirestoreStatus.Failed, offline.Status);
			StringAssert.DoesNotContain("resolve", offline.Message, "messages carry words, not the raw answer");

			var signedOut = new FirestoreClient(() => "proj", () => Task.FromResult<string>(null), request => { seen.Add(request); return Task.FromResult(next); });
			var count = seen.Count;
			Assert.AreEqual(FirestoreStatus.Denied, signedOut.GetAsync("a/b").GetAwaiter().GetResult().Status);
			Assert.AreEqual(count, seen.Count, "nothing is sent without a token");
		}
	}
}
