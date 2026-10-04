/* ============================================================
   FLAPPY CRIX — PlayFab Manager
   ------------------------------------------------------------
   Handles: login, currency, catalog, inventory, progress saving,
            player roles/tags, and player reporting.

   Title ID goes in api.json under  "playfab": { "titleId": "XXXXX" }
   (Get it from PlayFab Game Manager → Title Settings → API Features)

   IMPORTANT — what a browser is allowed to do:
     Safe from the client:  login, read catalog, read inventory,
                            PurchaseItem, ReportPlayer, read/write own data.
     NOT safe / blocked:    granting currency, banning, editing other
                            players. Those need a secret key and must live
                            in CloudScript (see cloudscript.js).
   ============================================================ */

const PLAYFAB_MANAGER_VERSION = '2026.08.20-dm';
const PlayFabManager = (() => {
    let titleId = null;
    let sessionTicket = null;
    let playFabId = null;
    let ready = false;
    let currencyCode = 'CN';          // overridden from api.json
    let roles = [];                   // e.g. ['OWNER'] or ['MOD']
    let status = 'not_started';       // machine-readable state
    let statusDetail = '';            // human-readable reason

    const api = (endpoint, body, useAuth = true) => {
        if (!titleId) return Promise.reject(new Error('PlayFab titleId not set'));
        const headers = { 'Content-Type': 'application/json' };
        if (useAuth) {
            if (!sessionTicket) return Promise.reject(new Error('Not logged in to PlayFab'));
            headers['X-Authorization'] = sessionTicket;
        }
        return fetch(`https://${titleId}.playfabapi.com${endpoint}`, {
            method: 'POST', headers, body: JSON.stringify(body)
        }).then(async res => {
            const json = await res.json().catch(() => ({}));
            if (!res.ok) {
                const err = new Error(json.errorMessage || `PlayFab ${res.status}`);
                err.playfabError = json.error;
                err.errorCode = json.errorCode;
                err.raw = json;
                throw err;
            }
            return json.data;
        });
    };

    return {
        get isReady()   { return ready; },
        get playFabId() { return playFabId; },
        get _titleId()  { return titleId; },
        get version()   { return PLAYFAB_MANAGER_VERSION; },
        get _ticket()   { return sessionTicket; },
        get status()    { return status; },
        get statusDetail() { return statusDetail; },

        /* One-line explanation of exactly why login isn't working,
           so the Settings panel can tell the user what to fix. */
        get statusMessage() {
            switch (status) {
                case 'ok':            return null;
                case 'no_title_id':   return 'PlayFab not set up — add your Title ID to api.json';
                case 'no_firebase':   return 'Sign in to the game first';
                case 'configured':    return 'Sign in to get your Player ID';
                case 'account_missing':
                    return 'No PlayFab account yet — enable "Allow client to create account" in PlayFab (see below)';
                case 'login_failed':  return 'PlayFab login failed: ' + statusDetail;
                case 'banned':        return 'This account is banned';
                default:              return 'PlayFab not connected';
            }
        },
        get roles()     { return [...roles]; },
        get currencyCode() { return currencyCode; },

        /* ---- SETUP ------------------------------------------------ */
        async init(config) {
            titleId = config?.titleId || null;
            currencyCode = config?.currencyCode || 'CN';
            if (!titleId || titleId.startsWith('PASTE_') || titleId.trim() === '') {
                status = 'no_title_id';
                statusDetail = 'api.json -> playfab.titleId is still the placeholder';
                console.warn('[PlayFab] ' + statusDetail);
                return false;
            }
            status = 'configured';
            return true;
        },

        /* ---- LOGIN ------------------------------------------------
           customId should be stable per player. We use the Firebase UID
           so PlayFab and Firebase accounts stay linked.               */
        async login(customId, displayName) {
            if (!titleId) { status = 'no_title_id'; return null; }
            if (!customId) {
                status = 'no_firebase';
                statusDetail = 'no Firebase UID to use as PlayFab CustomId';
                return null;
            }
            try {
                const data = await api('/Client/LoginWithCustomID', {
                    TitleId: titleId,
                    CustomId: customId,
                    CreateAccount: true,
                    InfoRequestParameters: {
                        GetUserVirtualCurrency: true,
                        GetUserInventory: true,
                        GetPlayerProfile: true,
                        GetUserData: true,
                        GetUserReadOnlyData: true
                    }
                }, false);

                sessionTicket = data.SessionTicket;
                playFabId     = data.PlayFabId;
                ready         = true;

                roles = this._extractRoles(data.InfoResultPayload || {});
                PlayFabManager._ownedItems =
                    (data.InfoResultPayload?.UserInventory || []).map(i => i.ItemId);
                status = 'ok';
                statusDetail = '';
                console.log('[PlayFab] Signed in. PlayFabId:', playFabId, '| roles:', roles);

                if (displayName) this.setDisplayName(displayName).catch(() => {});
                return data;
            } catch (e) {
                // AccountBanned = 1002. Surfaced so the caller can show a ban screen.
                if (e.playfabError === 'AccountBanned') {
                    status = 'banned'; e.isBan = true; throw e;
                }
                // PlayFab disabled client-side account creation for titles made
                // after 30 Jun 2025. Without it, a first-time login has nothing
                // to log in to. This is by far the most common setup failure.
                if (e.playfabError === 'AccountNotFound' ||
                    e.errorCode === 1001 || e.errorCode === 1002) {
                    status = 'account_missing';
                } else {
                    status = 'login_failed';
                }
                statusDetail = e.message || String(e);
                console.error('[PlayFab] Login failed —', e.playfabError || '', e.message);
                console.error('[PlayFab] Full response:', e.raw);
                return null;
            }
        },

        async setDisplayName(name) {
            return api('/Client/UpdateUserTitleDisplayName', { DisplayName: name.slice(0, 25) });
        },

        /* ---- ROLES ------------------------------------------------
           Roles are catalog items in the player's inventory (role_owner,
           role_dev, role_mod). Those items have NO price, so PurchaseItem
           can never buy them — only a server-side grant can add them.
           ReadOnlyData is still checked as a fallback so older accounts
           set up the previous way keep working.                        */
        _extractRoles(payload) {
            const found = [];
            // Roles granted through the Firestore fallback are merged in by
            // refreshRoles(); this handles the PlayFab-native sources.


            // Primary source: inventory items of class "role"
            const inv = payload.UserInventory || [];
            inv.forEach(item => {
                // role_dev was missing here, so the game's DEV tag could
                // never be granted from PlayFab even though the client knows
                // about it.
                if (item.ItemId === 'role_owner') found.push('OWNER');
                else if (item.ItemId === 'role_dev') found.push('DEV');
                else if (item.ItemId === 'role_mod') found.push('MOD');
            });

            // Fallback: legacy ReadOnlyData roles array
            const ro = payload.UserReadOnlyData || {};
            if (ro.roles?.Value) {
                try {
                    JSON.parse(ro.roles.Value).forEach(r => {
                        if (!found.includes(r)) found.push(r);
                    });
                } catch (e) {}
            }
            return found;
        },

        /* Re-check roles without a full re-login (after a grant). */
        async refreshRoles() {
            const [invData, roData] = await Promise.all([
                api('/Client/GetUserInventory', {}),
                api('/Client/GetUserReadOnlyData', { Keys: ['roles'] }).catch(() => ({ Data: {} }))
            ]);
            roles = this._extractRoles({
                UserInventory: invData.Inventory || [],
                UserReadOnlyData: roData.Data || {}
            });
            // Merge anything granted via the Firestore fallback
            try {
                if (this.fs && playFabId) {
                    const extra = await this.fs.getRoles(playFabId);
                    extra.forEach(r => { if (!roles.includes(r)) roles.push(r); });
                }
            } catch (e) {}
            return [...roles];
        },

        /* ---- CURRENCY --------------------------------------------- */
        async getCurrency() {
            const data = await api('/Client/GetUserInventory', {});
            return data.VirtualCurrency?.[currencyCode] ?? 0;
        },

        /* Spending is client-safe (it can only ever reduce balance). */
        async spendCurrency(amount) {
            return api('/Client/SubtractUserVirtualCurrency', {
                VirtualCurrency: currencyCode, Amount: Math.floor(amount)
            });
        },

        /* Granting must go through CloudScript — the direct grant API
           requires a secret key and is not callable from a browser.    */
        async grantCurrency(amount, reason = 'gameplay') {
            return api('/Client/ExecuteCloudScript', {
                FunctionName: 'grantCoins',
                FunctionParameter: { amount: Math.floor(amount), reason },
                GeneratePlayStreamEvent: true
            }).then(r => {
                if (r.Error) throw new Error(r.Error.Message || 'CloudScript error');
                return r.FunctionResult;
            });
        },

        /* ---- CATALOG / INVENTORY ---------------------------------- */
        async getCatalog(version = 'Main') {
            const data = await api('/Client/GetCatalogItems', { CatalogVersion: version });
            return data.Catalog || [];
        },

        async getInventory() {
            const data = await api('/Client/GetUserInventory', {});
            return {
                items: data.Inventory || [],
                currency: data.VirtualCurrency?.[currencyCode] ?? 0
            };
        },

        async purchaseItem(itemId, price, version = 'Main') {
            return api('/Client/PurchaseItem', {
                CatalogVersion: version,
                ItemId: itemId,
                Price: Math.floor(price),
                VirtualCurrency: currencyCode
            });
        },

        /* ---- PROGRESS SAVING --------------------------------------
           Player-writable data. Anything the player must NOT be able to
           edit (currency, roles, bans) is deliberately not stored here. */
        async saveProgress(progress) {
            return api('/Client/UpdateUserData', {
                Data: { progress: JSON.stringify(progress) },
                Permission: 'Private'
            });
        },

        async loadProgress() {
            const data = await api('/Client/GetUserData', { Keys: ['progress'] });
            const raw = data.Data?.progress?.Value;
            if (!raw) return null;
            try { return JSON.parse(raw); } catch { return null; }
        },

        /* ---- REPORTING (client-safe, shows in PlayFab Game Manager) */
        async reportPlayer(targetPlayFabId, comment) {
            return api('/Client/ReportPlayer', {
                ReporteeId: targetPlayFabId,
                Comment: (comment || '').slice(0, 1000)
            });
        },

        /* ---- OWNER/MOD ACTIONS ------------------------------------
           All of these are CloudScript calls. The server re-checks the
           caller's role, so a player editing this file client-side
           achieves nothing.                                            */
        async adminAction(action, params = {}) {
            return api('/Client/ExecuteCloudScript', {
                FunctionName: 'adminAction',
                FunctionParameter: { action, ...params },
                GeneratePlayStreamEvent: true
            }).then(r => {
                if (r.Error) {
                    const msg = r.Error.Message || 'CloudScript error';
                    if (/no function named/i.test(msg)) {
                        throw new Error('CloudScript is not deployed. Paste cloudscript.js into ' +
                            'PlayFab → Automation → CloudScript and click "Deploy to live".');
                    }
                    throw new Error(msg);
                }
                if (r.FunctionResult?.error) throw new Error(r.FunctionResult.error);
                return r.FunctionResult;
            });
        },

        hasRole(role) { return roles.includes(role); },
        get isOwner()  { return roles.includes('OWNER'); },
        get isMod()    { return roles.includes('OWNER') || roles.includes('MOD'); },

        logout() { sessionTicket = null; playFabId = null; ready = false; roles = []; }
    };
})();

