using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace ITKamianets.Engine.Account
{
	/// <summary>What happened to a Firestore call.</summary>
	public enum FirestoreStatus
	{
		Ok,
		/// <summary>The document does not exist (a get) or has been removed (a patch with a precondition).</summary>
		NotFound,
		/// <summary>The precondition failed: the document was changed by somebody else since it was read.</summary>
		Conflict,
		/// <summary>Not signed in, a bad token, or the security rules refused.</summary>
		Denied,
		/// <summary>No network, a timeout, or an answer that could not be understood.</summary>
		Failed,
	}

	public sealed class FirestoreRequest
	{
		public string Method = "GET";
		public string Url = "";
		public string Body = "";
		/// <summary>The ID token, sent as <c>Authorization: Bearer</c>. Never log a request.</summary>
		public string Token = "";
	}

	public sealed class FirestoreResponse
	{
		/// <summary>The HTTP status, 0 when nothing came back.</summary>
		public int Status;
		public string Text = "";
		public string Error = "";
	}

	public sealed class FirestoreGetResult
	{
		public FirestoreStatus Status;
		public FirestoreDocument Document;
		public string Message = "";
		public bool Ok => Status == FirestoreStatus.Ok;
	}

	public sealed class FirestorePatchResult
	{
		public FirestoreStatus Status;
		public FirestoreDocument Document;
		public string Message = "";
		public bool Ok => Status == FirestoreStatus.Ok;
	}

	/// <summary>
	/// Reads and patches Firestore documents as the signed-in player, over REST: the ID token comes from
	/// <see cref="GameAccount.GetIdTokenAsync"/> and the project from <see cref="GameAccount.Config"/>. It never creates a document and
	/// never writes a field it was not given. The sender can be replaced, so tests never reach the network.
	/// </summary>
	public sealed class FirestoreClient
	{
		readonly Func<string> _projectId;
		readonly Func<Task<string>> _token;
		readonly Func<FirestoreRequest, Task<FirestoreResponse>> _send;

		public FirestoreClient(Func<string> projectId, Func<Task<string>> token, Func<FirestoreRequest, Task<FirestoreResponse>> send = null)
		{
			_projectId = projectId;
			_token = token;
			_send = send ?? SendAsync;
		}

		/// <summary>A client for the signed-in <see cref="GameAccount"/>.</summary>
		public static FirestoreClient ForAccount() => new FirestoreClient(() => GameAccount.Config.ProjectId, GameAccount.GetIdTokenAsync);

		/// <summary>Gets a document by path such as <c>users/{uid}/progress/lords</c>.</summary>
		public async Task<FirestoreGetResult> GetAsync(string path)
		{
			var token = await _token();
			if (string.IsNullOrEmpty(token)) return new FirestoreGetResult { Status = FirestoreStatus.Denied, Message = "Not signed in." };
			var response = await Send(new FirestoreRequest { Method = "GET", Url = FirestoreRest.DocumentUrl(_projectId(), path), Token = token });
			var status = Classify(response);
			if (status != FirestoreStatus.Ok) return new FirestoreGetResult { Status = status, Message = Describe(response, status) };
			var document = FirestoreRest.ParseDocument(response.Text);
			if (document == null) return new FirestoreGetResult { Status = FirestoreStatus.Failed, Message = "The answer was not a document." };
			return new FirestoreGetResult { Status = FirestoreStatus.Ok, Document = document };
		}

		/// <summary>
		/// Changes only <paramref name="fieldPaths"/> of an existing document, taking their values from <paramref name="data"/>. With
		/// <paramref name="updateTime"/> the write happens only if the document has not changed since (<see cref="FirestoreStatus.Conflict"/> otherwise).
		/// </summary>
		public async Task<FirestorePatchResult> PatchAsync(string path, JObject data, string[] fieldPaths, string updateTime = null)
		{
			var token = await _token();
			if (string.IsNullOrEmpty(token)) return new FirestorePatchResult { Status = FirestoreStatus.Denied, Message = "Not signed in." };
			var request = new FirestoreRequest
			{
				Method = "PATCH",
				Url = FirestoreRest.PatchUrl(_projectId(), path, fieldPaths, updateTime),
				Body = FirestoreRest.PatchBody(data, fieldPaths),
				Token = token,
			};
			var response = await Send(request);
			var status = Classify(response);
			if (status != FirestoreStatus.Ok) return new FirestorePatchResult { Status = status, Message = Describe(response, status) };
			return new FirestorePatchResult { Status = FirestoreStatus.Ok, Document = FirestoreRest.ParseDocument(response.Text) };
		}

		async Task<FirestoreResponse> Send(FirestoreRequest request)
		{
			try
			{
				return await _send(request) ?? new FirestoreResponse { Error = "No answer." };
			}
			catch (Exception exception)
			{
				return new FirestoreResponse { Error = exception.GetType().Name };
			}
		}

		static FirestoreStatus Classify(FirestoreResponse response)
		{
			if (response.Status >= 200 && response.Status < 300) return FirestoreStatus.Ok;
			var word = FirestoreRest.ErrorStatus(response.Text);
			if (response.Status == 404 || word == "NOT_FOUND") return FirestoreStatus.NotFound;
			// A failed precondition is reported as 400 FAILED_PRECONDITION (updateTime) or 409 ABORTED.
			if (word == "FAILED_PRECONDITION" || word == "ABORTED" || response.Status == 409 || response.Status == 412) return FirestoreStatus.Conflict;
			if (response.Status == 401 || response.Status == 403 || word == "PERMISSION_DENIED" || word == "UNAUTHENTICATED") return FirestoreStatus.Denied;
			return FirestoreStatus.Failed;
		}

		static string Describe(FirestoreResponse response, FirestoreStatus status)
		{
			// Only words, never the answer's text or the address: those hold uids.
			var word = FirestoreRest.ErrorStatus(response.Text);
			return "Firestore: " + status + (response.Status > 0 ? " (HTTP " + response.Status + (word.Length > 0 ? ", " + word : "") + ")" : "");
		}

		static async Task<FirestoreResponse> SendAsync(FirestoreRequest request)
		{
			using (var web = new UnityWebRequest(request.Url, request.Method))
			{
				if (!string.IsNullOrEmpty(request.Body))
				{
					web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.Body));
					web.SetRequestHeader("Content-Type", "application/json");
				}
				web.downloadHandler = new DownloadHandlerBuffer();
				web.SetRequestHeader("Authorization", "Bearer " + request.Token);
				web.timeout = 10;
				await web.SendWebRequest();
				return new FirestoreResponse
				{
					Status = (int)web.responseCode,
					Text = web.downloadHandler != null ? web.downloadHandler.text : "",
					Error = web.result == UnityWebRequest.Result.Success ? "" : web.error ?? "",
				};
			}
		}
	}
}
