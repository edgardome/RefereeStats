// Fetch Interceptor for JWT
const originalFetch = window.fetch;
window.fetch = async (resource, config) => {
    let url = resource;
    if (resource instanceof Request) {
        url = resource.url;
    }

    const isAuthRequest = url.includes('/api/auth/login') || url.includes('/api/auth/register') || url.includes('/api/payments/');
    if (!isAuthRequest) {
        const token = sessionStorage.getItem('jwt_token');
        if (token) {
            config = config || {};
            config.headers = config.headers || {};
            config.headers['Authorization'] = `Bearer ${token}`;
        }
    }

    const response = await originalFetch(resource, config);
    if (response.status === 401) {
        if (sessionStorage.getItem('jwt_token')) {
            sessionStorage.removeItem('jwt_token');
            window.location.reload();
        } else {
            // Already logged out, just show the auth modal or premium modal
            if (typeof showPremiumModal === 'function') {
                showPremiumModal();
            } else {
                const premiumModal = document.getElementById('premium-modal');
                if (premiumModal) premiumModal.classList.remove('hidden');
            }
        }
        return Promise.reject('Unauthorized - Token Expired or Missing');
    }
    if (response.status === 403 || response.status === 402) {
        if (typeof showPremiumModal === 'function') {
            showPremiumModal();
        } else {
            const modal = document.getElementById('premium-modal');
            if (modal) modal.classList.remove('hidden');
        }
        return Promise.reject('Unauthorized - Premium Required');
    }
    return response;
};