if (typeof window !== 'undefined') {
    window.PlayFabManager = PlayFabManager;
    window.PLAYFAB_MANAGER_VERSION = PLAYFAB_MANAGER_VERSION;
}

/* ============================================================
   FRIENDS / PRESENCE / PROGRESS  (extension)
   Appended so the core object above stays readable.
   ============================================================ */
(function (PFM) {
    if (typeof PFM === 'undefined') return;

    const call = (endpoint, body) => {
        if (!PFM.isReady) return Promise.reject(new Error('Not logged in to PlayFab'));
        return fetch(`https://${PFM._titleId}.playfabapi.com${endpoint}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-Authorization': PFM._ticket },
            body: JSON.stringify(body)
        }).then(async res => {
            const j = await res.json().catch(() => ({}));
            if (!res.ok) {
                const e = new Error(j.errorMessage || `PlayFab ${res.status}`);
                e.playfabError = j.error; e.errorCode = j.errorCode; e.raw = j;
                throw e;
            }
            return j.data;
        });
    };

    /* ---- FRIENDS ---------------------------------------------
       PlayFab tags are how we mark favourites — they survive on
       the server, so favourites follow the account, not the device. */
    PFM.addFriend = function (opts) {
        const body = {};
        if (opts.playFabId)   body.FriendPlayFabId = opts.playFabId;
        if (opts.displayName) body.FriendTitleDisplayName = opts.displayName;
        if (opts.username)    body.FriendUsername = opts.username;
        if (opts.email)       body.FriendEmail = opts.email;
        return call('/Client/AddFriend', body);
    };

    PFM.removeFriend = function (playFabId) {
        return call('/Client/RemoveFriend', { FriendPlayFabId: playFabId });
    };

    PFM.getFriends = function () {
        return call('/Client/GetFriendsList', {
            ProfileConstraints: {
                ShowDisplayName: true,
                ShowAvatarUrl: true,
                ShowLastLogin: true,
                ShowStatistics: true
            }
        }).then(d => (d.Friends || []).map(f => ({
            playFabId:   f.FriendPlayFabId,
            displayName: f.TitleDisplayName || f.Profile?.DisplayName || 'Player',
            avatarUrl:   f.Profile?.AvatarUrl || null,
            lastLogin:   f.Profile?.LastLogin || null,
            tags:        f.Tags || [],
            favourite:   (f.Tags || []).includes('favourite'),
            statistics:  f.Profile?.Statistics || []
        })));
    };

    /* ---- FRIEND REQUESTS ----
       All four go through CloudScript because PlayFab's client AddFriend
       only writes to your own list — the other person would never see it. */
    PFM.sendFriendRequest = function (targetPlayFabId) {
        return PFM.adminAction ? cloud('sendFriendRequest', { targetPlayFabId })
                               : Promise.reject(new Error('not ready'));
    };
    PFM.respondToFriendRequest = function (requesterPlayFabId, accept) {
        return cloud('respondToFriendRequest', { requesterPlayFabId, accept: !!accept });
    };
    PFM.cancelFriendRequest = function (targetPlayFabId) {
        return cloud('cancelFriendRequest', { targetPlayFabId });
    };
    PFM.removeFriendBoth = function (targetPlayFabId) {
        return cloud('removeFriendBoth', { targetPlayFabId });
    };

    function cloud(fn, params) {
        return call('/Client/ExecuteCloudScript', {
            FunctionName: fn,
            FunctionParameter: params,
            GeneratePlayStreamEvent: true
        }).then(r => {
            if (r.Error) throw new Error(r.Error.Message || 'CloudScript error');
            if (r.FunctionResult && r.FunctionResult.error) throw new Error(r.FunctionResult.error);
            return r.FunctionResult;
        });
    }

    /* Friends split by request state. */
    PFM.getFriendsSplit = function () {
        return PFM.getFriends().then(all => ({
            friends:  all.filter(f => !f.tags.includes('incoming') && !f.tags.includes('outgoing')),
            incoming: all.filter(f =>  f.tags.includes('incoming')),
            outgoing: all.filter(f =>  f.tags.includes('outgoing'))
        }));
    };

    /* Favourites are stored as a PlayFab friend tag. */
    PFM.setFavourite = function (playFabId, isFav) {
        // Preserve 'friend' — SetFriendTags replaces the whole list
        return call('/Client/SetFriendTags', {
            FriendPlayFabId: playFabId,
            Tags: isFav ? ['friend', 'favourite'] : ['friend']
        });
    };

    /* ---- PRESENCE --------------------------------------------
       Stored as Public UserData so it persists across sessions.
       Live status for players in your current room comes over
       Photon instead — see broadcastPresence() in game.html.     */
    PFM.setPresence = function (status) {
        return call('/Client/UpdateUserData', {
            Data: { presence: status, presenceAt: String(Date.now()) },
            Permission: 'Public'
        });
    };

    /* ---- PROGRESS (level / RP / stats) ------------------------
       Statistics are used rather than UserData because they can be
       leaderboard-ranked later without a migration.                */
    PFM.saveStats = function (stats) {
        const updates = Object.entries(stats).map(([k, v]) => ({
            StatisticName: k, Value: Math.floor(v)
        }));
        return call('/Client/UpdatePlayerStatistics', { Statistics: updates });
    };

    PFM.loadStats = function (names) {
        return call('/Client/GetPlayerStatistics', { StatisticNames: names })
            .then(d => {
                const out = {};
                (d.Statistics || []).forEach(s => { out[s.StatisticName] = s.Value; });
                return out;
            });
    };
})(typeof PlayFabManager !== 'undefined' ? PlayFabManager : undefined);


/* ============================================================
   FRIEND CODES / AVATAR / PRIVACY  (extension 2)
   ------------------------------------------------------------
   Friend codes work by baking a short code into the PlayFab
   display name as "Name#AB12". PlayFab's AddFriend already
   supports exact display-name lookup, so no custom backend is
   needed — the code IS part of the name.
   ============================================================ */
(function (PFM) {
    if (typeof PFM === 'undefined') return;

    const call = (endpoint, body) => {
        if (!PFM.isReady) return Promise.reject(new Error('Not logged in to PlayFab'));
        return fetch(`https://${PFM._titleId}.playfabapi.com${endpoint}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-Authorization': PFM._ticket },
            body: JSON.stringify(body)
        }).then(async res => {
            const j = await res.json().catch(() => ({}));
            if (!res.ok) {
                const e = new Error(j.errorMessage || `PlayFab ${res.status}`);
                e.playfabError = j.error; e.errorCode = j.errorCode; e.raw = j;
                throw e;
            }
            return j.data;
        });
    };

    /* Deterministic 4-character code derived from the PlayFab ID, so the
       same account always produces the same code. Ambiguous characters
       (0/O, 1/I) are excluded so codes are easy to read out loud. */
    PFM.friendCodeFor = function (playFabId) {
        const ALPHA = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
        if (!playFabId) return null;
        let h = 0;
        for (let i = 0; i < playFabId.length; i++) {
            h = ((h << 5) - h + playFabId.charCodeAt(i)) | 0;
        }
        h = Math.abs(h);
        let code = '';
        for (let i = 0; i < 4; i++) { code += ALPHA[h % ALPHA.length]; h = Math.floor(h / ALPHA.length) + 7; }
        return code;
    };

    /* Sets the display name to "Name#CODE" so friend lookups work. */
    PFM.setNameWithCode = async function (baseName) {
        const code = PFM.friendCodeFor(PFM.playFabId);
        if (!code) return null;
        const clean = String(baseName).replace(/#.*$/, '').trim().slice(0, 18);
        const full = `${clean}#${code}`;
        await call('/Client/UpdateUserTitleDisplayName', { DisplayName: full });
        return full;
    };

    /* Add by the full "Name#CODE" string. */
    PFM.addFriendByCode = function (nameWithCode) {
        return call('/Client/AddFriend', { FriendTitleDisplayName: nameWithCode.trim() });
    };

    /* Does the player own a given catalog item? Cached from login,
       refreshable after a purchase. */
    PFM._ownedItems = [];
    PFM.refreshInventory = function () {
        return call('/Client/GetUserInventory', {}).then(d => {
            PFM._ownedItems = (d.Inventory || []).map(i => i.ItemId);
            return { items: PFM._ownedItems, currency: d.VirtualCurrency || {} };
        });
    };
    PFM.owns = function (itemId) { return PFM._ownedItems.includes(itemId); };

    /* Avatar. PlayFab stores a URL — we don't host uploads. */
    PFM.setAvatar = function (url) {
        return call('/Client/UpdateAvatarUrl', { ImageUrl: url });
    };

    /* Privacy preference. Stored public so a lookup can honour it. */
    PFM.setAllowFriendRequests = function (allow) {
        return call('/Client/UpdateUserData', {
            Data: { allowFriendRequests: allow ? 'true' : 'false' },
            Permission: 'Public'
        });
    };

    PFM.getMyPrivacy = function () {
        return call('/Client/GetUserData', { Keys: ['allowFriendRequests'] })
            .then(d => d.Data?.allowFriendRequests?.Value !== 'false');
    };
})(typeof PlayFabManager !== 'undefined' ? PlayFabManager : undefined);


