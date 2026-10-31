using System;
using System.Text;
using NUnit.Framework;

namespace ITKamianets.Engine.Account.Tests
{
	public class AccountTests
	{
		[Test]
		public void The_pkce_challenge_is_the_sha256_of_the_verifier_in_base64url()
		{
			// The example from RFC 7636, appendix B.
			Assert.AreEqual("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", FirebaseRest.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
		}

		[Test]
		public void A_new_verifier_is_long_url_safe_and_different_each_time()
		{
			var a = FirebaseRest.NewVerifier();
			var b = FirebaseRest.NewVerifier();
			Assert.GreaterOrEqual(a.Length, 43);
			Assert.LessOrEqual(a.Length, 128);
			Assert.AreNotEqual(a, b);
			StringAssert.DoesNotContain("+", a);
			StringAssert.DoesNotContain("/", a);
			StringAssert.DoesNotContain("=", a);
		}

		[Test]
		public void Requests_are_built_for_the_right_endpoints()
		{
			StringAssert.StartsWith("https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=abc", FirebaseRest.SignUpUrl("abc"));
			StringAssert.StartsWith("https://securetoken.googleapis.com/v1/token?key=abc", FirebaseRest.RefreshUrl("abc"));
			Assert.AreEqual("grant_type=refresh_token&refresh_token=r%2F1", FirebaseRest.RefreshBody("r/1"));
			StringAssert.Contains("\"returnSecureToken\":true", FirebaseRest.AnonymousBody());
		}

		[Test]
		public void A_google_sign_in_can_link_the_guest()
		{
			var plain = FirebaseRest.IdpBody("g.token");
			StringAssert.Contains("id_token=g.token&providerId=google.com", plain);
			StringAssert.DoesNotContain("\"idToken\"", plain);
			var linked = FirebaseRest.IdpBody("g.token", "guest.token");
			StringAssert.Contains("\"idToken\":\"guest.token\"", linked);
		}

		[Test]
		public void The_google_exchange_carries_the_verifier_and_the_secret_only_when_there_is_one()
		{
			var body = FirebaseRest.GoogleExchangeBody("cid", "sec", "the code", "ver", "http://127.0.0.1:5/");
			StringAssert.Contains("code=the%20code", body);
			StringAssert.Contains("code_verifier=ver", body);
			StringAssert.Contains("client_secret=sec", body);
			StringAssert.DoesNotContain("client_secret", FirebaseRest.GoogleExchangeBody("cid", "", "c", "v", "http://127.0.0.1:5/"));
			StringAssert.Contains("code_challenge_method=S256", FirebaseRest.GoogleAuthUrl("cid", "http://127.0.0.1:5/", "chal", "st"));
		}

		[Test]
		public void Firebase_answers_are_read_in_both_shapes()
		{
			var signUp = AuthResponse.Parse("{\"idToken\":\"A\",\"refreshToken\":\"B\",\"expiresIn\":\"3600\",\"localId\":\"uid1\",\"email\":\"a@b.c\",\"displayName\":\"Ann\"}");
			Assert.AreEqual("A", signUp.Token);
			Assert.AreEqual("B", signUp.Refresh);
			Assert.AreEqual("uid1", signUp.Uid);
			Assert.AreEqual(3600, signUp.Seconds);

			var refresh = AuthResponse.Parse("{\"id_token\":\"C\",\"refresh_token\":\"D\",\"expires_in\":\"1800\",\"user_id\":\"uid2\"}");
			Assert.AreEqual("C", refresh.Token);
			Assert.AreEqual("D", refresh.Refresh);
			Assert.AreEqual("uid2", refresh.Uid);
			Assert.AreEqual(1800, refresh.Seconds);

			Assert.IsNull(AuthResponse.Parse(""));
			Assert.IsNull(AuthResponse.Parse("<html>"));
		}

		[Test]
		public void The_lookup_tells_a_guest_from_a_google_user()
		{
			var user = LookupResponse.ParseFirst("{\"users\":[{\"localId\":\"u\",\"email\":\"a@b.c\",\"providerUserInfo\":[{\"providerId\":\"google.com\"}]}]}");
			Assert.AreEqual("u", user.localId);
			Assert.AreEqual("google.com", user.providerUserInfo[0].providerId);
			Assert.IsNull(LookupResponse.ParseFirst("{\"users\":[]}"));
		}

		[Test]
		public void The_google_client_is_read_from_the_downloaded_json_in_either_shape()
		{
			var config = new FirebaseConfig();
			Assert.IsTrue(OauthFile.Apply("{\"installed\":{\"client_id\":\"id1\",\"client_secret\":\"s1\"}}", config));
			Assert.AreEqual("id1", config.GoogleDesktopClientId);
			Assert.AreEqual("s1", config.GoogleDesktopClientSecret);
			Assert.IsTrue(OauthFile.Apply("{\"client_id\":\"id2\",\"client_secret\":\"s2\"}", config));
			Assert.AreEqual("id2", config.GoogleDesktopClientId);
			Assert.IsFalse(OauthFile.Apply("", config));
			Assert.IsFalse(OauthFile.Apply("{\"installed\":{}}", config));
			Assert.IsFalse(OauthFile.Apply("<html>", config));
		}

		[Test]
		public void No_google_client_means_no_google_button()
		{
			var before = GameAccount.Config;
			try
			{
				GameAccount.Config = new FirebaseConfig { ApiKey = "k" };
				Assert.IsFalse(GameAccount.GoogleAvailable);
				GameAccount.Config = new FirebaseConfig { ApiKey = "k", GoogleDesktopClientId = "id" };
				Assert.IsTrue(GameAccount.GoogleAvailable);
			}
			finally
			{
				GameAccount.Config = before;
			}
		}
	}
}
