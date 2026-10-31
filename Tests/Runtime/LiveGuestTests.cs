using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ITKamianets.Engine.Account.PlayTests
{
	/// <summary>Talks to the real Firebase project once: makes a throwaway guest, refreshes it, restores it and deletes it again. Run on purpose, it needs the internet.</summary>
	public class LiveGuestTests
	{
		[UnityTest]
		[Explicit("Creates and deletes one real anonymous user in the Firebase project.")]
		public IEnumerator A_guest_signs_in_refreshes_restores_and_is_deleted()
		{
			GameAccount.ResetForTests();
			var signIn = GameAccount.SignInAsGuestAsync();
			yield return new WaitUntil(() => signIn.IsCompleted);
			Assert.IsTrue(signIn.Result, GameAccount.LastError);
			Assert.IsTrue(GameAccount.IsSignedIn);
			Assert.IsTrue(GameAccount.IsGuest);
			var uid = GameAccount.Uid;
			Assert.IsNotEmpty(uid);

			var token = GameAccount.GetIdTokenAsync();
			yield return new WaitUntil(() => token.IsCompleted);
			Assert.IsNotEmpty(token.Result);

			// A new start of the game restores the same player.
			GameAccount.SimulateRestartForTests();
			Assert.IsFalse(GameAccount.IsSignedIn);
			var restore = GameAccount.RestoreAsync();
			yield return new WaitUntil(() => restore.IsCompleted);
			Assert.AreEqual(uid, GameAccount.Uid);
			Assert.IsTrue(GameAccount.IsGuest);
			var again = GameAccount.GetIdTokenAsync();
			yield return new WaitUntil(() => again.IsCompleted);
			Assert.IsNotEmpty(again.Result);

			// Tidy up: the throwaway user is deleted from the project.
			var delete = GameAccount.DeleteAccountAsync();
			yield return new WaitUntil(() => delete.IsCompleted);
			Assert.IsTrue(delete.Result);
			Assert.IsFalse(GameAccount.IsSignedIn);
		}
	}
}
