# Changelog

## 0.2.0

- `FirestoreRest` (pure: addresses, patch body, `updateMask.fieldPaths`, `currentDocument.updateTime` precondition, Firestore typed values to and from Newtonsoft `JToken`) and `FirestoreClient` (get a document, patch chosen fields of an existing document as the signed-in player; the sender is replaceable for tests). Never creates a document.
- The package now needs `com.unity.nuget.newtonsoft-json`.

## 0.1.0

- `GameAccount`: guest and Google sign-in over Firebase REST, restore, refresh, sign out, delete.
