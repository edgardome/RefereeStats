// Fetch Interceptor for JWT
const originalFetch = window.fetch;
window.fetch = async function () {
    let [resource, config] = arguments;
    if (!config) config = {};
    if (!config.headers) config.headers = {};
    
    const token = sessionStorage.getItem('jwt_token');
    if (token && resource.startsWith('/api')) {
        config.headers['Authorization'] = `Bearer ${token}`;
    }
    
    const response = await originalFetch(resource, config);
    if (response.status === 401) {
        sessionStorage.removeItem('jwt_token');
        window.location.href = '/login.html?premium=true';
    }
    return response;
};

document.addEventListener('DOMContentLoaded', () => {
    const leagueSelect = document.getElementById('league-select');
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
            totalCacheMatches: "Total Cache Matches",
            biasDesc: "Calculated card and penalty distribution between Home and Away teams",
            strongAwayBias: "STRONG AWAY BIAS",
            balanced: "BALANCED",
            strongHomeBias: "STRONG HOME BIAS",
            selectTeamRecord: "Select a team to see their specific record with this referee"
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
            totalCacheMatches: "Partidos en Caché",
            biasDesc: "Distribución calculada de tarjetas y penaltis entre local y visitante",
            strongAwayBias: "SESGO VISITANTE",
            balanced: "EQUILIBRADO",
            strongHomeBias: "SESGO LOCAL",
            selectTeamRecord: "Selecciona un equipo para ver su historial con este árbitro"
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
            totalCacheMatches: "Gesamte Spiele im Cache",
            biasDesc: "Berechnete Karten- und Elfmeterverteilung zwischen Heim und Auswärts",
            strongAwayBias: "STARK AUSWÄRTS",
            balanced: "AUSGEGLICHEN",
            strongHomeBias: "STARK HEIM",
            selectTeamRecord: "Team wählen um spezifische Bilanz zu sehen"
        }
    };

    let currentLang = 'en';

    function t(key) {
        return translations[currentLang][key] || key;
    }

    function updateLanguage() {
        currentLang = langSelect.value;
        document.querySelectorAll('[data-i18n]').forEach(el => {
            const key = el.getAttribute('data-i18n');
            if (translations[currentLang][key]) {
                el.textContent = translations[currentLang][key];
            }
        });
        
        // Re-load labels dependent on JS state
        if (refereeSelect.disabled) {
            refereeSelect.innerHTML = `<option value="">${t('loadingReferees')}</option>`;
        }
        document.getElementById('sync-button').innerHTML = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21.5 2v6h-6M21.34 15.57a10 10 0 1 1-.57-8.38l5.67-5.67"/></svg> ${t('syncLeague')}`;
        document.getElementById('leaderboard-button').innerHTML = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20v-8M6 20V4M18 20v-4"/></svg> ${t('leaderboard')}`;
        
        // Force reload of texts
        if(refereeSelect.value) {
            handleRefereeChange();
        } else {
            loadReferees();
        }
    }

    langSelect.addEventListener('change', updateLanguage);

    // Initial Translation Application
    updateLanguage();

    // Logout Logic
    const logoutBtn = document.getElementById('logout-btn');
    if (logoutBtn) {
        logoutBtn.addEventListener('click', () => {
            sessionStorage.removeItem('jwt_token');
            window.location.href = '/login.html';
        });
        
        // Hide logout button if user is not logged in
        if (!sessionStorage.getItem('jwt_token')) {
            logoutBtn.style.display = 'none';
        }
    }

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
        matchesTbody.innerHTML = '<tr><td colspan="5" class="table-empty-state">Loading matches...</td></tr>';
        
        try {
            const res = await fetch(`/api/referees/${refereeId}/matches?league=${league}&season=${season}&global=${isGlobal}`);
            if (!res.ok) throw new Error('Failed to fetch matches');

            const payload = await res.json();
            const matches = payload.content || [];
            matchesCount.textContent = `${matches.length} Matches`;
            refTotalMatches.textContent = matches.length;

            if (matches.length === 0) {
                matchesTbody.innerHTML = '<tr><td colspan="5" class="table-empty-state">No match reports in cache for this referee in this league/season.</td></tr>';
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
            matchesTbody.innerHTML = '<tr><td colspan="5" class="table-empty-state">Error loading match reports.</td></tr>';
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