document.addEventListener('DOMContentLoaded', async () => {
    // Handle Stripe payment success redirect
    const topUrlParams = new URLSearchParams(window.location.search);
    if (topUrlParams.get('payment') === 'success') {
        const token = sessionStorage.getItem('jwt_token');
        if (token) {
            try {
                const res = await fetch('/api/v1/auth/refresh', { method: 'POST' });
                if (res.ok) {
                    const data = await res.json();
                    sessionStorage.setItem('jwt_token', data.token);
                }
            } catch (e) { console.error('Failed to refresh token after payment', e); }
        }
    }

    const leagueSelect = document.getElementById('league-select');
    
    // Build the options data from the original select BEFORE TomSelect touches it
    const allLeagueOptions = [];
    if (leagueSelect) {
        Array.from(leagueSelect.querySelectorAll('optgroup')).forEach(group => {
            const country = group.label;
            Array.from(group.querySelectorAll('option')).forEach(opt => {
                allLeagueOptions.push({ value: opt.value, text: opt.textContent, country });
            });
        });
    }
    
    // Init TomSelect for leagues using the internal options/optgroups API
    function initLeagueSelect(filterCountry) {
        if (window.tsLeague) window.tsLeague.destroy();
        
        const filteredOptions = filterCountry
            ? allLeagueOptions.filter(o => o.country === filterCountry)
            : allLeagueOptions;
        
        // Build unique optgroups list
        const optgroupSet = [...new Set(filteredOptions.map(o => o.country))];
        
        window.tsLeague = new TomSelect(leagueSelect, {
            create: false,
            options: filteredOptions.map(o => ({ value: o.value, text: o.text, optgroup: o.country })),
            optgroups: optgroupSet.map(c => ({ value: c, label: c })),
            optgroupField: 'optgroup',
            labelField: 'text',
            valueField: 'value',
            searchField: ['text'],
            sortField: { field: "text", direction: "asc" },
            controlInput: '<input type="text" autocomplete="new-password" data-lpignore="true" spellcheck="false">',
            onChange: loadReferees
        });
        
        // Select first available option
        if (filteredOptions.length > 0) {
            window.tsLeague.setValue(filteredOptions[0].value, true);
        }
    }
    
    if (leagueSelect) {
        initLeagueSelect(null);
    }
    
    const countrySelect = document.getElementById('country-select');
    if (countrySelect) {
        window.tsCountry = new TomSelect(countrySelect, {
            create: false,
            sortField: { field: "text", direction: "asc" },
            controlInput: '<input type="text" autocomplete="new-password" data-lpignore="true" spellcheck="false">'
        });
        
        window.tsCountry.on('change', (val) => {
            initLeagueSelect(val === 'all' ? null : val);
            loadReferees();
        });
    }

    const seasonSelect = document.getElementById('season-select');
    const refereeSelect = document.getElementById('referee-select');
    const syncButton = document.getElementById('sync-button');
    const toast = document.getElementById('toast');
    const langSelect = document.getElementById('lang-select');

    // i18n Dictionary
    const translations = {
        en: {
            darkMode: "Dark Mode",
            leaderboard: "Leaderboard",
            syncLeague: "Sync League",
            loadingReferees: "Loading referees...",
            noReferees: "No referees found for this season.",
            selectReferee: "Select a referee...",
            errorLoadingRefs: "⚠️ Error loading referees from server.",
            overallAverages: "Overall Averages",
            yellowCards: "Yellow Cards",
            secondYellows: "2nd Yellows",
            directReds: "Direct Reds",
            penalties: "Penalties",
            homeLeaning: "Home Leaning",
            awayLeaning: "Away Leaning",
            neutral: "Neutral",
            loadingMatches: "Loading matches...",
            noMatches: "No match reports in cache for this referee in this league/season.",
            errorMatches: "Error loading match reports.",
            syncing: "Syncing...",
            syncComplete: "Sync Complete",
            leagueLabel: "League",
            seasonLabel: "Season",
            refereeLabel: "Referee",
            homeVsAway: "Home vs Away Bias",
            refVsTeam: "Referee vs Team Statistics",
            matchHistory: "Match History",
            logout: "Logout",
            noRefSelected: "No Referee Selected",
            selectRefLoad: "Select a referee to load profile",
            debutDate: "Debut Date",
            totalCacheMatches: "Matches",
            biasDesc: "Calculated card and penalty distribution between Home and Away teams",
            strongAwayBias: "STRONG AWAY BIAS",
            balanced: "BALANCED",
            strongHomeBias: "STRONG HOME BIAS",
            selectTeamRecord: "Select a team to see their specific record with this referee",
            selectLeagueSeasonFirst: "Select a league & season first...",
            globalStatsMode: "Global Stats Mode",
            homeCardsPct: "Home Cards %",
            awayCardsPct: "Away Cards %",
            homePenPct: "Home Pen %",
            awayPenPct: "Away Pen %",
            selectTeam: "Select a team...",
            homeTeamSearch: "Home Team (Search...)",
            awayTeamSearch: "Away Team (Search...)",
            matchupPreview: "Matchup Strictness Preview",
            predictAdvantage: "Predict Advantage",
            thDate: "Date",
            thHomeTeam: "Home Team",
            thScore: "Score",
            thAwayTeam: "Away Team",
            thCards: "Cards Issued",
            matchesText: "Matches",
            selectRefMatchHistory: "Select a referee to view match history",
            premiumTitle: "Unlock Premium Analytics",
            premiumDesc: "Get full access to Matchup Predictor, cross-league Global Stats, and complete referee historical data.",
            subscribeBtn: "Upgrade Now",
            orText: "OR",
            alreadyHaveAccount: "I already have an account",
            loginBtn: "Login / PRO",
            invalidEmail: "Please enter a valid email address.",
            emailPlaceholder: "Your Email",
            passwordPlaceholder: "Choose a Password",
            createAccountBtn: "Create Account & Pay",
            profileBtn: "My Profile",
            profileTitle: "My Profile",
            changeEmailLabel: "Change Email",
            updateEmailBtn: "Update Email",
            changePasswordLabel: "Change Password",
            updatePasswordBtn: "Update Password",
            subscriptionLabel: "Subscription",
            manageSubBtn: "Manage Subscription",
            noSubText: "No active subscription"
        },
        es: {
            darkMode: "Modo Oscuro",
            leaderboard: "Clasificación",
            syncLeague: "Sincronizar Liga",
            loadingReferees: "Cargando árbitros...",
            noReferees: "No se encontraron árbitros para esta temporada.",
            selectReferee: "Selecciona un árbitro...",
            errorLoadingRefs: "⚠️ Error cargando árbitros del servidor.",
            overallAverages: "Promedios Generales",
            yellowCards: "Tarjetas Amarillas",
            secondYellows: "Dobles Amarillas",
            directReds: "Rojas Directas",
            penalties: "Penaltis",
            homeLeaning: "Tendencia Local",
            awayLeaning: "Tendencia Visitante",
            neutral: "Neutral",
            loadingMatches: "Cargando partidos...",
            noMatches: "No hay partidos en caché para este árbitro en esta liga/temporada.",
            errorMatches: "Error al cargar los partidos.",
            syncing: "Sincronizando...",
            syncComplete: "Sincronización Completa",
            leagueLabel: "Liga",
            seasonLabel: "Temporada",
            refereeLabel: "Árbitro",
            homeVsAway: "Sesgo Local vs Visitante",
            refVsTeam: "Estadísticas Árbitro vs Equipo",
            matchHistory: "Historial de Partidos",
            logout: "Cerrar Sesión",
            noRefSelected: "Ningún Árbitro Seleccionado",
            selectRefLoad: "Selecciona un árbitro para ver su perfil",
            debutDate: "Fecha de Debut",
            totalCacheMatches: "Partidos",
            biasDesc: "Distribución calculada de tarjetas y penaltis entre local y visitante",
            strongAwayBias: "SESGO VISITANTE",
            balanced: "EQUILIBRADO",
            strongHomeBias: "SESGO LOCAL",
            selectTeamRecord: "Selecciona un equipo para ver su historial con este árbitro",
            selectLeagueSeasonFirst: "Selecciona una liga y temporada...",
            globalStatsMode: "Modo Estadísticas Globales",
            homeCardsPct: "% Tarjetas Local",
            awayCardsPct: "% Tarjetas Visit.",
            homePenPct: "% Penaltis Local",
            awayPenPct: "% Penaltis Visit.",
            selectTeam: "Selecciona un equipo...",
            homeTeamSearch: "Equipo Local (Buscar...)",
            awayTeamSearch: "Equipo Visit. (Buscar...)",
            matchupPreview: "Predicción de Enfrentamiento",
            predictAdvantage: "Predecir Ventaja",
            thDate: "Fecha",
            thHomeTeam: "Equipo Local",
            thScore: "Resultado",
            thAwayTeam: "Equipo Visit.",
            thCards: "Tarjetas",
            matchesText: "Partidos",
            selectRefMatchHistory: "Selecciona un árbitro para ver su historial",
            premiumTitle: "Desbloquea Analítica Premium",
            premiumDesc: "Accede al Predictor de Enfrentamientos, Estadísticas Globales cruzadas y todo el historial del árbitro.",
            subscribeBtn: "Mejorar Ahora",
            orText: "O",
            alreadyHaveAccount: "Ya tengo una cuenta",
            loginBtn: "Login / PRO",
            invalidEmail: "Por favor, introduce un correo válido.",
            emailPlaceholder: "Tu Correo Electrónico",
            passwordPlaceholder: "Elige una Contraseña",
            createAccountBtn: "Crear Cuenta y Pagar",
            profileBtn: "Mi Perfil",
            profileTitle: "Mi Perfil",
            changeEmailLabel: "Cambiar Correo",
            updateEmailBtn: "Actualizar Correo",
            changePasswordLabel: "Cambiar Contraseña",
            updatePasswordBtn: "Actualizar Contraseña",
            subscriptionLabel: "Suscripción",
            manageSubBtn: "Gestionar Suscripción",
            noSubText: "Sin suscripción activa"
        },
        de: {
            darkMode: "Dunkelmodus",
            leaderboard: "Rangliste",
            syncLeague: "Liga Synchronisieren",
            loadingReferees: "Schiedsrichter werden geladen...",
            noReferees: "Keine Schiedsrichter für diese Saison gefunden.",
            selectReferee: "Wähle einen Schiedsrichter...",
            errorLoadingRefs: "⚠️ Fehler beim Laden der Schiedsrichter.",
            overallAverages: "Gesamtdurchschnitt",
            yellowCards: "Gelbe Karten",
            secondYellows: "Gelb-Rote Karten",
            directReds: "Rote Karten",
            penalties: "Elfmeter",
            homeLeaning: "Heim-Tendenz",
            awayLeaning: "Auswärts-Tendenz",
            neutral: "Neutral",
            loadingMatches: "Spiele werden geladen...",
            noMatches: "Keine Spiele im Cache für diesen Schiedsrichter gefunden.",
            errorMatches: "Fehler beim Laden der Spiele.",
            syncing: "Wird synchronisiert...",
            syncComplete: "Synchronisation Abgeschlossen",
            leagueLabel: "Liga",
            seasonLabel: "Saison",
            refereeLabel: "Schiedsrichter",
            homeVsAway: "Heim vs Auswärts Tendenz",
            refVsTeam: "Schiedsrichter vs Team",
            matchHistory: "Spielverlauf",
            logout: "Abmelden",
            noRefSelected: "Kein Schiedsrichter Ausgewählt",
            selectRefLoad: "Schiedsrichter wählen um Profil zu laden",
            debutDate: "Debütdatum",
            totalCacheMatches: "Spiele",
            biasDesc: "Berechnete Karten- und Elfmeterverteilung zwischen Heim und Auswärts",
            strongAwayBias: "STARK AUSWÄRTS",
            balanced: "AUSGEGLICHEN",
            strongHomeBias: "STARK HEIM",
            selectTeamRecord: "Team wählen um spezifische Bilanz zu sehen",
            selectLeagueSeasonFirst: "Zuerst Liga & Saison wählen...",
            globalStatsMode: "Globaler Statistikmodus",
            homeCardsPct: "Heimkarten %",
            awayCardsPct: "Auswärtskarten %",
            homePenPct: "Heim-Elfm. %",
            awayPenPct: "Auswärts-Elfm. %",
            selectTeam: "Team wählen...",
            homeTeamSearch: "Heimteam (Suchen...)",
            awayTeamSearch: "Auswärtsteam (Suchen...)",
            matchupPreview: "Matchup-Vorschau",
            predictAdvantage: "Vorteil Vorhersagen",
            thDate: "Datum",
            thHomeTeam: "Heimteam",
            thScore: "Ergebnis",
            thAwayTeam: "Auswärtsteam",
            thCards: "Karten Gegeben",
            matchesText: "Spiele",
            selectRefMatchHistory: "Schiedsrichter wählen um Spielverlauf zu sehen",
            premiumTitle: "Premium-Analyse freischalten",
            premiumDesc: "Voller Zugriff auf Matchup-Vorhersagen, ligaübergreifende Statistiken und die komplette Historie.",
            subscribeBtn: "Jetzt Upgraden",
            orText: "ODER",
            alreadyHaveAccount: "Ich habe bereits ein Konto",
            loginBtn: "Login / PRO",
            invalidEmail: "Bitte geben Sie eine gültige E-Mail-Adresse ein.",
            emailPlaceholder: "Ihre E-Mail",
            passwordPlaceholder: "Passwort wählen",
            createAccountBtn: "Konto erstellen & bezahlen",
            profileBtn: "Mein Profil",
            profileTitle: "Mein Profil",
            changeEmailLabel: "E-Mail ändern",
            updateEmailBtn: "E-Mail aktualisieren",
            changePasswordLabel: "Passwort ändern",
            updatePasswordBtn: "Passwort aktualisieren",
            subscriptionLabel: "Abonnement",
            manageSubBtn: "Abonnement verwalten",
            noSubText: "Kein aktives Abonnement"
        }
    };

    let currentLang = 'en';

    function t(key) {
        return translations[currentLang][key] || key;
    }

    function updateLanguage() {
        try {
            currentLang = langSelect.value;
            document.querySelectorAll('[data-i18n]').forEach(el => {
                const key = el.getAttribute('data-i18n');
                if (translations[currentLang][key]) {
                    el.textContent = translations[currentLang][key];
                }
            });
            
            // Update modal placeholders and buttons
            const ctaEmail = document.getElementById('cta-email');
            const ctaPassword = document.getElementById('cta-password');
            const btnSubmitSignup = document.getElementById('btn-submit-signup');
            const newEmailInput = document.getElementById('new-email-input');
            const newPasswordInput = document.getElementById('new-password-input');
            
            if (ctaEmail) ctaEmail.placeholder = t('emailPlaceholder');
            if (ctaPassword) ctaPassword.placeholder = t('passwordPlaceholder');
            if (btnSubmitSignup) btnSubmitSignup.textContent = t('createAccountBtn');
            if (newEmailInput) newEmailInput.placeholder = t('emailPlaceholder');
            if (newPasswordInput) newPasswordInput.placeholder = t('passwordPlaceholder');
            
            // Re-load labels dependent on JS state
            if (refereeSelect.disabled) {
                refereeSelect.innerHTML = `<option value="">${t('selectLeagueSeasonFirst')}</option>`;
            }
            
            // Also update the selectTeam dropdowns if they are in their default state
            const tsSelect = document.getElementById('team-stats-select');
            if (tsSelect && tsSelect.options.length === 1 && tsSelect.options[0].value === "") {
                tsSelect.innerHTML = `<option value="">${t('selectTeam')}</option>`;
            }
            const htInput = document.getElementById('home-team-input');
            if (htInput && htInput.options.length === 1 && htInput.options[0].value === "") {
                htInput.innerHTML = `<option value="">${t('homeTeamSearch')}</option>`;
            }
            const atInput = document.getElementById('away-team-input');
            if (atInput && atInput.options.length === 1 && atInput.options[0].value === "") {
                atInput.innerHTML = `<option value="">${t('awayTeamSearch')}</option>`;
            }
            
            const mCount = document.getElementById('matches-count');
            if (mCount) {
                const count = parseInt(mCount.textContent) || 0;
                mCount.textContent = `${count} ${t('matchesText')}`;
            }

            if(refereeSelect.value) {
                handleRefereeChange();
            } else {
                loadReferees();
            }
        } catch (e) {
            console.error(e);
        }
    }

    langSelect.addEventListener('change', updateLanguage);

    // Initial Translation Application
    updateLanguage();

    const btnLogout = document.getElementById('logout-btn');
    const authLoginBtn = document.getElementById('auth-login-btn');
    const profileBtn = document.getElementById('profile-btn');
    
    console.log("Auth Check:", sessionStorage.getItem('jwt_token') ? "LOGGED IN" : "NOT LOGGED IN");
    
    if (sessionStorage.getItem('jwt_token')) {
        if (btnLogout) {
            btnLogout.style.setProperty('display', 'flex', 'important');
        }
        if (profileBtn) {
            profileBtn.style.setProperty('display', 'inline-block', 'important');
        }
        if (authLoginBtn) authLoginBtn.style.setProperty('display', 'none', 'important');
    } else {
        if (btnLogout) btnLogout.style.setProperty('display', 'none', 'important');
        if (profileBtn) profileBtn.style.setProperty('display', 'none', 'important');
        if (authLoginBtn) authLoginBtn.style.setProperty('display', 'block', 'important');
    }

    if (btnLogout) {
        btnLogout.addEventListener('click', () => {
            sessionStorage.removeItem('jwt_token');
            if (leagueSelect) {
                leagueSelect.value = 'ES2'; // Use the correct ID for the free league
            }
            window.location.reload();
        });
    }
    
    if (authLoginBtn) {
        authLoginBtn.addEventListener('click', () => {
            window.location.href = '/login.html';
        });
    }
    
    // Profile Modal Logic
    const profileModal = document.getElementById('profile-modal');
    if (profileBtn && profileModal) {
        profileBtn.addEventListener('click', () => {
            profileModal.classList.remove('hidden');
            
            // Check if user is premium to show portal button
            const token = sessionStorage.getItem('jwt_token');
            if (token) {
                try {
                    const base64Url = token.split('.')[1];
                    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
                    const jsonPayload = decodeURIComponent(window.atob(base64).split('').map(function(c) {
                        return '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2);
                    }).join(''));
                    const payload = JSON.parse(jsonPayload);
                    
                    if (payload.is_premium === "true" || payload.is_premium === true) {
                        document.getElementById('btn-manage-subscription').style.display = 'block';
                        document.getElementById('no-sub-text').style.display = 'none';
                    }
                } catch(e) {
                    console.error('Failed to parse JWT', e);
                }
            }
        });
        
        document.querySelector('.close-profile-modal')?.addEventListener('click', () => {
            profileModal.classList.add('hidden');
        });
    }

    // Change Email
    document.getElementById('btn-change-email')?.addEventListener('click', async () => {
        const newEmail = document.getElementById('new-email-input').value;
        const errLabel = document.getElementById('email-error');
        if (!newEmail) return;
        
        try {
            const res = await fetch('/api/v1/auth/change-email', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ newEmail })
            });
            if (res.ok) {
                const data = await res.json();
                sessionStorage.setItem('jwt_token', data.token);
                showToast('Email updated successfully', 3000);
                errLabel.style.display = 'none';
            } else {
                errLabel.textContent = 'Failed to update email. Maybe in use.';
                errLabel.style.display = 'block';
            }
        } catch(e) {}
    });

    // Change Password
    document.getElementById('btn-change-password')?.addEventListener('click', async () => {
        const newPassword = document.getElementById('new-password-input').value;
        const errLabel = document.getElementById('password-error');
        if (!newPassword) return;
        
        try {
            const res = await fetch('/api/v1/auth/change-password', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ newPassword })
            });
            if (res.ok) {
                showToast('Password updated successfully', 3000);
                errLabel.style.display = 'none';
            } else {
                errLabel.textContent = 'Failed to update password.';
                errLabel.style.display = 'block';
            }
        } catch(e) {}
    });

    // Customer Portal
    document.getElementById('btn-manage-subscription')?.addEventListener('click', async () => {
        try {
            const res = await fetch('/api/v1/payments/portal', { method: 'POST' });
            if (res.ok) {
                const data = await res.json();
                window.location.href = data.url;
            } else {
                showToast('Failed to open billing portal', 3000);
            }
        } catch(e) {}
    });

    // Check if user just paid
    const urlParams = new URLSearchParams(window.location.search);
    if (urlParams.get('payment') === 'success' || urlParams.get('payment') === 'mock_success') {
        showToast('Payment successful! Your account is now Premium.', 5000);
        window.history.replaceState({}, document.title, window.location.pathname);
    }
    
    // Premium Modal Logic
    const premiumModal = document.getElementById('premium-modal');
    function showPremiumModal() {
        if (premiumModal) {
            const token = localStorage.getItem('jwtToken');
            if (token) {
                // If logged in, hide "OR" and "Login / PRO"
                const divider = premiumModal.querySelector('.divider');
                if (divider) divider.style.display = 'none';
                const loginBtn = document.getElementById('btn-login-redirect');
                if (loginBtn) loginBtn.style.display = 'none';
                
                // Ensure signup form is hidden and Subscribe button is visible
                document.getElementById('fast-signup-form').style.display = 'none';
                document.getElementById('btn-subscribe').style.display = 'block';
                document.getElementById('btn-subscribe').textContent = t('subscribeBtn');
            } else {
                // Not logged in, show everything
                const divider = premiumModal.querySelector('.divider');
                if (divider) divider.style.display = 'flex';
                const loginBtn = document.getElementById('btn-login-redirect');
                if (loginBtn) loginBtn.style.display = 'block';
                document.getElementById('btn-subscribe').style.display = 'block';
                document.getElementById('fast-signup-form').style.display = 'none';
            }
            premiumModal.classList.remove('hidden');
        }
    }
    
    document.getElementById('btn-close-modal')?.addEventListener('click', () => {
        premiumModal.classList.add('hidden');
        // Revert league select to LaLiga 2 (free) so they don't get stuck
        if (window.tsLeague) {
            window.tsLeague.setValue('ES2');
            loadReferees();
        }
    });
    
    document.getElementById('btn-login-redirect')?.addEventListener('click', () => {
        window.location.href = '/login.html';
    });
    
    document.getElementById('btn-subscribe')?.addEventListener('click', async () => {
        const token = localStorage.getItem('jwtToken');
        if (token) {
            // Already logged in! Extract email and proceed to checkout
            try {
                const base64Url = token.split('.')[1];
                const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
                const jsonPayload = decodeURIComponent(window.atob(base64).split('').map(function(c) {
                    return '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2);
                }).join(''));
                const payload = JSON.parse(jsonPayload);
                
                const email = payload.email || payload.sub || payload["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress"];
                
                const btn = document.getElementById('btn-subscribe');
                const origText = btn.textContent;
                btn.textContent = "Redirecting...";
                btn.disabled = true;

                const res = await fetch('/api/v1/payments/checkout', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ email: email })
                });
                
                const data = await res.json();
                if (data.url) {
                    window.location.href = data.url;
                } else {
                    btn.textContent = origText;
                    btn.disabled = false;
                    showToast('Checkout failed', 3000);
                }
            } catch (e) {
                console.error("Failed to parse JWT or checkout", e);
                showToast('An error occurred', 3000);
            }
        } else {
            // Not logged in, show signup form
            document.getElementById('btn-subscribe').style.display = 'none';
            document.getElementById('fast-signup-form').style.display = 'block';
        }
    });
    
    document.getElementById('btn-submit-signup')?.addEventListener('click', async () => {
        const email = document.getElementById('cta-email').value;
        const password = document.getElementById('cta-password').value;
        const errLabel = document.getElementById('cta-error');
        
        if (!email || !password) {
            errLabel.textContent = t('invalidEmail'); // Fallback logic
            errLabel.style.display = 'block';
            return;
        }
        
        const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
        if (!emailRegex.test(email)) {
            errLabel.textContent = t('invalidEmail');
            errLabel.style.display = 'block';
            return;
        }
        
        errLabel.style.display = 'none';
        
        try {
            // Register
            const res = await fetch('/api/auth/register', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email, password })
            });
            
            if (!res.ok) {
                const errData = await res.json();
                errLabel.textContent = errData.message || 'Registration failed';
                errLabel.style.display = 'block';
                return;
            }
            
            const data = await res.json();
            sessionStorage.setItem('jwt_token', data.token); // Logged in!
            
            // Go to checkout
            const checkoutRes = await fetch('/api/v1/payments/checkout', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email })
            });
            
            if (checkoutRes.ok) {
                const checkoutData = await checkoutRes.json();
                window.location.href = checkoutData.url;
            } else {
                errLabel.textContent = 'Error initiating checkout';
                errLabel.style.display = 'block';
            }
            
        } catch (err) {
            console.error(err);
            errLabel.textContent = 'Network error. Please try again.';
            errLabel.style.display = 'block';
        }
    });

    // Stats Panel Elements
    const refCard = document.getElementById('referee-card');
    const refName = document.getElementById('ref-name');
    const refNationality = document.getElementById('ref-nationality');
    const compareBtn = document.getElementById('compare-btn');
    const globalToggle = document.getElementById('global-toggle');
    const refDebut = document.getElementById('ref-debut');
    const refTotalMatches = document.getElementById('ref-total-matches');

    const avgYellows = document.getElementById('avg-yellows');
    const avgDoubleYellows = document.getElementById('avg-double-yellows');
    const avgReds = document.getElementById('avg-reds');
    const avgPenalties = document.getElementById('avg-penalties');

    // Bias Panel Elements
    const biasLabel = document.getElementById('bias-label');
    const biasMarker = document.getElementById('bias-marker');
    const homeCardsPct = document.getElementById('home-cards-pct');
    const awayCardsPct = document.getElementById('away-cards-pct');
    const homePenPct = document.getElementById('home-pen-pct');
    const awayPenPct = document.getElementById('away-pen-pct');
    
    // Referee vs Team Stats Elements
    const teamStatsSelect = document.getElementById('team-stats-select');
    const teamStatsContent = document.getElementById('team-stats-content');
    const tsWins = document.getElementById('ts-wins');
    const tsDraws = document.getElementById('ts-draws');
    const tsLosses = document.getElementById('ts-losses');
    const tsMatches = document.getElementById('ts-matches');
    const tsGoals = document.getElementById('ts-goals');
    const tsYellows = document.getElementById('ts-yellows');
    const tsReds = document.getElementById('ts-reds');
    const tsPens = document.getElementById('ts-pens');

    let currentRefereeTeamHistory = [];

    // Table Elements
    const matchesTbody = document.getElementById('matches-tbody');
    const matchesCount = document.getElementById('matches-count');

    // Helper: Show Toast Notification
    function showToast(message, duration = 4000) {
        toast.textContent = message;
        toast.classList.remove('hidden');
        setTimeout(() => {
            toast.classList.add('hidden');
        }, duration);
    }

    // Load Referees List
    async function loadReferees() {
        const league = leagueSelect.value;
        const season = seasonSelect.value;
        
        refereeSelect.disabled = true;
        refereeSelect.innerHTML = '<option value="">Loading referees...</option>';

        try {
            const res = await fetch(`/api/leagues/${league}/referees?season=${season}`);
            if (!res.ok) throw new Error('Failed to load referees');
            
            const payload = await res.json();
            const referees = payload.content || [];
            
            if (referees.length === 0) {
                refereeSelect.innerHTML = '<option value="">No referees found for this season.</option>';
                return;
            }

            // Sort referees alphabetically by name
            referees.sort((a, b) => a.name.localeCompare(b.name));

            refereeSelect.innerHTML = '<option value="" selected disabled>Select a referee...</option>';
            referees.forEach(ref => {
                const opt = document.createElement('option');
                opt.value = ref.refereeId;
                opt.textContent = `${ref.name} (${ref.matchesOfficiated} matches)`;
                opt.dataset.nationality = ref.nationality || 'Unknown';
                opt.dataset.debut = ref.debutDate ? ref.debutDate : 'Unknown';
                refereeSelect.appendChild(opt);
            });
            refereeSelect.disabled = false;
        } catch (err) {
            console.error(err);
            refereeSelect.innerHTML = '<option value="">Error loading referees list</option>';
            showToast('⚠️ Error loading referees from server.');
        }
    }

    // Load Details and Stats when a Referee is Selected
    async function handleRefereeChange() {
        const refereeId = refereeSelect.value;
        if (!refereeId) return;

        const selectedOption = refereeSelect.options[refereeSelect.selectedIndex];
        const league = leagueSelect.value;
        const season = seasonSelect.value;
        const isGlobal = globalToggle ? globalToggle.checked : false;

        // Set Basic Profile
        refName.textContent = selectedOption.textContent.split('(')[0].trim();
        refNationality.textContent = selectedOption.dataset.nationality;
        refDebut.textContent = selectedOption.dataset.debut;
        refCard.classList.remove('loading-state');

        // Fetch Analytics & Matches
        fetchAnalytics(refereeId, league, season, isGlobal);
        fetchMatches(refereeId, league, season, isGlobal);
    }

    // Toggle Change Listener
    if (globalToggle) {
        globalToggle.addEventListener('change', () => {
            if (refereeSelect.value) {
                handleRefereeChange();
            }
        });
    }

    // Fetch Referee Analytics
    async function fetchAnalytics(refereeId, league, season, isGlobal) {
        try {
            const res = await fetch(`/api/referees/${refereeId}/analytics?league=${league}&season=${season}&global=${isGlobal}`);
            if (!res.ok) throw new Error('Failed to fetch analytics');
            
            const data = await res.json();

            // Populate Cards
            avgYellows.textContent = data.averageYellowCards.toFixed(2);
            avgDoubleYellows.textContent = data.averageSecondYellowCards.toFixed(2);
            avgReds.textContent = data.averageDirectRedCards.toFixed(2);
            avgPenalties.textContent = data.averagePenaltiesAwarded.toFixed(2);

            // Populate Bias
            homeCardsPct.textContent = `${Math.round(data.homeCardsPercentage)}%`;
            awayCardsPct.textContent = `${Math.round(data.awayCardsPercentage)}%`;
            homePenPct.textContent = `${Math.round(data.homePenaltiesPercentage)}%`;
            awayPenPct.textContent = `${Math.round(data.awayPenaltiesPercentage)}%`;

            // Calculate Bias Weight and Marker Position
            // Neutral center is 50%. Home bias moves marker to the right (towards 100%). Away bias moves to left (towards 0%).
            // Use homeCardsPercentage as the baseline weight.
            const biasWeight = data.homeCardsPercentage; 
            biasMarker.style.left = `${biasWeight}%`;

            // Set Bias Category Label
            if (biasWeight > 55) {
                biasLabel.textContent = 'Home Leaning';
                biasLabel.style.borderColor = 'var(--accent-primary)';
                biasLabel.style.color = '#fff';
            } else if (biasWeight < 45) {
                biasLabel.textContent = 'Away Leaning';
                biasLabel.style.borderColor = 'var(--color-red)';
                biasLabel.style.color = 'var(--color-red)';
            } else {
                biasLabel.textContent = 'Neutral';
                biasLabel.style.borderColor = 'var(--border-color)';
                biasLabel.style.color = 'var(--color-text-muted)';
            }

            // Populate Referee vs Team Dropdown & Matchup Preview Selects
            currentRefereeTeamHistory = data.refereeTeamHistory || [];
            
            teamStatsSelect.innerHTML = '<option value="">Select a team...</option>';
            homeInput.innerHTML = '<option value="">Home Team (Search...)</option>';
            awayInput.innerHTML = '<option value="">Away Team (Search...)</option>';
            
            if (currentRefereeTeamHistory.length > 0) {
                currentRefereeTeamHistory.forEach(team => {
                    const option = document.createElement('option');
                    option.value = team.teamId;
                    option.textContent = `${team.teamId} (${team.matchesTogether} matches)`;
                    teamStatsSelect.appendChild(option);
                    
                    const opt1 = document.createElement('option');
                    opt1.value = team.teamId;
                    opt1.textContent = team.teamId;
                    homeInput.appendChild(opt1);
                    
                    const opt2 = document.createElement('option');
                    opt2.value = team.teamId;
                    opt2.textContent = team.teamId;
                    awayInput.appendChild(opt2);
                });
                teamStatsSelect.disabled = false;
                
                // Re-initialize Tom Select if they exist
                if (window.tsHome) {
                    window.tsHome.sync();
                } else {
                    window.tsHome = new TomSelect("#home-team-input",{ create: false, sortField: { field: "text", direction: "asc" } });
                }
                if (window.tsAway) {
                    window.tsAway.sync();
                } else {
                    window.tsAway = new TomSelect("#away-team-input",{ create: false, sortField: { field: "text", direction: "asc" } });
                }
                if (window.tsTeamStats) {
                    window.tsTeamStats.sync();
                } else {
                    window.tsTeamStats = new TomSelect("#team-stats-select",{ create: false, sortField: { field: "text", direction: "asc" } });
                }
                
            } else {
                teamStatsSelect.disabled = true;
                if (window.tsHome) window.tsHome.clearOptions();
                if (window.tsAway) window.tsAway.clearOptions();
                if (window.tsTeamStats) window.tsTeamStats.clearOptions();
            }
            teamStatsContent.classList.add('hidden');

        } catch (err) {
            console.error(err);
            showToast('⚠️ Error loading analytics for this referee.');
        }
    }

    // Fetch Matches Table
    async function fetchMatches(refereeId, league, season, isGlobal) {
        matchesTbody.innerHTML = `<tr><td colspan="5" class="table-empty-state">${t('loadingMatches')}</td></tr>`;
        
        try {
            const res = await fetch(`/api/referees/${refereeId}/matches?league=${league}&season=${season}&global=${isGlobal}`);
            if (!res.ok) throw new Error('Failed to fetch matches');

            const payload = await res.json();
            const matches = payload.content || [];
            matchesCount.textContent = `${matches.length} ${t('matchesText')}`;
            refTotalMatches.textContent = matches.length;

            if (matches.length === 0) {
                matchesTbody.innerHTML = `<tr><td colspan="5" class="table-empty-state">${t('noMatches')}</td></tr>`;
                return;
            }

            // Sort matches by date descending
            matches.sort((a, b) => new Date(b.date) - new Date(a.date));

            matchesTbody.innerHTML = '';
            matches.forEach(match => {
                const tr = document.createElement('tr');

                // Count cards
                let yellows = 0;
                let reds = 0;
                match.cards.forEach(c => {
                    if (c.cardType === 'Yellow') yellows++;
                    if (c.cardType === 'Red' || c.cardType === 'SecondYellow') reds++;
                });

                tr.innerHTML = `
                    <td>${match.date}</td>
                    <td>${match.homeTeam}</td>
                    <td><span class="score-badge">${match.finalScore || '-:-'}</span></td>
                    <td>${match.awayTeam}</td>
                    <td>
                        <div class="cards-inline">
                            <span class="card-pill pill-yellow">🟨 ${yellows}</span>
                            <span class="card-pill pill-red">🟥 ${reds}</span>
                        </div>
                    </td>
                `;
                matchesTbody.appendChild(tr);
            });

        } catch (err) {
            console.error(err);
            matchesTbody.innerHTML = `<tr><td colspan="5" class="table-empty-state">${t('errorMatches')}</td></tr>`;
        }
    }

    // Handle Manual Sync Button
    async function handleSync() {
        const league = leagueSelect.value;
        const season = seasonSelect.value;

        syncButton.disabled = true;
        syncButton.innerHTML = `<svg class="spin-animation" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21.5 2v6h-6M21.34 15.57a10 10 0 1 1-.57-8.38l5.67-5.67"/></svg> Syncing...`;
        showToast(`⚡ Synchronizing matches for ${league} (${season}) in background...`);

        try {
            const res = await fetch(`/api/leagues/${league}/sync-matches?season=${season}`, {
                method: 'POST'
            });

            if (!res.ok) throw new Error('Sync failed');
            const data = await res.json();

            showToast(`✅ Sync Complete: ${data.syncedNew} new matches downloaded. Total cached matches: ${data.totalCount}.`);
            
            // Reload dropdown
            await loadReferees();
        } catch (err) {
            console.error(err);
            showToast('❌ Error synchronizing league. Check API logs.');
        } finally {
            syncButton.disabled = false;
            syncButton.innerHTML = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21.5 2v6h-6M21.34 15.57a10 10 0 1 1-.57-8.38l5.67-5.67"/></svg> Sync League`;
        }
    }

    // Event Listeners
    leagueSelect.addEventListener('change', loadReferees);
    seasonSelect.addEventListener('change', loadReferees);
    refereeSelect.addEventListener('change', handleRefereeChange);
    syncButton.addEventListener('click', handleSync);

    // Theme Toggle Logic
    const themeToggle = document.getElementById('theme-toggle');
    if (themeToggle) {
        // Load saved theme
        const savedTheme = localStorage.getItem('app-theme') || 'light';
        if (savedTheme === 'dark') {
            document.body.setAttribute('data-theme', 'dark');
            themeToggle.checked = true;
        }

        themeToggle.addEventListener('change', (e) => {
            if (e.target.checked) {
                document.body.setAttribute('data-theme', 'dark');
                localStorage.setItem('app-theme', 'dark');
            } else {
                document.body.removeAttribute('data-theme');
                localStorage.setItem('app-theme', 'light');
            }
        });
    }

    // Initial Load
    loadReferees();

    // --------------------------------------------------------
    // PREMIUM VALUE-ADD FEATURES
    // --------------------------------------------------------

    // Leaderboard
    const leaderboardBtn = document.getElementById('leaderboard-button');
    const leaderboardModal = document.getElementById('leaderboard-modal');
    const leaderboardSort = document.getElementById('leaderboard-sort');
    const leaderboardTbody = document.getElementById('leaderboard-tbody');

    async function loadLeaderboard() {
        const league = leagueSelect.value;
        const season = seasonSelect.value;
        const sortBy = leaderboardSort.value;
        
        leaderboardTbody.innerHTML = '<tr><td colspan="6" style="text-align: center;">Loading...</td></tr>';
        
        try {
            const res = await fetch(`/api/analytics/leagues/${league}/leaderboard?season=${season}&sortBy=${sortBy}`);
            const payload = await res.json();
            const data = payload.content || [];
            
            leaderboardTbody.innerHTML = '';
            data.forEach(ref => {
                const tr = document.createElement('tr');
                let name = ref.refereeName || ref.refereeId;
                if (name === ref.refereeId) {
                    const opt = Array.from(refereeSelect.options).find(o => o.value === ref.refereeId);
                    if (opt) name = opt.textContent.split('(')[0].trim();
                }
                
                tr.innerHTML = `
                    <td>#${ref.rank}</td>
                    <td>${name}</td>
                    <td>${ref.matchesOfficiated}</td>
                    <td>${ref.averageYellowCards.toFixed(2)}</td>
                    <td>${ref.averageRedCards.toFixed(2)}</td>
                    <td>${ref.homeBiasPercentage}%</td>
                `;
                leaderboardTbody.appendChild(tr);
            });
        } catch (e) {
            console.error(e);
            leaderboardTbody.innerHTML = '<tr><td colspan="6" style="text-align: center;">Failed to load leaderboard</td></tr>';
        }
    }

    leaderboardBtn.addEventListener('click', () => {
        leaderboardModal.classList.remove('hidden');
        loadLeaderboard();
    });
    leaderboardSort.addEventListener('change', loadLeaderboard);

    // Matchup Preview
    const matchupPanel = document.getElementById('matchup-panel');
    const homeInput = document.getElementById('home-team-input');
    const awayInput = document.getElementById('away-team-input');
    const predictBtn = document.getElementById('predict-btn');
    const matchupResult = document.getElementById('matchup-result');

    predictBtn.addEventListener('click', async () => {
        const refereeId = refereeSelect.value;
        if (!refereeId) return;
        const league = leagueSelect.value;
        const season = seasonSelect.value;
        const home = homeInput.value.trim();
        const away = awayInput.value.trim();
        
        if (!home || !away) {
            showToast('⚠️ Enter both Home and Away team names');
            return;
        }

        predictBtn.textContent = 'Predicting...';
        try {
            const isGlobal = globalToggle ? globalToggle.checked : false;
            const res = await fetch(`/api/analytics/matchup-preview?refereeId=${refereeId}&homeTeam=${home}&awayTeam=${away}&league=${league}&season=${season}&global=${isGlobal}`);
            if (res.status === 404) {
                matchupResult.textContent = 'No data for this matchup.';
            } else {
                const data = await res.json();
                matchupResult.innerHTML = `
                    <div style="font-size: 1.2rem; color: var(--accent-primary); margin-bottom: 0.5rem;">${data.advantagePrediction}</div>
                    <div style="font-size: 0.9rem; font-weight: normal;">
                        Avg YC against ${data.homeTeam}: <b>${data.avgCardsAgainstHomeTeam.toFixed(2)}</b> (${data.homeTeamMatchesWithReferee} matches)<br>
                        Avg YC against ${data.awayTeam}: <b>${data.avgCardsAgainstAwayTeam.toFixed(2)}</b> (${data.awayTeamMatchesWithReferee} matches)
                    </div>
                `;
            }
            matchupResult.style.display = 'block';
        } catch (e) {
            console.error(e);
            matchupResult.textContent = 'Error computing prediction.';
            matchupResult.style.display = 'block';
        } finally {
            predictBtn.textContent = 'Predict Advantage';
        }
    });

    // Compare
    const compareModal = document.getElementById('compare-modal');
    const compareRefSelect = document.getElementById('compare-ref-select');
    const runCompareBtn = document.getElementById('run-compare-btn');
    const compareResults = document.getElementById('compare-results');
    
    compareBtn.addEventListener('click', () => {
        const selectedId = refereeSelect.value;
        if (!selectedId) return;

        document.getElementById('compare-ref-a-name').textContent = refName.textContent;
        compareRefSelect.innerHTML = refereeSelect.innerHTML;
        compareRefSelect.value = ''; // Reset selection
        compareResults.classList.add('hidden');
        compareModal.classList.remove('hidden');
    });

    runCompareBtn.addEventListener('click', async () => {
        const refA = refereeSelect.value;
        const refB = compareRefSelect.value;
        if (!refB || refA === refB) return;

        const league = leagueSelect.value;
        const season = seasonSelect.value;

        runCompareBtn.textContent = 'Loading...';
        try {
            const isGlobal = globalToggle ? globalToggle.checked : false;
            const res = await fetch(`/api/analytics/compare?refereeAId=${refA}&refereeBId=${refB}&league=${league}&season=${season}&global=${isGlobal}`);
            const data = await res.json();

            // Ref A Opt
            const optA = Array.from(refereeSelect.options).find(o => o.value === refA);
            const nameA = optA ? optA.textContent.split('(')[0].trim() : refA;
            // Ref B Opt
            const optB = Array.from(compareRefSelect.options).find(o => o.value === refB);
            const nameB = optB ? optB.textContent.split('(')[0].trim() : refB;

            document.getElementById('res-name-a').textContent = nameA;
            document.getElementById('res-yc-a').textContent = data.refereeA.averageYellowCards.toFixed(2);
            document.getElementById('res-bias-a').textContent = data.refereeA.homeCardsPercentage;

            document.getElementById('res-name-b').textContent = nameB;
            document.getElementById('res-yc-b').textContent = data.refereeB.averageYellowCards.toFixed(2);
            document.getElementById('res-bias-b').textContent = data.refereeB.homeCardsPercentage;

            const ul = document.getElementById('compare-insights');
            ul.innerHTML = '';
            data.differenceHighlights.forEach(h => {
                const li = document.createElement('li');
                li.textContent = h;
                ul.appendChild(li);
            });

            // Populate remaining comparison logic...
            showToast('✅ Comparison loaded successfully!');

            compareResults.classList.remove('hidden');
        } catch (err) {
            console.error(err);
            showToast('⚠️ Error comparing referees.');
        } finally {
            runCompareBtn.textContent = 'Compare';
        }
    });

    // Handle Team Stats Selection
    if (teamStatsSelect) {
        teamStatsSelect.addEventListener('change', (e) => {
            const selectedTeam = e.target.value;
            if (!selectedTeam) {
                teamStatsContent.classList.add('hidden');
                return;
            }

            const teamData = currentRefereeTeamHistory.find(t => t.teamId === selectedTeam);
            if (teamData) {
                tsWins.textContent = teamData.wins;
                tsDraws.textContent = teamData.draws;
                tsLosses.textContent = teamData.losses;
                tsMatches.textContent = teamData.matchesTogether;
                tsGoals.textContent = `${teamData.goalsFor} : ${teamData.goalsAgainst}`;
                tsYellows.textContent = teamData.yellowCardsAgainst;
                tsReds.textContent = teamData.redCardsAgainst;
                tsPens.textContent = `${teamData.penaltiesFor} : ${teamData.penaltiesAgainst}`;
                
                teamStatsContent.classList.remove('hidden');
            }
        });
    }

    // Handle closing modals
    document.querySelectorAll('.close-modal').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.target.closest('.modal').classList.add('hidden');
        });
    });

    // Make sure elements un-hide when a referee is selected
    const originalHandleRefereeChange = handleRefereeChange;
    handleRefereeChange = async function() {
        await originalHandleRefereeChange();
        compareBtn.classList.remove('hidden');
        matchupPanel.classList.remove('hidden');
        matchupResult.style.display = 'none';
        homeInput.value = '';
        awayInput.value = '';
    };
    refereeSelect.removeEventListener('change', originalHandleRefereeChange);
    refereeSelect.addEventListener('change', handleRefereeChange);
});
