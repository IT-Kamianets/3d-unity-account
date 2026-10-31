using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ITKamianets.Engine.Account
{
	/// <summary>A Firestore document as the REST API returns it, with its fields already converted to plain JSON.</summary>
	public sealed class FirestoreDocument
	{
		/// <summary>The full resource name, <c>projects/.../documents/users/{uid}/...</c>. Contains a uid for user documents: never log it.</summary>
		public string Name = "";
		public string CreateTime = "";
		/// <summary>RFC 3339 time of the last write. Pass it back as the precondition of a patch to be sure nobody wrote in between.</summary>
		public string UpdateTime = "";
		public JObject Data = new JObject();
	}

	/// <summary>
	/// Firestore's REST API in pure functions: addresses, request bodies, and the conversion between Firestore's typed values
	/// (<c>{"integerValue":"5"}</c>, <c>{"mapValue":{"fields":{...}}}</c>) and plain Newtonsoft JSON. Nothing here touches the network,
	/// so it is unit tested; <see cref="FirestoreClient"/> sends the requests.
	/// <para>
	/// Mapping: null, bool, string, integers (long), doubles, timestamps (<see cref="JTokenType.Date"/>, always UTC), arrays and maps map one to one.
	/// Bytes become <see cref="JTokenType.Bytes"/>; a reference becomes its string; a geo point becomes <c>{"latitude":..,"longitude":..}</c>.
	/// </para>
	/// </summary>
	public static class FirestoreRest
	{
		const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";
		static readonly Regex SimpleField = new Regex("^[A-Za-z_][A-Za-z_0-9]*$");

		// ---------------------------------------------------------------- addresses

		/// <summary>The document's address. <paramref name="path"/> is like <c>users/abc/progress/lords</c>; each segment is escaped.</summary>
		public static string DocumentUrl(string projectId, string path)
		{
			if (string.IsNullOrEmpty(projectId)) throw new ArgumentException("A project id is needed.", nameof(projectId));
			if (string.IsNullOrEmpty(path)) throw new ArgumentException("A document path is needed.", nameof(path));
			var segments = path.Trim('/').Split('/');
			var url = new StringBuilder("https://firestore.googleapis.com/v1/projects/").Append(Uri.EscapeDataString(projectId)).Append("/databases/(default)/documents");
			foreach (var segment in segments)
			{
				if (segment.Length == 0) throw new ArgumentException("Empty path segment.", nameof(path));
				url.Append('/').Append(Uri.EscapeDataString(segment));
			}
			return url.ToString();
		}

		/// <summary>
		/// The address of a patch that changes only <paramref name="fieldPaths"/> (<c>updateMask.fieldPaths</c>). With
		/// <paramref name="updateTime"/> the write fails unless the document is still exactly as it was then
		/// (<c>currentDocument.updateTime</c>); without it, <c>currentDocument.exists=true</c> still stops the patch from creating the document.
		/// </summary>
		public static string PatchUrl(string projectId, string path, IEnumerable<string> fieldPaths, string updateTime = null)
		{
			var url = new StringBuilder(DocumentUrl(projectId, path));
			var first = true;
			foreach (var field in fieldPaths)
			{
				url.Append(first ? '?' : '&').Append("updateMask.fieldPaths=").Append(Uri.EscapeDataString(FieldPath(field)));
				first = false;
			}
			if (first) throw new ArgumentException("A patch needs at least one field path.", nameof(fieldPaths));
			if (!string.IsNullOrEmpty(updateTime)) url.Append("&currentDocument.updateTime=").Append(Uri.EscapeDataString(updateTime));
			else url.Append("&currentDocument.exists=true");
			return url.ToString();
		}

		/// <summary>A field path as Firestore writes it: plain names as they are, anything else in backticks.</summary>
		public static string FieldPath(string name)
		{
			if (SimpleField.IsMatch(name)) return name;
			return "`" + name.Replace("\\", "\\\\").Replace("`", "\\`") + "`";
		}

		/// <summary>The body of a patch: <c>{"fields":{...}}</c> with only the given top-level fields of <paramref name="data"/>.</summary>
		public static string PatchBody(JObject data, IEnumerable<string> fieldPaths)
		{
			var fields = new JObject();
			foreach (var name in fieldPaths)
			{
				var value = data[name];
				// A field in the mask but not in the body is deleted by Firestore: only the fields that are given are masked.
				if (value == null) throw new ArgumentException("The data has no field '" + name + "' to write.", nameof(data));
				fields[name] = ToValue(value);
			}
			return new JObject { ["fields"] = fields }.ToString(Formatting.None);
		}

		// ---------------------------------------------------------------- typed values

		/// <summary>Plain JSON to a Firestore typed value.</summary>
		public static JObject ToValue(JToken token)
		{
			if (token == null) return new JObject { ["nullValue"] = JValue.CreateNull() };
			switch (token.Type)
			{
				case JTokenType.Null:
				case JTokenType.Undefined:
					return new JObject { ["nullValue"] = JValue.CreateNull() };
				case JTokenType.Boolean:
					return new JObject { ["booleanValue"] = token.Value<bool>() };
				case JTokenType.Integer:
					return new JObject { ["integerValue"] = Convert.ToInt64(((JValue)token).Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) };
				case JTokenType.Float:
				{
					var number = Convert.ToDouble(((JValue)token).Value, CultureInfo.InvariantCulture);
					if (double.IsNaN(number)) return new JObject { ["doubleValue"] = "NaN" };
					if (double.IsPositiveInfinity(number)) return new JObject { ["doubleValue"] = "Infinity" };
					if (double.IsNegativeInfinity(number)) return new JObject { ["doubleValue"] = "-Infinity" };
					return new JObject { ["doubleValue"] = number };
				}
				case JTokenType.Date:
				{
					var value = ((JValue)token).Value;
					var moment = value is DateTimeOffset offset ? offset.UtcDateTime : ((DateTime)value).ToUniversalTime();
					return new JObject { ["timestampValue"] = moment.ToString(TimestampFormat, CultureInfo.InvariantCulture) };
				}
				case JTokenType.Bytes:
					return new JObject { ["bytesValue"] = Convert.ToBase64String((byte[])((JValue)token).Value) };
				case JTokenType.Array:
				{
					var values = new JArray();
					foreach (var item in (JArray)token) values.Add(ToValue(item));
					var array = new JObject();
					if (values.Count > 0) array["values"] = values;
					return new JObject { ["arrayValue"] = array };
				}
				case JTokenType.Object:
				{
					var map = new JObject();
					var fields = ToFields((JObject)token);
					if (fields.Count > 0) map["fields"] = fields;
					return new JObject { ["mapValue"] = map };
				}
				case JTokenType.String:
					return new JObject { ["stringValue"] = token.Value<string>() ?? "" };
				default:
					// Guid, Uri, TimeSpan: their text.
					return new JObject { ["stringValue"] = (token as JValue)?.Value?.ToString() ?? "" };
			}
		}

		/// <summary>A Firestore typed value to plain JSON. An unknown kind of value is an error.</summary>
		public static JToken FromValue(JToken typed)
		{
			var obj = typed as JObject;
			if (obj == null) throw new FormatException("A Firestore value is an object.");
			foreach (var property in obj.Properties())
			{
				switch (property.Name)
				{
					case "nullValue": return JValue.CreateNull();
					case "booleanValue": return new JValue(property.Value.Value<bool>());
					case "integerValue": return new JValue(long.Parse(property.Value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture));
					case "doubleValue":
					{
						var text = property.Value.Type == JTokenType.String ? property.Value.Value<string>() : null;
						if (text == "NaN") return new JValue(double.NaN);
						if (text == "Infinity") return new JValue(double.PositiveInfinity);
						if (text == "-Infinity") return new JValue(double.NegativeInfinity);
						return new JValue(Convert.ToDouble(((JValue)property.Value).Value, CultureInfo.InvariantCulture));
					}
					case "timestampValue":
						return new JValue(DateTime.Parse(property.Value.Value<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
					case "stringValue": return new JValue(property.Value.Value<string>() ?? "");
					case "bytesValue": return new JValue(Convert.FromBase64String(property.Value.Value<string>() ?? ""));
					case "referenceValue": return new JValue(property.Value.Value<string>() ?? "");
					case "geoPointValue":
						return new JObject { ["latitude"] = property.Value["latitude"]?.Value<double>() ?? 0d, ["longitude"] = property.Value["longitude"]?.Value<double>() ?? 0d };
					case "arrayValue":
					{
						var array = new JArray();
						if (property.Value["values"] is JArray values) foreach (var item in values) array.Add(FromValue(item));
						return array;
					}
					case "mapValue":
						return FromFields(property.Value["fields"] as JObject);
				}
			}
			throw new FormatException("Unknown Firestore value.");
		}

		/// <summary>Plain JSON object to Firestore <c>fields</c>.</summary>
		public static JObject ToFields(JObject data)
		{
			var fields = new JObject();
			if (data == null) return fields;
			foreach (var property in data.Properties()) fields[property.Name] = ToValue(property.Value);
			return fields;
		}

		/// <summary>Firestore <c>fields</c> to a plain JSON object. A missing <c>fields</c> (an empty map) is an empty object.</summary>
		public static JObject FromFields(JObject fields)
		{
			var data = new JObject();
			if (fields == null) return data;
			foreach (var property in fields.Properties()) data[property.Name] = FromValue(property.Value);
			return data;
		}

		// ---------------------------------------------------------------- answers

		/// <summary>Reads JSON without turning date-looking strings into dates (Firestore's own typed values say what is a timestamp).</summary>
		public static JToken ParseJson(string text)
		{
			using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
				return JToken.Load(reader);
		}

		/// <summary>The document in a get or patch answer, or null when the text is not a document.</summary>
		public static FirestoreDocument ParseDocument(string text)
		{
			if (string.IsNullOrWhiteSpace(text)) return null;
			try
			{
				var root = ParseJson(text) as JObject;
				if (root == null || root["name"] == null) return null;
				return new FirestoreDocument
				{
					Name = root["name"].Value<string>() ?? "",
					CreateTime = root["createTime"]?.Value<string>() ?? "",
					UpdateTime = root["updateTime"]?.Value<string>() ?? "",
					Data = FromFields(root["fields"] as JObject),
				};
			}
			catch (Exception exception) when (exception is JsonException || exception is FormatException || exception is InvalidCastException)
			{
				return null;
			}
		}

		/// <summary>The status word of a Firestore error answer (<c>FAILED_PRECONDITION</c>, <c>NOT_FOUND</c>...), or empty.</summary>
		public static string ErrorStatus(string text)
		{
			if (string.IsNullOrWhiteSpace(text)) return "";
			try
			{
				var root = ParseJson(text);
				if (root is JArray array && array.Count > 0) root = array[0];
				return root?["error"]?["status"]?.Value<string>() ?? "";
			}
			catch (JsonException)
			{
				return "";
			}
		}
	}
}
