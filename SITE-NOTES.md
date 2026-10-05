# Notes for crixgamingvr.com (found while building the mod's online features)

Made with AI (Claude by Anthropic), from reading github.com/CRIX447/crix-website. The mod doesn't rely on
any of these problems, but you should know about them.

## Security

1. **Pairing codes can be collected by anyone after approval.** In `api/device-link.js`, `pair-poll` checks
   `d.pending?.requestToken !== requestToken && !d.approved`. Once a request is approved, the `&& !d.approved`
   part lets *any* request token through, so anyone who knows the pairing code can collect the sign-in token
   for that account. Fix: always require the matching `requestToken`.
2. **PlayFab accounts can be logged into with someone else's account id.** The site logs into PlayFab with
   `LoginWithCustomID` and `CustomId = Firebase uid`, and every signed-in user can read every other user's uid
   (the `users` read rule in `firestore.rules`, presence data, friend requests). Knowing a uid is enough to
   log into that player's PlayFab account. Fix: log into PlayFab with a server-issued token (for example
   through a Vercel function that verifies the Firebase ID token and calls PlayFab's server API).

## Also worth knowing

- `users/{uid}` accepts any values from its owner (coins, cosmetics), and ranked points are written by each
  browser to PlayFab. Nothing on the server checks them.
- The Photon app's own hint in the site says it is on the free plan (20 players online at once). Website and
  mod players share that limit.
- If the Firebase web API key is ever restricted to the website's address in Google Cloud, the mod's sign-in
  stops working (the log then says `API_KEY_HTTP_REFERRER_BLOCKED`). A separate key for the mod, limited to
  Identity Toolkit, Token Service and Firestore, would fix that.
- `link.html` says "Go back to Flappy Crix Launcher" after linking; players linking the VR mod see that too.
