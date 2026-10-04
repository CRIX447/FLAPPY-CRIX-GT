/* ============================================================
   FLAPPY CRIX — Chat Filter
   ------------------------------------------------------------
   Replaces profanity and slurs with asterisks.

   Usage:
     ChatFilter.clean("some text")        -> "some ****"
     ChatFilter.isClean("some text")      -> false
     ChatFilter.addWords(["extra","words"])

   Notes on how it catches evasion:
     - Leetspeak is normalised first (@→a, 1→i, 3→e, $→s, 0→o …)
     - Repeated letters collapse (fuuuuck → fuck)
     - Separators inside a word are stripped (f.u.c.k, f-u-c-k, f u c k)
     - Matching is done on the normalised form, but asterisks are applied
       to the ORIGINAL text so spacing and punctuation survive.

   Word-boundary matching is used for short words to avoid false
   positives (the "Scunthorpe problem" — innocent words that contain
   a rude substring). Longer/unambiguous terms match anywhere.
   ============================================================ */

const ChatFilter = (() => {

    // Words safe to match anywhere in a string (unambiguous)
    let ANYWHERE = [
        'fuck','fuk','fuq','phuck','fucking','fucker','motherfucker','mofo',
        'shit','shite','bullshit','dipshit','horseshit',
        'bitch','biatch','btch',
        'cunt','kunt',
        'asshole','arsehole','jackass','dumbass','smartass',
        'bastard','bollocks','wanker','wank','tosser',
        'dickhead','dickwad','prick','knobhead',
        'pussy','twat','minge',
        'whore','slut','skank','hoe',
        'nigger','nigga','niger','nigg','negro',
        'faggot','fagot','fag','dyke','tranny',
        'retard','retarded','spastic','spaz',
        'chink','gook','spic','wetback','beaner','kike','paki','wop','dago',
        'coon','jigaboo','pickaninny','sambo','golliwog',
        'towelhead','raghead','camel jockey','sandnigger',
        'cracker','honky','whitetrash',
        'cock','cocksucker','bellend','knobend',
        'jizz','cum','spunk','wank',
        'blowjob','handjob','rimjob','deepthroat',
        'anal','anus','rectum',
        'boner','dildo','buttplug','fleshlight',
        'porn','porno','pornography','hentai',
        'rape','rapist','molest','molester','pedo','pedophile','paedophile',
        'incest','bestiality',
        'kys','kill yourself','killyourself','neck yourself','neckyourself',
        'nonce','groomer'
    ];

    // Short/ambiguous words — only match as whole words.
    // (e.g. "ass" must not censor "class", "grass", "pass", "assassin")
    let WHOLE_WORD = [
        'ass','arse','tit','tits','titty','titties','boob','boobs',
        'damn','goddamn','hell','crap','piss','pissed','pee',
        'dick','dik','knob','wang','willy','schlong',
        'fanny','snatch','muff','clit','vagina','penis','testicle','scrotum',
        'bang','shag','screw','slag','tart','tramp',
        'homo','queer','lesbo','poof','poofter',
        'jew','yid','gyp','gypsy',
        'mick','kraut','nazi','hitler',
        'std','aids','herpes',
        'stfu','wtf','ffs','omfg','af',
        'cum','jerk','tosspot','git','prat','plonker','muppet',
        'bloody','bugger','sod','crikey'
    ];

    // Words explicitly allowed even if a rule would flag them
    const ALLOWLIST = new Set([
        'class','classic','grass','pass','passed','passing','bass','mass','massive',
        'assassin','assassination','assess','assessment','asset','assets','assign',
        'assist','assistant','associate','association','assume','assumption','assure',
        'compass','embassy','harass','glass','brass','crass','sass','lass',
        'analysis','analyst','analyse','analyze','analytic','analytics','canal',
        'shitake','shiitake','scunthorpe','penistone','lightwater','cockpit',
        'cocktail','peacock','shuttlecock','hancock','wilcox','babcock',
        'titan','titanic','title','titles','titled','constitution','substitute',
        'dickens','dickinson','benedict','medic','predict','verdict','addict',
        'butter','button','buttons','rebuttal','debut','tribute','attribute',
        'document','documents','documentary','argument','instrument','monument',
        'hello','shell','shelter','helmet','wheel','bell','cell','tell','well',
        'therapist','grape','grapes','scrape','drape','rapid','rapidly','therapy',
        'matches','watches','catches','snatches'
    ]);

    // Character substitutions used to dodge filters
    const LEET = {
        '@':'a','4':'a','^':'a','à':'a','á':'a','â':'a','ä':'a','å':'a','α':'a',
        '8':'b','ß':'b','β':'b',
        '(':'c','<':'c','{':'c','¢':'c','ç':'c',
        '3':'e','€':'e','è':'e','é':'e','ê':'e','ë':'e','є':'e',
        '6':'g','9':'g',
        '#':'h',
        '1':'i','!':'i','|':'i','ì':'i','í':'i','î':'i','ï':'i','¡':'i',
        '0':'o','ø':'o','ò':'o','ó':'o','ô':'o','ö':'o','°':'o','σ':'o',
        '5':'s','$':'s','§':'s','ѕ':'s',
        '7':'t','+':'t','†':'t',
        'ù':'u','ú':'u','û':'u','ü':'u','µ':'u','υ':'u',
        '¥':'y','ý':'y','ÿ':'y',
        '2':'z','7z':'z'
    };

    // Characters people insert between letters to break up a word
    const SEPARATORS = /[\s._\-*+~`'"^,|/\\()[\]{}<>:;!?]/g;

    /* Normalise a string so evasion attempts collapse to the plain word. */
    function normalise(str) {
        let s = str.toLowerCase();
        s = s.replace(/[^\w\s]|_/g, ch => LEET[ch] ?? ch);   // symbol leet
        s = s.split('').map(ch => LEET[ch] ?? ch).join('');   // digit leet
        s = s.replace(/(.)\1{2,}/g, '$1$1');                  // fuuuuck -> fuuck
        return s;
    }

    /* Same as normalise but also removes separators, so "f.u.c.k" -> "fuck" */
    function squash(str) {
        return normalise(str).replace(SEPARATORS, '').replace(/(.)\1+/g, '$1');
    }

    function stars(n) { return '*'.repeat(Math.max(3, n)); }

    /* Build a regex that tolerates separators between each letter. */
    function spacedPattern(word) {
        const sep = '[\\s._\\-*+~`\'"^,|/\\\\]{0,2}';
        return word.split('').map(c => c.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join(sep);
    }

    let anywhereRe = null, wholeWordRe = null;

    function compile() {
        const a = ANYWHERE.map(spacedPattern).join('|');
        const w = WHOLE_WORD.map(spacedPattern).join('|');
        anywhereRe  = new RegExp('(' + a + ')', 'gi');
        wholeWordRe = new RegExp('\\b(' + w + ')\\b', 'gi');
    }
    compile();

    function isAllowed(token) {
        const bare = token.toLowerCase().replace(SEPARATORS, '');
        if (ALLOWLIST.has(bare)) return true;
        // Also allow if the squashed form is an allowlisted word
        return ALLOWLIST.has(squash(token));
    }

    return {
        /* Replace anything matching with asterisks. */
        clean(text) {
            if (!text) return text;

            // Pass 1: token by token. The allowlist is checked FIRST so real
            // words that merely contain a rude substring survive untouched
            // ("analysis", "therapist", "cockpit", "Scunthorpe").
            const cleanedTokens = [];
            let out = text.replace(/\S+/g, token => {
                if (isAllowed(token)) { cleanedTokens.push(token); return token; }
                const sq = squash(token);
                const hit = ANYWHERE.some(w => sq.includes(w.replace(/\s/g, ''))) ||
                            WHOLE_WORD.some(w => sq === w);
                return hit ? stars(token.length) : token;
            });

            // Pass 2 catches words split across spaces ("f u c k"). It must not
            // touch any token pass 1 already decided was legitimate, so those
            // are masked out, the regex runs, then they're restored.
            const PLACEHOLDER = '\u0000';
            const stash = [];
            cleanedTokens.forEach(t => {
                const i = stash.push(t) - 1;
                out = out.replace(new RegExp('\\b' + t.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '\\b'),
                                  PLACEHOLDER + i + PLACEHOLDER);
            });

            out = out.replace(anywhereRe, m => stars(m.length));
            out = out.replace(wholeWordRe, m => isAllowed(m) ? m : stars(m.length));

            stash.forEach((t, i) => {
                out = out.replace(PLACEHOLDER + i + PLACEHOLDER, t);
            });

            return out;
        },

        isClean(text) { return this.clean(text) === text; },

        /* Add your own words at runtime (e.g. from a moderation list). */
        addWords(words, wholeWordOnly = false) {
            const target = wholeWordOnly ? WHOLE_WORD : ANYWHERE;
            words.forEach(w => {
                const lw = String(w).toLowerCase().trim();
                if (lw && !target.includes(lw)) target.push(lw);
            });
            compile();
        },

        allow(words) {
            words.forEach(w => ALLOWLIST.add(String(w).toLowerCase().trim()));
        },

        get wordCount() { return ANYWHERE.length + WHOLE_WORD.length; }
    };
})();

if (typeof window !== 'undefined') window.ChatFilter = ChatFilter;
if (typeof module !== 'undefined' && module.exports) module.exports = ChatFilter;
