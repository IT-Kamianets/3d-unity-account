using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ITKamianets.Engine.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace ITKamianets.Engine.Account
{
	/// <summary>
	/// The player's account, the same one Lords uses: a Firebase user, either a guest (anonymous) or signed in with Google.
	/// Everything goes through Firebase's REST API, so it works the same on Windows, Android and iOS without the Firebase SDK.
	/// A guest can later sign in with Google and keeps the same uid, so nothing is lost. Not being signed in at all is fine:
	/// a game still plays offline.
	/// </summary>
	public static class GameAccount
	{
		const string KeyRefresh = "account.refresh";
		const string KeyUid = "account.uid";
		const string KeyName = "account.name";
		const string KeyEmail = "account.email";
		const string KeyAnonymous = "account.anonymous";

		public static FirebaseConfig Config { get; set; } = FirebaseConfig.Temnorid;

		public static bool IsSignedIn => !string.IsNullOrEmpty(Uid);
		public static string Uid { get; private set; } = "";
		public static string DisplayName { get; private set; } = "";
		public static string Email { get; private set; } = "";
		/// <summary>True for a guest. Meaningful only while <see cref="IsSignedIn"/>.</summary>
		public static bool IsGuest { get; private set; }
		public static string LastError { get; private set; } = "";
		public static bool Busy { get; private set; }

		/// <summary>Google sign-in needs an OAuth client for this platform; until one is set the screen offers the guest only.</summary>
		public static bool GoogleAvailable
		{
			get
			{
#if UNITY_STANDALONE || UNITY_EDITOR
				return !string.IsNullOrEmpty(Config.GoogleDesktopClientId);
#else
				return false;
#endif
			}
		}

		public static event Action Changed;

		static string _idToken = "";
		static string _refreshToken = "";
		static DateTime _expires = DateTime.MinValue;
		static bool _restored;

		// ---------------------------------------------------------------- restoring

		/// <summary>Picks up the saved session: who was signed in last time, refreshed if the network is there. Safe to call every start.</summary>
		public static async Task RestoreAsync()
		{
			if (_restored) return;
			_restored = true;
			var storage = EngineStorage.Current;
			_refreshToken = storage.GetString(KeyRefresh, "");
			if (string.IsNullOrEmpty(_refreshToken)) return;

			// Show who it was straight away, even before the network answers.
			Uid = storage.GetString(KeyUid, "");
			DisplayName = storage.GetString(KeyName, "");
			Email = storage.GetString(KeyEmail, "");
			IsGuest = storage.GetBool(KeyAnonymous, true);
			Changed?.Invoke();

			await RefreshAsync();
		}

		/// <summary>A valid ID token for calls to Firebase services, refreshed when it is about to run out. Null when not signed in or offline.</summary>
		public static async Task<string> GetIdTokenAsync()
		{
			if (!IsSignedIn) return null;
			if (DateTime.UtcNow < _expires.AddSeconds(-60) && !string.IsNullOrEmpty(_idToken)) return _idToken;
			return await RefreshAsync() ? _idToken : null;
		}

		static async Task<bool> RefreshAsync()
		{
			if (string.IsNullOrEmpty(_refreshToken)) return false;
			var (ok, text) = await Post(FirebaseRest.RefreshUrl(Config.ApiKey), FirebaseRest.RefreshBody(_refreshToken), "application/x-www-form-urlencoded");
			var response = ok ? AuthResponse.Parse(text) : null;
			if (response == null || string.IsNullOrEmpty(response.Token))
			{
				// Offline keeps the remembered player; a refusal means the account is gone, so start clean.
				if (!ok && text != null && text.Contains("TOKEN_EXPIRED") || text != null && text.Contains("USER_NOT_FOUND")) ForgetLocal();
				return false;
			}
			Adopt(response, null);
			return true;
		}

		// ---------------------------------------------------------------- signing in

		/// <summary>Signs in as a guest: a real Firebase user without a name or an email, for someone who just wants to play.</summary>
		public static async Task<bool> SignInAsGuestAsync()
		{
			if (IsSignedIn) return true;
			return await Run(async () =>
			{
				var (ok, text) = await Post(FirebaseRest.SignUpUrl(Config.ApiKey), FirebaseRest.AnonymousBody(), "application/json");
				var response = ok ? AuthResponse.Parse(text) : null;
				if (response == null || string.IsNullOrEmpty(response.Token)) return Describe(text, "Could not reach the account service.");
				Adopt(response, true);
				return null;
			});
		}

		/// <summary>
		/// Signs in with Google through the browser (Windows). When this device already has a guest, the guest becomes the Google
		/// account and keeps its uid.
		/// </summary>
		public static async Task<bool> SignInWithGoogleAsync()
		{
			if (!GoogleAvailable) { LastError = "Google sign-in is not set up for this platform yet."; return false; }
#if UNITY_STANDALONE || UNITY_EDITOR
			return await Run(async () =>
			{
				var idToken = await GoogleIdTokenAsync();
				if (string.IsNullOrEmpty(idToken)) return LastError.Length > 0 ? LastError : "Google sign-in was cancelled.";

				var link = IsSignedIn && IsGuest ? await GetIdTokenAsync() : null;
				var (ok, text) = await Post(FirebaseRest.SignInWithIdpUrl(Config.ApiKey), FirebaseRest.IdpBody(idToken, link), "application/json");
				var response = ok ? AuthResponse.Parse(text) : null;
				if (response == null || string.IsNullOrEmpty(response.Token)) return Describe(text, "Google sign-in was refused.");
				Adopt(response, false);
				return null;
			});
#else
			await Task.CompletedTask;
			return false;
#endif
		}

		/// <summary>Forgets this device's session. The Firebase user stays; signing in again brings it back.</summary>
		public static void SignOut()
		{
			ForgetLocal();
			Changed?.Invoke();
		}

		/// <summary>Deletes the Firebase user itself. Used by tests that create a throwaway guest, and available to a "delete my account" button.</summary>
		public static async Task<bool> DeleteAccountAsync()
		{
			var token = await GetIdTokenAsync();
			if (token == null) return false;
			var (ok, _) = await Post(FirebaseRest.DeleteUrl(Config.ApiKey), FirebaseRest.IdTokenBody(token), "application/json");
			if (ok) SignOut();
			return ok;
		}

		static async Task<bool> Run(Func<Task<string>> work)
		{
			if (Busy) return false;
			Busy = true;
			LastError = "";
			Changed?.Invoke();
			try
			{
				var error = await work();
				if (error != null) LastError = error;
				return error == null;
			}
			catch (Exception exception)
			{
				LastError = exception.Message;
				return false;
			}
			finally
			{
				Busy = false;
				Changed?.Invoke();
			}
		}

		static void Adopt(AuthResponse response, bool? guest)
		{
			_idToken = response.Token;
			_refreshToken = !string.IsNullOrEmpty(response.Refresh) ? response.Refresh : _refreshToken;
			_expires = DateTime.UtcNow.AddSeconds(response.Seconds);
			Uid = !string.IsNullOrEmpty(response.Uid) ? response.Uid : Uid;
			if (!string.IsNullOrEmpty(response.displayName)) DisplayName = response.displayName;
			if (!string.IsNullOrEmpty(response.email)) Email = response.email;
			if (guest.HasValue) IsGuest = guest.Value;

			var storage = EngineStorage.Current;
			storage.SetString(KeyRefresh, _refreshToken);
			storage.SetString(KeyUid, Uid);
			storage.SetString(KeyName, DisplayName);
			storage.SetString(KeyEmail, Email);
			storage.SetBool(KeyAnonymous, IsGuest);
			storage.Save();
			Changed?.Invoke();
		}

		static void ForgetLocal()
		{
			_idToken = "";
			_refreshToken = "";
			_expires = DateTime.MinValue;
			Uid = "";
			DisplayName = "";
			Email = "";
			IsGuest = false;
			var storage = EngineStorage.Current;
			storage.Delete(KeyRefresh);
			storage.Delete(KeyUid);
			storage.Delete(KeyName);
			storage.Delete(KeyEmail);
			storage.Delete(KeyAnonymous);
			storage.Save();
		}

		/// <summary>Forgets the loaded state so the next <see cref="RestoreAsync"/> reads storage again. For tests.</summary>
		public static void ResetForTests()
		{
			_restored = false;
			ForgetLocal();
		}

		/// <summary>Drops what is in memory but keeps what is saved, as a new start of the game would. For tests.</summary>
		public static void SimulateRestartForTests()
		{
			_restored = false;
			_idToken = "";
			_refreshToken = "";
			_expires = DateTime.MinValue;
			Uid = "";
			DisplayName = "";
			Email = "";
			IsGuest = false;
		}

		static string Describe(string text, string fallback)
		{
			if (string.IsNullOrEmpty(text)) return fallback;
			if (text.Contains("OPERATION_NOT_ALLOWED")) return "This sign-in method is not turned on for the project.";
			if (text.Contains("INVALID_IDP_RESPONSE") || text.Contains("invalid_grant")) return "Google did not accept the sign-in. Try again.";
			if (text.Contains("CREDENTIAL_ALREADY_IN_USE")) return "That Google account is already linked to another player.";
			return fallback;
		}

		// ---------------------------------------------------------------- http

		static async Task<(bool ok, string text)> Post(string url, string body, string contentType)
		{
			using (var request = new UnityWebRequest(url, "POST"))
			{
				request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
				request.downloadHandler = new DownloadHandlerBuffer();
				request.SetRequestHeader("Content-Type", contentType);
				request.timeout = 15;
				await request.SendWebRequest();
				var text = request.downloadHandler != null ? request.downloadHandler.text : "";
				var ok = request.result == UnityWebRequest.Result.Success;
				return (ok, ok ? text : string.IsNullOrEmpty(text) ? request.error : text);
			}
		}

#if UNITY_STANDALONE || UNITY_EDITOR
		/// <summary>Google's installed-app flow: the browser signs in, Google redirects to a one-shot listener on this machine, and the code is exchanged for an ID token.</summary>
		static async Task<string> GoogleIdTokenAsync()
		{
			var verifier = FirebaseRest.NewVerifier();
			var state = FirebaseRest.NewVerifier();
			var port = FreePort();
			var redirect = "http://127.0.0.1:" + port + "/";

			using (var listener = new HttpListener())
			{
				listener.Prefixes.Add(redirect);
				try
				{
					listener.Start();
				}
				catch (Exception exception)
				{
					LastError = "Could not listen for Google's answer: " + exception.Message;
					return null;
				}

				Application.OpenURL(FirebaseRest.GoogleAuthUrl(Config.GoogleDesktopClientId, redirect, FirebaseRest.Challenge(verifier), state));

				var waiting = listener.GetContextAsync();
				var first = await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromMinutes(3)));
				if (first != waiting) { LastError = "Google sign-in timed out."; return null; }

				var context = await waiting;
				var query = context.Request.QueryString;
				var code = query["code"];
				var error = query["error"];
				var answer = Encoding.UTF8.GetBytes("<html><body style=\"font-family:sans-serif;background:#121111;color:#eee;text-align:center;padding-top:20vh\"><h2>"
					+ (code != null && query["state"] == state ? "Signed in. You can close this window and go back to the game." : "Sign-in did not finish. You can close this window.")
					+ "</h2></body></html>");
				context.Response.ContentType = "text/html; charset=utf-8";
				context.Response.OutputStream.Write(answer, 0, answer.Length);
				context.Response.Close();

				if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code) || query["state"] != state)
				{
					LastError = error == "access_denied" ? "Google sign-in was cancelled." : "Google sign-in did not finish.";
					return null;
				}

				var (ok, text) = await Post(FirebaseRest.GoogleTokenUrl, FirebaseRest.GoogleExchangeBody(Config.GoogleDesktopClientId, Config.GoogleDesktopClientSecret, code, verifier, redirect), "application/x-www-form-urlencoded");
				var response = ok ? AuthResponse.Parse(text) : null;
				if (response == null || string.IsNullOrEmpty(response.id_token))
				{
					LastError = Describe(text, "Google did not return a sign-in.");
					return null;
				}
				return response.id_token;
			}
		}

		static int FreePort()
		{
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			var port = ((IPEndPoint)listener.LocalEndpoint).Port;
			listener.Stop();
			return port;
		}
#endif
	}
}
