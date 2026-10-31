# 3d-unity-account

The player's account for Temnorid games: the same Firebase account Lords uses, so a lord is the same person in every game.

### Logic

`GameAccount` (static) talks to Firebase Authentication through its REST API, so there is no Firebase SDK and it behaves the same on Windows, Android and iOS.

- **Guest:** `SignInAsGuestAsync()` makes an anonymous Firebase user. Nothing to type, nothing to set up.
- **Google (Windows):** `SignInWithGoogleAsync()` opens the browser, Google redirects to a one-shot listener on `127.0.0.1`, the code is exchanged (PKCE) for a Google ID token, and Firebase signs the player in with it. If the device already has a guest, the guest becomes the Google account and keeps its uid, so progress is not lost.
- **Restore:** `RestoreAsync()` brings back the last player from saved tokens (`EngineStorage`) and refreshes them. `GetIdTokenAsync()` returns a valid ID token for other Firebase calls, refreshed when it is about to expire.
- Offline is normal: the remembered player shows at once and the game plays on.
- `GoogleAvailable` is true when the game has the Google Desktop OAuth client: the JSON Google lets you download for the client, as `Assets/Resources/account-oauth.json` (every game git-ignores it, so the secret is never published; builds made on that machine carry it). Keep one copy at `~/.temnorid/google-oauth-desktop.json`; `ShellSceneKit.BuildBoot` copies it into the game. Android and iOS need their own OAuth clients (Android: package name and SHA-1; iOS: client id) and a native sign-in step, so on phones only the guest is offered for now.

`FirebaseConfig.Temnorid` holds the web configuration of the Lords project (`temnorid-arena`). It is public configuration, the same as on the Lords site; access is decided by the Firestore rules, not by the key.

### Status

v0.1: guest sign-in, Google sign-in on Windows (needs the Desktop OAuth client), restore, token refresh, sign out, delete account. 8 edit-mode tests (PKCE against the RFC 7636 example, request building, answer parsing) and an explicit live test that makes a throwaway guest in the real project, refreshes and restores it, and deletes it.

### Roadmap

- Google sign-in on Android and iOS (native sign-in, then the same `signInWithIdp` call).
- Firestore reads and writes with the ID token, for the link between Craft and Lords.