/* ============================================================
   FRIEND REQUESTS / INVITES  (extension 3)
   All of these call CloudScript, because writing to another
   player's data requires the server.
   ============================================================ */
(function (PFM) {
    if (typeof PFM === 'undefined') return;

    const cs = (fn, params = {}) => {
        if (!PFM.isReady) return Promise.reject(new Error('Not logged in to PlayFab'));
        return fetch(`https://${PFM._titleId}.playfabapi.com/Client/ExecuteCloudScript`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-Authorization': PFM._ticket },
            body: JSON.stringify({ FunctionName: fn, FunctionParameter: params, GeneratePlayStreamEvent: false })
        })
        .then(r => r.json())
        .then(j => {
            const d = j.data;
            if (!d) throw new Error(j.errorMessage || 'CloudScript call failed');
            if (d.Error) {
                const msg = d.Error.Message || 'CloudScript error';
                if (/no function named/i.test(msg)) {
                    throw new Error(
                        `"${fn}" is not deployed to PlayFab. Paste cloudscript.js into ` +
                        `Game Manager → Automation → CloudScript, then click "Deploy to live" ` +
                        `(saving a revision alone is not enough).`);
                }
                throw new Error(msg);
            }
            const res = d.FunctionResult || {};
            if (res.error) throw new Error(res.error);
            return res;
        });
    };

    PFM.cloudScriptVersion   = ()            => cs('version');
    PFM.sendFriendRequest    = id            => cs('sendFriendRequest', { targetPlayFabId: id });
    PFM.cancelFriendRequest  = id            => cs('cancelFriendRequest', { targetPlayFabId: id });
    PFM.removeFriendBoth     = id            => cs('removeFriendBoth', { targetPlayFabId: id });
    PFM.blockPlayer          = id            => cs('blockPlayer', { targetPlayFabId: id });
    PFM.unblockPlayer        = id            => cs('unblockPlayer', { targetPlayFabId: id });
    PFM.getFriendRequests    = ()            => cs('getFriendRequests').then(r => r.requests || []);
    PFM.respondFriendRequest = (id, accept)  => cs('respondToFriendRequest', { requesterPlayFabId: id, accept });
    PFM.sendInvite           = (id, code, mode) => cs('sendInvite', { targetPlayFabId: id, roomCode: code, mode });
    PFM.getInvites           = ()            => cs('getInvites').then(r => r.invites || []);
    PFM.csVersion            = ()            => cs('version');
    // Calls any handler by name — used by the console's health check
    PFM.callHandler          = (fn, params)  => cs(fn, params || {});
    PFM.clearInvites         = ()            => cs('clearInvites');
})(typeof PlayFabManager !== 'undefined' ? PlayFabManager : undefined);


