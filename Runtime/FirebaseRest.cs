using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace ITKamianets.Engine.Account
{
	/// <summary>Where the Firebase project is and how a Google sign-in on this device identifies itself. Not secret: it is the same web configuration the Lords site ships.</summary>
	[Serializable]
	public sealed class FirebaseConfig
	{
		public string ApiKey = "";
		public string ProjectId = "";
		/// <summary>OAuth client of type Desktop in the project's Google Cloud, for Google sign-in on Windows. Empty until it is created.</summary>
		public string GoogleDesktopClientId = "";
		public string GoogleDesktopClientSecret = "";

		/// <summary>
		/// The project Lords uses, so a lord signs in to the same account in every game. The Google Desktop client is read from
		/// <c>Assets/Resources/account-oauth.json</c> (the JSON Google lets you download for the client); that file stays out of git, so
		/// the secret is never published, and the builds made on this machine carry it. Without the file there is no Google button.
		/// </summary>
		public static FirebaseConfig Temnorid
		{
			get
			{
				var config = new FirebaseConfig { ApiKey = "AIzaSyDwqmZvJj599VIRrgQ6CCXKHSmwzqEeG7o", ProjectId = "temnorid-arena" };
				var file = Resources.Load<TextAsset>("account-oauth");
				if (file != null) OauthFile.Apply(file.text, config);
				return config;
			}
		}
	}

	/// <summary>The JSON Google gives for an OAuth client: <c>{"installed":{"client_id":...,"client_secret":...}}</c>, or the two fields on their own.</summary>
	[Serializable]
	public sealed class OauthFile
	{
		[Serializable]
		public sealed class Client
		{
			public string client_id;
			public string client_secret;
		}

		public Client installed;
		public string client_id;
		public string client_secret;

		public static bool Apply(string json, FirebaseConfig config)
		{
			if (string.IsNullOrWhiteSpace(json)) return false;
			try
			{
				var file = JsonUtility.FromJson<OauthFile>(json);
				var id = file?.installed != null && !string.IsNullOrEmpty(file.installed.client_id) ? file.installed.client_id : file?.client_id;
				var secret = file?.installed != null && !string.IsNullOrEmpty(file.installed.client_secret) ? file.installed.client_secret : file?.client_secret;
				if (string.IsNullOrEmpty(id)) return false;
				config.GoogleDesktopClientId = id;
				config.GoogleDesktopClientSecret = secret ?? "";
				return true;
			}
			catch (ArgumentException)
			{
				return false;
			}
		}
	}

	/// <summary>The parts of the Firebase and Google REST answers this package reads. Field names follow the services so <see cref="JsonUtility"/> fills them.</summary>
	[Serializable]
	public sealed class AuthResponse
	{
		// signUp and signInWithIdp
		public string idToken;
		public string refreshToken;
		public string expiresIn;
		public string localId;
		public string email;
		public string displayName;
		public string photoUrl;
		// token refresh
		public string id_token;
		public string refresh_token;
		public string expires_in;
		public string user_id;
		// Google token exchange error
		public string error;
		public string error_description;

		public string Token => !string.IsNullOrEmpty(idToken) ? idToken : id_token;
		public string Refresh => !string.IsNullOrEmpty(refreshToken) ? refreshToken : refresh_token;
		public string Uid => !string.IsNullOrEmpty(localId) ? localId : user_id;
		public int Seconds => int.TryParse(!string.IsNullOrEmpty(expiresIn) ? expiresIn : expires_in, out var seconds) ? seconds : 3600;

		public static AuthResponse Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return null;
			try
			{
				return JsonUtility.FromJson<AuthResponse>(json);
			}
			catch (ArgumentException)
			{
				return null;
			}
		}
	}

	[Serializable]
	public sealed class LookupResponse
	{
		[Serializable]
		public sealed class Provider
		{
			public string providerId;
		}

		[Serializable]
		public sealed class User
		{
			public string localId;
			public string email;
			public string displayName;
			public string photoUrl;
			public Provider[] providerUserInfo = Array.Empty<Provider>();
		}

		public User[] users = Array.Empty<User>();

		public static User ParseFirst(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return null;
			try
			{
				var response = JsonUtility.FromJson<LookupResponse>(json);
				return response != null && response.users != null && response.users.Length > 0 ? response.users[0] : null;
			}
			catch (ArgumentException)
			{
				return null;
			}
		}
	}

	/// <summary>Request addresses and bodies for the Firebase Authentication REST API, and the PKCE values Google's installed-app sign-in needs. Pure, so it is unit tested.</summary>
	public static class FirebaseRest
	{
		public static string SignUpUrl(string key) => "https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=" + Uri.EscapeDataString(key);
		public static string SignInWithIdpUrl(string key) => "https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key=" + Uri.EscapeDataString(key);
		public static string LookupUrl(string key) => "https://identitytoolkit.googleapis.com/v1/accounts:lookup?key=" + Uri.EscapeDataString(key);
		public static string DeleteUrl(string key) => "https://identitytoolkit.googleapis.com/v1/accounts:delete?key=" + Uri.EscapeDataString(key);
		public static string RefreshUrl(string key) => "https://securetoken.googleapis.com/v1/token?key=" + Uri.EscapeDataString(key);
		public const string GoogleTokenUrl = "https://oauth2.googleapis.com/token";

		public static string AnonymousBody() => "{\"returnSecureToken\":true}";

		/// <summary>Signs in with a Google ID token. With <paramref name="linkIdToken"/> (a guest's token) the guest account becomes the Google account and keeps its uid.</summary>
		public static string IdpBody(string googleIdToken, string linkIdToken = null)
		{
			var post = "id_token=" + Uri.EscapeDataString(googleIdToken) + "&providerId=google.com";
			var body = new StringBuilder("{\"postBody\":\"").Append(post).Append("\",\"requestUri\":\"http://localhost\",\"returnSecureToken\":true,\"returnIdpCredential\":true");
			if (!string.IsNullOrEmpty(linkIdToken)) body.Append(",\"idToken\":\"").Append(linkIdToken).Append('"');
			return body.Append('}').ToString();
		}

		public static string IdTokenBody(string idToken) => "{\"idToken\":\"" + idToken + "\"}";

		public static string RefreshBody(string refreshToken) => "grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(refreshToken);

		public static string GoogleAuthUrl(string clientId, string redirect, string challenge, string state)
		{
			return "https://accounts.google.com/o/oauth2/v2/auth?client_id=" + Uri.EscapeDataString(clientId)
				+ "&redirect_uri=" + Uri.EscapeDataString(redirect)
				+ "&response_type=code&scope=" + Uri.EscapeDataString("openid email profile")
				+ "&code_challenge=" + challenge + "&code_challenge_method=S256&state=" + Uri.EscapeDataString(state);
		}

		public static string GoogleExchangeBody(string clientId, string secret, string code, string verifier, string redirect)
		{
			var body = new StringBuilder("grant_type=authorization_code&code=").Append(Uri.EscapeDataString(code))
				.Append("&client_id=").Append(Uri.EscapeDataString(clientId))
				.Append("&redirect_uri=").Append(Uri.EscapeDataString(redirect))
				.Append("&code_verifier=").Append(Uri.EscapeDataString(verifier));
			if (!string.IsNullOrEmpty(secret)) body.Append("&client_secret=").Append(Uri.EscapeDataString(secret));
			return body.ToString();
		}

		// ---- PKCE ----

		public static string NewVerifier()
		{
			var bytes = new byte[48];
			using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
			return Base64Url(bytes);
		}

		public static string Challenge(string verifier)
		{
			using (var sha = SHA256.Create()) return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
		}

		public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}
}
