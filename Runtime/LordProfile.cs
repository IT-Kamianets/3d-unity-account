using System.Threading.Tasks;

namespace ITKamianets.Engine.Account
{
	/// <summary>
	/// The lord of the signed-in player, as the Lords app saved it: <c>users/{uid}/progress/lords</c>, field <c>lord.name</c>. Every game shows this
	/// name, so a player is the same lord everywhere. Read only; the Lords app is the one that writes it.
	/// </summary>
	public static class LordProfile
	{
		public const int MaxNameLength = 40;

		/// <summary>The lord's name, or null when not signed in, offline, the player has no lord yet (a guest, or Lords never opened) or the rules refuse.</summary>
		public static async Task<string> GetNameAsync()
		{
			var name = await GetFieldAsync("name");
			if (string.IsNullOrWhiteSpace(name)) return null;
			name = name.Trim();
			return name.Length > MaxNameLength ? name.Substring(0, MaxNameLength) : name;
		}

		/// <summary>
		/// The class the player gave their lord in Lords (<c>lord.class</c>): <c>warrior</c>, <c>knight</c>, <c>archer</c>, <c>mage</c>, <c>rogue</c> or <c>healer</c>.
		/// Null when not signed in, offline or the lord has none. Games that have classes play the lord's own.
		/// </summary>
		public static async Task<string> GetClassAsync()
		{
			var id = await GetFieldAsync("class");
			return string.IsNullOrWhiteSpace(id) ? null : id.Trim().ToLowerInvariant();
		}

		static async Task<string> GetFieldAsync(string field)
		{
			if (!GameAccount.IsSignedIn || string.IsNullOrEmpty(GameAccount.Uid)) return null;
			var result = await FirestoreClient.ForAccount().GetAsync("users/" + GameAccount.Uid + "/progress/lords");
			if (!result.Ok || result.Document == null) return null;
			return result.Document.Data["lord"]?[field]?.ToString();
		}
	}
}