/* ============================================================
   FIRESTORE FALLBACK
   ------------------------------------------------------------
   Friend requests and role grants normally run through CloudScript,
   because writing to another player's PlayFab data needs a server.
   Firestore can do the same job with security rules, and it needs
   no deployment step — so when CloudScript isn't available these
   take over automatically and everything keeps working.
   ============================================================ */
(function (PFM) {
    if (typeof PFM === 'undefined') return;

    const db = () => {
        if (typeof firebase === 'undefined' || !firebase.firestore)
            throw new Error('Firestore is not available');
        return firebase.firestore();
    };

    // Firestore says "Missing or insufficient permissions" for everything,
    // which tells you nothing. Translate it into the actual fix.
    function explain(e, what) {
        const msg = String(e?.message || e);
        if (/insufficient permissions|permission-denied/i.test(msg)) {
            return new Error(
                `Firestore rejected this (${what}). Publish firestore.rules in ` +
                `Firebase Console → Firestore → Rules → Publish. ` +
                `If you already have, check the rules saved without errors.`);
        }
        if (/requires an index/i.test(msg)) {
            return new Error('Firestore needs an index — open the link in the browser console to create it.');
        }
        return e;
    }
    const uid = () => {
        const u = firebase.auth().currentUser;
        if (!u) throw new Error('Sign in first');
        return u.uid;
    };

    PFM.fs = {
        /* ---- friend requests ---- */
        async sendRequest(targetPlayFabId, targetName) {
            const me = firebase.auth().currentUser;
            await db().collection('friendRequests').doc(`${uid()}_${targetPlayFabId}`).set({
                fromUid: uid(),
                fromPlayFabId: PFM.playFabId || null,
                fromName: me.displayName || 'Player',
                fromPhoto: me.photoURL || null,
                toPlayFabId: targetPlayFabId,
                toName: targetName || null,
                status: 'pending',
                createdAt: firebase.firestore.FieldValue.serverTimestamp()
            });
            return { ok: true };
        },

        async incoming() {
            if (!PFM.playFabId) return [];
            const snap = await db().collection('friendRequests')
                .where('toPlayFabId', '==', PFM.playFabId)
                .where('status', '==', 'pending')
                .get();
            return snap.docs.map(d => ({
                id: d.id,
                from: d.data().fromPlayFabId,
                name: d.data().fromName || 'Player',
                avatar: d.data().fromPhoto || null,
                at: d.data().createdAt?.toMillis?.() || Date.now()
            }));
        },

        async outgoing() {
            const snap = await db().collection('friendRequests')
                .where('fromUid', '==', uid())
                .where('status', '==', 'pending')
                .get();
            return snap.docs.map(d => ({
                id: d.id,
                playFabId: d.data().toPlayFabId,
                displayName: d.data().toName || d.data().toPlayFabId,
                tags: ['outgoing']
            }));
        },

        async respond(requestId, accept) {
            const ref = db().collection('friendRequests').doc(requestId);
            const doc = await ref.get();
            if (!doc.exists) throw new Error('That request no longer exists');
            const data = doc.data();
            if (accept && data.fromPlayFabId) {
                // Adds the sender to MY list. PlayFab's AddFriend only ever
                // writes to the caller's own list, so the sender completes
                // their side separately (see completeAcceptedRequests).
                // This used to be wrapped in a silent catch, which hid the
                // failure and left the accepter with no friend at all.
                try {
                    await PFM.addFriend({ playFabId: data.fromPlayFabId });
                } catch (e) {
                    if (!/already/i.test(e.message)) {
                        throw new Error('Could not add them to your friends list: ' + e.message);
                    }
                }
            }
            await ref.update({ status: accept ? 'accepted' : 'denied' });
            return { ok: true, accepted: accept };
        },

        async cancel(requestId) {
            await db().collection('friendRequests').doc(requestId).delete();
            return { ok: true };
        },

        /* ---- GLOBAL PRESENCE ----
           Previously a friend only showed as online if they happened to be in
           YOUR lobby. A heartbeat document per player gives real presence
           anywhere, and carries the room code so friends can join. */
        async heartbeat(state) {
            const u = firebase.auth().currentUser;
            if (!u || !PFM.playFabId) return;
            const invisible = state.status === 'invisible' || state.status === 'offline';
            await db().collection('presence').doc(PFM.playFabId).set({
                // Kept even while invisible so friends see "last seen 2h ago"
                // rather than nothing at all.
                lastOnline: firebase.firestore.FieldValue.serverTimestamp(),
                invisible,
                uid: u.uid,
                playFabId: PFM.playFabId,
                name: state.name || u.displayName || 'Player',
                photo: state.photo || u.photoURL || null,
                status: state.status || 'online',      // online | dnd | offline
                roomCode: state.roomCode || null,      // null when not in a lobby
                roomMode: state.roomMode || null,
                joinable: !!state.joinable,
                level: state.level || 1,
                at: firebase.firestore.FieldValue.serverTimestamp()
            }, { merge: true });
        },

        async clearPresence() {
            const u = firebase.auth().currentUser;
            if (!u || !PFM.playFabId) return;
            await db().collection('presence').doc(PFM.playFabId)
                      .set({ status: 'offline', roomCode: null, joinable: false,
                             at: firebase.firestore.FieldValue.serverTimestamp() },
                           { merge: true }).catch(() => {});
        },

        /* Live presence for a list of friends. A heartbeat older than 90s is
           treated as offline, so a closed tab doesn't linger as "online". */
        watchPresence(playFabIds, onChange) {
            if (!playFabIds || !playFabIds.length) return () => {};
            const unsubs = [];
            // Firestore 'in' queries cap at 10, so batch them
            for (let i = 0; i < playFabIds.length; i += 10) {
                const batch = playFabIds.slice(i, i + 10);
                unsubs.push(
                    db().collection('presence')
                        .where('playFabId', 'in', batch)
                        .onSnapshot(snap => {
                            const out = {};
                            snap.forEach(d => {
                                const v = d.data();
                                const age = v.at?.toMillis ? Date.now() - v.at.toMillis() : 1e9;
                                const stale = age > 90000;
                                const hidden = v.invisible || v.status === 'invisible';
                                out[v.playFabId] = {
                                    // Someone appearing offline looks offline, and
                                    // their lobby is not exposed either — otherwise
                                    // the setting would be pointless.
                                    status: (stale || hidden || v.status === 'offline') ? 'offline' : v.status,
                                    lastOnline: v.lastOnline?.toMillis ? v.lastOnline.toMillis()
                                              : (v.at?.toMillis ? v.at.toMillis() : null),
                                    roomCode: (stale || hidden) ? null : v.roomCode,
                                    roomMode: hidden ? null : v.roomMode,
                                    joinable: !stale && !hidden && !!v.joinable && v.status !== 'offline',
                                    name: v.name, photo: v.photo, level: v.level
                                };
                            });
                            onChange(out);
                        }, err => console.warn('[Presence]', err.message))
                );
            }
            return () => unsubs.forEach(fn => { try { fn(); } catch(e) {} });
        },

        /* Live listener — Firestore pushes changes instantly, so a request
           arrives in about a second instead of waiting up to 45s for a poll. */
        watchRequests(onChange) {
            const u = firebase.auth().currentUser;
            if (!u || !PFM.playFabId) return () => {};
            const unsubs = [];

            // Incoming requests addressed to me
            unsubs.push(
                db().collection('friendRequests')
                    .where('toPlayFabId', '==', PFM.playFabId)
                    .where('status', '==', 'pending')
                    .onSnapshot(
                        snap => onChange('incoming', snap.docs.map(d => ({
                            id: d.id,
                            from: d.data().fromPlayFabId,
                            name: d.data().fromName || 'Player',
                            avatar: d.data().fromPhoto || null,
                            at: d.data().createdAt?.toMillis?.() || Date.now()
                        }))),
                        err => console.warn('[Friends] incoming listener:', err.message)
                    )
            );

            // Requests I sent being accepted, so the UI updates immediately
            unsubs.push(
                db().collection('friendRequests')
                    .where('fromUid', '==', u.uid)
                    .where('status', '==', 'accepted')
                    .onSnapshot(
                        snap => { if (!snap.empty) onChange('accepted', snap.size); },
                        err => console.warn('[Friends] accepted listener:', err.message)
                    )
            );

            return () => unsubs.forEach(fn => { try { fn(); } catch(e) {} });
        },

        /* Requests the player SENT that have since been accepted. PlayFab's
           AddFriend only writes to the caller's own list, so the sender has to
           add the other person themselves — otherwise only one side sees the
           friendship. Runs on load and after each poll. */
        async completeAccepted() {
            const u = firebase.auth().currentUser;
            if (!u) return 0;
            const snap = await db().collection('friendRequests')
                .where('fromUid', '==', u.uid)
                .where('status', '==', 'accepted')
                .get();
            let done = 0;
            for (const d of snap.docs) {
                const to = d.data().toPlayFabId;
                if (!to) { await d.ref.delete().catch(() => {}); continue; }
                try {
                    await PFM.addFriend({ playFabId: to });
                    done++;
                } catch (e) {
                    if (!/already/i.test(e.message)) continue;   // retry next time
                }
                await d.ref.delete().catch(() => {});   // handshake complete
            }
            return done;
        },

        /* Live count of everyone online, and who's in a joinable lobby.
           Heartbeats older than 90s are ignored so closed tabs drop off. */
        watchOnline(onChange) {
            const cutoff = () => new Date(Date.now() - 90000);
            // A '!=' filter needs a composite index; reading all and filtering
            // in code avoids that and the collection stays small.
            const unsub = db().collection('presence')
                .onSnapshot(snap => {
                    const now = Date.now();
                    const live = [];
                    snap.forEach(d => {
                        const v = d.data();
                        const age = v.at?.toMillis ? now - v.at.toMillis() : 1e9;
                        if (age > 90000) return;                              // stale heartbeat
                        if (v.status === 'offline' || v.invisible) return;    // hidden by choice
                        live.push({
                            playFabId: v.playFabId,
                            name: v.name || 'Player',
                            photo: v.photo || null,
                            level: v.level || 1,
                            status: v.status,
                            roomCode: v.roomCode || null,
                            roomMode: v.roomMode || null,
                            joinable: !!v.joinable
                        });
                    });
                    onChange({
                        total: live.length,
                        inLobby: live.filter(p => p.roomCode).length,
                        joinable: live.filter(p => p.joinable).length,
                        players: live
                    });
                }, err => console.warn('[Online]', err.message));
            return unsub;
        },

        /* ---- FRIEND REMOVAL ----
           PlayFab's RemoveFriend only affects the caller's own list, so the
           other side is asked to drop us via a pending record they act on
           next time they load. */
        async removeFriend(targetPlayFabId) {
            await PFM.removeFriend(targetPlayFabId).catch(() => {});
            const u = firebase.auth().currentUser;
            if (u && PFM.playFabId) {
                await db().collection('friendRemovals')
                    .doc(`${targetPlayFabId}_${PFM.playFabId}`)
                    .set({
                        forPlayFabId: targetPlayFabId,
                        removePlayFabId: PFM.playFabId,
                        byUid: u.uid,
                        at: firebase.firestore.FieldValue.serverTimestamp()
                    }).catch(() => {});
            }
            return { ok: true };
        },

        /* Applies removals other people queued for us. */
        async applyPendingRemovals() {
            if (!PFM.playFabId) return 0;
            const snap = await db().collection('friendRemovals')
                .where('forPlayFabId', '==', PFM.playFabId).get();
            let n = 0;
            for (const d of snap.docs) {
                try {
                    await PFM.removeFriend(d.data().removePlayFabId);
                    n++;
                } catch (e) {}
                await d.ref.delete().catch(() => {});
            }
            return n;
        },

        /* ---- LOBBY INVITES ---- */
        async sendInvite(targetPlayFabId, roomCode, mode) {
            const u = firebase.auth().currentUser;
            await db().collection('invites').doc(`${targetPlayFabId}_${PFM.playFabId}`).set({
                toPlayFabId: targetPlayFabId,
                fromPlayFabId: PFM.playFabId,
                fromUid: u ? u.uid : null,
                name: u?.displayName || 'Player',
                roomCode, mode: mode || 'freemode',
                at: Date.now()
            });
            return { ok: true };
        },

        async getInvites() {
            if (!PFM.playFabId) return [];
            const snap = await db().collection('invites')
                .where('toPlayFabId', '==', PFM.playFabId).get();
            const fresh = [];
            for (const d of snap.docs) {
                const v = d.data();
                // Invites older than 10 minutes are stale
                if (Date.now() - (v.at || 0) > 600000) { await d.ref.delete().catch(() => {}); continue; }
                fresh.push({ id: d.id, from: v.fromPlayFabId, name: v.name,
                             roomCode: v.roomCode, mode: v.mode, at: v.at });
            }
            return fresh;
        },

        async clearInvites() {
            if (!PFM.playFabId) return { ok: true };
            const snap = await db().collection('invites')
                .where('toPlayFabId', '==', PFM.playFabId).get();
            await Promise.all(snap.docs.map(d => d.ref.delete().catch(() => {})));
            return { ok: true };
        },

        /* ---- BANS ----
           A real PlayFab account ban needs the secret key, which a browser
           can never hold. These records live in Firestore instead: only an
           owner can write them, and the game checks them at sign-in and
           before entering multiplayer. */
        async banPlayer(targetPlayFabId, opts) {
            opts = opts || {};
            const hours = opts.hours ? parseInt(opts.hours, 10) : null;
            const expires = hours ? Date.now() + hours * 3600000 : null;
            // Let them know, if they linked Discord
            PFM.fs.queueDM(targetPlayFabId, 'banned', {
                reason: opts.reason || 'Breaking the rules',
                note: opts.note || null,
                expires: expires
            }).catch(() => {});

            await db().collection('bans').doc(targetPlayFabId).set({
                playFabId: targetPlayFabId,
                name: opts.name || null,
                reason: opts.reason || 'Breaking the rules',
                note: opts.note || null,
                bannedBy: PFM.playFabId || uid(),
                bannedByName: firebase.auth().currentUser?.displayName || 'Staff',
                createdAt: firebase.firestore.FieldValue.serverTimestamp(),
                expiresAt: expires,          // null = permanent
                active: true
            });
            return { ok: true, banId: targetPlayFabId, expires };
        },

        async unbanPlayer(targetPlayFabId) {
            await db().collection('bans').doc(targetPlayFabId).delete();
            PFM.fs.queueDM(targetPlayFabId, 'unbanned', {}).catch(() => {});
            return { ok: true };
        },

        /* Returns null when not banned. Expired bans clean themselves up. */
        async checkBan(playFabId) {
            if (!playFabId) return null;
            const d = await db().collection('bans').doc(playFabId).get();
            if (!d.exists) return null;
            const b = d.data();
            if (!b.active) return null;
            if (b.expiresAt && Date.now() > b.expiresAt) {
                await d.ref.delete().catch(() => {});   // served their time
                return null;
            }
            return {
                reason: b.reason || 'Breaking the rules',
                note: b.note || null,
                expires: b.expiresAt || null,
                bannedBy: b.bannedByName || 'Staff'
            };
        },

        async listBans() {
            const snap = await db().collection('bans').get();
            const now = Date.now();
            return snap.docs.map(d => ({ id: d.id, ...d.data() }))
                            .filter(b => !b.expiresAt || b.expiresAt > now);
        },

        /* ---- DM OUTBOX ----
           The bot is the only thing holding a Discord token, so notifications
           are queued here and delivered by it. That also means a DM still
           lands if the bot happened to be offline at the time. */
        async queueDM(targetPlayFabId, kind, data) {
            // Find their Discord id — no link, no notification
            let discordId = null;
            try {
                const snap = await db().collection('users')
                    .where('playFabId', '==', targetPlayFabId).limit(1).get();
                if (!snap.empty) discordId = snap.docs[0].data().discordId || null;
            } catch (e) {}
            if (!discordId) return { queued: false, reason: 'not linked' };

            await db().collection('dmQueue').add({
                discordId, kind,
                playFabId: targetPlayFabId,
                ...data,
                sent: false,
                at: firebase.firestore.FieldValue.serverTimestamp()
            });
            return { queued: true };
        },

        /* ---- LOBBY OVERRIDES ----
           Lets an owner claim host or force-end a match from the console.
           The game watches this and acts on it. */
        async setLobbyOverride(roomCode, data) {
            await db().collection('lobbyOverrides').doc(roomCode).set({
                ...data,
                by: PFM.playFabId || uid(),
                at: firebase.firestore.FieldValue.serverTimestamp()
            }, { merge: true });
            return { ok: true };
        },

        watchLobbyOverride(roomCode, onChange) {
            if (!roomCode) return () => {};
            return db().collection('lobbyOverrides').doc(roomCode)
                .onSnapshot(d => { if (d.exists) onChange(d.data()); },
                            e => console.warn('[Lobby]', e.message));
        },

        clearLobbyOverride(roomCode) {
            return db().collection('lobbyOverrides').doc(roomCode).delete().catch(() => {});
        },

        /* ---- roles ----
           Firestore rules restrict writes here to existing owners, so this is
           as safe as the CloudScript route. */
        async setRole(targetPlayFabId, roles) {
            PFM.fs.queueDM(targetPlayFabId, 'role', { roles }).catch(() => {});
            await db().collection('roles').doc(targetPlayFabId).set({
                roles,
                grantedBy: PFM.playFabId || uid(),
                grantedAt: firebase.firestore.FieldValue.serverTimestamp()
            });
            return { ok: true, roles };
        },

        async getRoles(playFabId) {
            const d = await db().collection('roles').doc(playFabId).get();
            return d.exists ? (d.data().roles || []) : [];
        },

        /* ---- blocks ---- */
        async block(targetPlayFabId) {
            await db().collection('blocks').doc(`${uid()}_${targetPlayFabId}`).set({
                byUid: uid(), byPlayFabId: PFM.playFabId || null,
                blocked: targetPlayFabId,
                at: firebase.firestore.FieldValue.serverTimestamp()
            });
            return { ok: true };
        },
        async unblock(targetPlayFabId) {
            await db().collection('blocks').doc(`${uid()}_${targetPlayFabId}`).delete();
            return { ok: true };
        },
        async blocked() {
            const snap = await db().collection('blocks').where('byUid', '==', uid()).get();
            return snap.docs.map(d => ({
                playFabId: d.data().blocked,
                displayName: d.data().blocked,
                tags: ['blocked']
            }));
        }
    };

    /* ---- automatic routing ----
       Try CloudScript; if it isn't deployed, silently use Firestore. */
    let csAvailable = null;

    async function viaCloudScript(fn, args, fallback) {
        if (csAvailable === false) return fallback();
        try {
            const r = await PFM.callHandler(fn, args);
            csAvailable = true;
            return r;
        } catch (e) {
            if (/not deployed|no function named/i.test(e.message)) {
                if (csAvailable === null) {
                    console.warn('[PlayFab] CloudScript is not deployed — using the Firestore fallback. ' +
                                 'Everything still works; deploy cloudscript.js when convenient.');
                }
                csAvailable = false;
                return fallback();
            }
            throw e;
        }
    }

    const _send   = PFM.sendFriendRequest;
    const _get    = PFM.getFriendRequests;
    const _respond= PFM.respondFriendRequest;
    const _cancel = PFM.cancelFriendRequest;
    const _block  = PFM.blockPlayer;
    const _unblock= PFM.unblockPlayer;

    PFM.sendFriendRequest = (id, name) =>
        viaCloudScript('sendFriendRequest', { targetPlayFabId: id }, () => PFM.fs.sendRequest(id, name));

    PFM.getFriendRequests = () =>
        viaCloudScript('getFriendRequests', {}, () => PFM.fs.incoming())
            .then(r => Array.isArray(r) ? r : (r.requests || []));

    PFM.respondFriendRequest = (id, accept) =>
        viaCloudScript('respondToFriendRequest', { requesterPlayFabId: id, accept },
            async () => {
                // Firestore stores requests by document id
                const list = await PFM.fs.incoming();
                const match = list.find(x => x.from === id || x.id === id);
                if (!match) throw new Error('Request not found');
                return PFM.fs.respond(match.id, accept);
            });

    PFM.cancelFriendRequest = (id) =>
        viaCloudScript('cancelFriendRequest', { targetPlayFabId: id },
            () => PFM.fs.cancel(`${firebase.auth().currentUser.uid}_${id}`));

    PFM.removeFriendBoth = (id) =>
        viaCloudScript('removeFriendBoth', { targetPlayFabId: id }, () => PFM.fs.removeFriend(id));

    PFM.sendInvite = (id, code, mode) =>
        viaCloudScript('sendInvite', { targetPlayFabId: id, roomCode: code, mode },
                       () => PFM.fs.sendInvite(id, code, mode));

    PFM.getInvites = () =>
        viaCloudScript('getInvites', {}, () => PFM.fs.getInvites())
            .then(r => Array.isArray(r) ? r : (r.invites || []));

    PFM.clearInvites = () =>
        viaCloudScript('clearInvites', {}, () => PFM.fs.clearInvites());

    // version has no Firestore equivalent — report it cleanly instead of throwing
    const _csVersion = PFM.csVersion;
    PFM.csVersion = () => _csVersion().catch(e => {
        if (/not deployed|no function named/i.test(e.message)) {
            return { version: 'not deployed', handlers: [], count: 0, fallback: 'firestore' };
        }
        throw e;
    });

    PFM.blockPlayer   = (id) => viaCloudScript('blockPlayer',   { targetPlayFabId: id }, () => PFM.fs.block(id));
    PFM.unblockPlayer = (id) => viaCloudScript('unblockPlayer', { targetPlayFabId: id }, () => PFM.fs.unblock(id));

    // adminAction('setRole') is the one people hit when granting MOD
    const _admin = PFM.adminAction;
    PFM.adminAction = function (action, params) {
        if (action === 'setRole') {
            return viaCloudScript('adminAction', { action, ...params },
                () => PFM.fs.setRole(params.targetPlayFabId, params.roles || []));
        }
        if (action === 'ban') {
            return viaCloudScript('adminAction', { action, ...params },
                () => PFM.fs.banPlayer(params.targetPlayFabId, params));
        }
        if (action === 'unban') {
            return viaCloudScript('adminAction', { action, ...params },
                () => PFM.fs.unbanPlayer(params.banId || params.targetPlayFabId));
        }
        return _admin.call(PFM, action, params);
    };

    // Wrap every fallback method so permission errors explain themselves.
    //
    // This used to declare the wrapper `async`, which meant every method
    // returned a Promise — including the four watch* subscribers, whose
    // return value is the unsubscribe function Firestore hands back. Callers
    // stored that Promise and later did `if (_watcher) _watcher()`, so the
    // second call threw "is not a function" (uncaught in watchLobbyOverride,
    // swallowed by the try in the other three) and, worse, never detached the
    // previous onSnapshot. Every re-watch left another live listener behind.
    // The wrapper now only attaches to the promise when there is one, so
    // subscribers keep returning their unsubscribe synchronously.
    Object.keys(PFM.fs).forEach(k => {
        const orig = PFM.fs[k];
        PFM.fs[k] = function (...args) {
            try {
                const out = orig.apply(PFM.fs, args);
                if (out && typeof out.then === 'function') {
                    return out.catch(e => { throw explain(e, k); });
                }
                return out;
            } catch (e) { throw explain(e, k); }
        };
    });

    PFM.cloudScriptAvailable = () => csAvailable;
})(typeof PlayFabManager !== 'undefined' ? PlayFabManager : undefined);

/* Startup self-check: shouts loudly if a stale cached copy is running. */
(function () {
    if (typeof window === 'undefined') return;
    const need = ['addFriend','getFriends','removeFriend','setFavourite','setPresence','saveStats','loadStats','friendCodeFor','addFriendByCode','setAvatar','setNameWithCode','owns','refreshInventory','sendFriendRequest','getFriendRequests','respondFriendRequest','getInvites','sendFriendRequest','respondToFriendRequest','getFriendsSplit'];
    const missing = need.filter(fn => typeof PlayFabManager[fn] !== 'function');
    if (missing.length) {
        console.error('[PlayFab] STALE FILE — playfab-manager.js is missing:', missing.join(', '),
                      '\nYour browser is running an old cached copy. Hard-refresh with Ctrl+Shift+R (Cmd+Shift+R on Mac).');
    } else {
        console.log('[PlayFab] playfab-manager.js', PLAYFAB_MANAGER_VERSION, '— all methods loaded ✓');
    }
})();
