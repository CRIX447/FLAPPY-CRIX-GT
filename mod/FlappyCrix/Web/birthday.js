/* Flappy Crix's birthday — one set of rules for every page that needs them.
 *
 * Flappy Crix first went up on 18 March 2026. From 2027 the whole of March
 * (Sydney time, like every season) is its birthday month: the game defaults
 * to the original — /flappycrix-og — and players can switch between the
 * original and the new one whenever they like. Playing the new game on
 * 18 March itself gets a free Golden Party Hat.
 *
 * Easter keeps its own week when it falls in March (Palm Sunday to Easter
 * Monday), so its egg hunt is never lost — except on the 18th, which is
 * always the birthday.
 *
 * Loaded synchronously at the top of /flappycrix and /flappycrix-og, before
 * anything is drawn, so a page that is about to be swapped never shows.
 * It has no dependencies and must never throw.
 */
(function () {
    'use strict';
    var BORN = 2026;
    var PREVIEW_KEY = 'crix_season_preview_v1';   // staff: just for me
    var FORCED_KEY  = 'crix_season_force_v1';     // staff: for everyone
    var PICK_KEY    = 'crix_bday_pick_v1';        // original or new, this year

    // Today in Sydney: { y, m (1-12), d }
    function sydney(date) {
        var n = date || new Date();
        try {
            var p = {};
            new Intl.DateTimeFormat('en-AU', { timeZone: 'Australia/Sydney', year: 'numeric', month: 'numeric', day: 'numeric' })
                .formatToParts(n).forEach(function (x) { p[x.type] = x.value; });
            return { y: +p.year, m: +p.month, d: +p.day };
        } catch (e) {
            return { y: n.getFullYear(), m: n.getMonth() + 1, d: n.getDate() };
        }
    }

    // Anonymous Gregorian computus — the same one the game uses.
    function easter(y) {
        var a = y % 19, b = Math.floor(y / 100), c = y % 100, d = Math.floor(b / 4), e = b % 4,
            f = Math.floor((b + 8) / 25), g = Math.floor((b - f + 1) / 3),
            h = (19 * a + b - d - g + 15) % 30, i = Math.floor(c / 4), k = c % 4,
            l = (32 + 2 * e + 2 * i - h - k) % 7, m = Math.floor((a + 11 * h + 22 * l) / 451),
            v = h + l - 7 * m + 114;
        return Date.UTC(y, Math.floor(v / 31) - 1, (v % 31) + 1);
    }
    function easterWeek(t) {
        var e = easter(t.y), day = 86400000, today = Date.UTC(t.y, t.m - 1, t.d);
        return today >= e - 7 * day && today <= e + day;
    }

    // Is the birthday month on, by the calendar alone?
    function inSeason(t) {
        t = t || sydney();
        if (t.y <= BORN || t.m !== 3) return false;
        return t.d === 18 || !easterWeek(t);
    }
    // The one day the party hat is given out
    function isHatDay(t) {
        t = t || sydney();
        return t.y > BORN && t.m === 3 && t.d === 18;
    }
    // Which birthday this is (or the next one, when staff turn it on early)
    function number(t) {
        t = t || sydney();
        return Math.max(1, t.y - BORN + (t.m > 3 ? 1 : 0));
    }

    function ordinal(n) {
        var sfx = ['th', 'st', 'nd', 'rd'], v = n % 100;
        return n + (sfx[(v - 20) % 10] || sfx[v] || sfx[0]);
    }

    function readForce(key) {
        try { return JSON.parse(localStorage.getItem(key) || '{}').force || ''; } catch (e) { return ''; }
    }
    /* Is it on, and why. The same order the game uses for every season:
       a staff member's just-for-me setting, then the one for everyone, then
       the calendar.
         ''          not on
         'calendar'  it is March
         'everyone'  staff switched it on for all
         'me'        staff switched it on just for themselves */
    function state() {
        var mine = readForce(PREVIEW_KEY), all = readForce(FORCED_KEY);
        var pick = mine || all;
        if (pick === 'birthday') return mine === 'birthday' ? 'me' : 'everyone';
        if (pick && pick !== 'auto') return '';          // 'off', or another season forced
        return inSeason() ? 'calendar' : '';
    }

    // The player's choice of game for this birthday: 'old', 'new' or ''.
    // Kept per year, so next March starts on the original again.
    function getPick() {
        try {
            var v = JSON.parse(localStorage.getItem(PICK_KEY) || '{}');
            return v.y === sydney().y ? (v.pick || '') : '';
        } catch (e) { return ''; }
    }
    function setPick(p) {
        try { localStorage.setItem(PICK_KEY, JSON.stringify({ y: sydney().y, pick: p })); } catch (e) {}
    }

    // Should /flappycrix send this player to the original right now?
    function goOld() { return !!state() && getPick() !== 'new'; }

    // Two pages that disagree must never bounce a browser back and forth
    // forever: four hops inside half a minute and it stays where it is.
    function hop() {
        try {
            var now = Date.now(), k = 'crix_bday_hops';
            var hops = JSON.parse(sessionStorage.getItem(k) || '[]').filter(function (t) { return now - t < 30000; });
            if (hops.length >= 4) return false;
            hops.push(now);
            sessionStorage.setItem(k, JSON.stringify(hops));
        } catch (e) {}
        return true;
    }

    window.CrixBirthday = {
        BORN: BORN, PREVIEW_KEY: PREVIEW_KEY, FORCED_KEY: FORCED_KEY, PICK_KEY: PICK_KEY,
        sydney: sydney, inSeason: inSeason, isHatDay: isHatDay, number: number, ordinal: ordinal,
        state: state, getPick: getPick, setPick: setPick, goOld: goOld, hop: hop
    };
})();
