// Network Protocols Management - Part 2 (NAT, SSH, NTP, SNMP)

Object.assign(FirewallApp.prototype, {

    // ==========================================
    // NAT / PAT
    // ==========================================

    async loadNatConfig() {
        try {
            const config = await this.api('networkprotocols/nat/config');
            
            document.getElementById('nat-enabled').checked = config.enabled || false;
            document.getElementById('nat-masquerade').checked = config.masqueradeEnabled || false;

            // Charger les interfaces pour le select WAN
            const interfaces = await this.api('networkprotocols/ip/interfaces').catch(() => []);
            const wanSelect = document.getElementById('nat-wan-interface');
            if (wanSelect) {
                wanSelect.innerHTML = '<option value="">Sélectionner...</option>' +
                    interfaces.map(i => `<option value="${i.interfaceName}" ${config.wanInterface === i.interfaceName ? 'selected' : ''}>${i.interfaceName}</option>`).join('');
            }
        } catch (error) {
            console.error('Error loading NAT config:', error);
        }
    },

    async loadNatRules() {
        try {
            const rules = await this.api('networkprotocols/nat/rules');
            const tbody = document.getElementById('nat-rules-table');
            if (!tbody) return;

            if (!rules || !rules.length) {
                tbody.innerHTML = '<tr><td colspan="7" class="empty-state">Aucune règle NAT</td></tr>';
                return;
            }

            const typeNames = { 0: 'SNAT', 1: 'DNAT', 2: 'Full NAT', 3: 'PAT' };

            tbody.innerHTML = rules.map(rule => `
                <tr>
                    <td>${this.escapeHtml(rule.name)}</td>
                    <td><span class="badge">${typeNames[rule.type] || 'Unknown'}</span></td>
                    <td>${this.escapeHtml(rule.sourceAddress || '*')}:${rule.sourcePort || '*'}</td>
                    <td>${this.escapeHtml(rule.destinationAddress || '*')}:${rule.destinationPort || '*'}</td>
                    <td>${this.escapeHtml(rule.translatedAddress || '-')}:${rule.translatedPort || '-'}</td>
                    <td>
                        <span class="status-badge ${rule.enabled ? 'online' : 'offline'}">
                            ${rule.enabled ? 'Actif' : 'Inactif'}
                        </span>
                    </td>
                    <td>
                        <button class="btn btn-sm btn-danger" onclick="app.deleteNatRule(${rule.id})" title="Supprimer">
                            <i class="fas fa-trash"></i>
                        </button>
                    </td>
                </tr>
            `).join('');
        } catch (error) {
            console.error('Error loading NAT rules:', error);
        }
    },

    async loadNatConnections() {
        try {
            const connections = await this.api('networkprotocols/nat/connections');
            const container = document.getElementById('nat-connections');
            if (!container) return;

            if (!connections || !connections.length) {
                container.innerHTML = '<p class="empty-state">Aucune connexion NAT active</p>';
                return;
            }

            container.innerHTML = connections.slice(0, 50).map(conn => `
                <div class="connection-item">
                    <span class="proto ${conn.protocol.toLowerCase()}">${conn.protocol}</span>
                    <span>${this.escapeHtml(conn.originalSource)}:${conn.originalSourcePort}</span>
                    <span>? ${this.escapeHtml(conn.originalDestination)}:${conn.originalDestinationPort}</span>
                    <span class="state">${this.escapeHtml(conn.state)}</span>
                </div>
            `).join('');
        } catch (error) {
            console.error('Error loading NAT connections:', error);
        }
    },

    async toggleNat() {
        const enabled = document.getElementById('nat-enabled')?.checked;
        try {
            const config = await this.api('networkprotocols/nat/config');
            config.enabled = enabled;
            await this.api('networkprotocols/nat/config', {
                method: 'PUT',
                body: JSON.stringify(config)
            });
            this.showToast({ title: 'NAT', message: enabled ? 'NAT activé' : 'NAT désactivé', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async toggleMasquerade() {
        const enabled = document.getElementById('nat-masquerade')?.checked;
        const wanInterface = document.getElementById('nat-wan-interface')?.value;

        try {
            if (enabled) {
                if (!wanInterface) {
                    this.showToast({ title: 'Erreur', message: 'Sélectionnez une interface WAN', severity: 2 });
                    document.getElementById('nat-masquerade').checked = false;
                    return;
                }
                await this.api('networkprotocols/nat/masquerade/enable', {
                    method: 'POST',
                    body: JSON.stringify({ wanInterface })
                });
            } else {
                await this.api('networkprotocols/nat/masquerade/disable', { method: 'POST' });
            }
            this.showToast({ title: 'Masquerade', message: enabled ? 'Activé' : 'Désactivé', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    // Niveau de difficulté actuel pour le formulaire NAT
    natFormLevel: 'beginner',

    showAddNatRuleModal() {
        document.getElementById('modal-title').innerHTML = '<i class="fas fa-exchange-alt"></i> Créer une règle NAT (Redirection de port)';
        document.getElementById('modal-body').innerHTML = this.generateNatFormContent();
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-primary" onclick="app.addNatRule()">
                <i class="fas fa-plus"></i> Créer la règle
            </button>
        `;
        document.getElementById('modal').classList.add('active');
        
        // Initialiser la prévisualisation
        this.updateNatPreview();
    },

    generateNatFormContent() {
        return `
            <!-- En-tête explicatif -->
            <div class="help-container">
                <div class="help-header">
                    <i class="fas fa-lightbulb"></i>
                    <h4>Qu'est-ce qu'une règle NAT ?</h4>
                </div>
                <div class="help-description">
                    <strong>NAT</strong> (Network Address Translation) permet de <strong>rediriger le trafic</strong> d'Internet vers un appareil de votre réseau local.
                    <br><br>
                    <strong>Exemple simple :</strong> Vous avez un serveur de jeu à la maison. Vos amis veulent s'y connecter depuis Internet. 
                    Sans règle NAT, c'est impossible car votre serveur est "caché" derrière votre box/routeur. 
                    Une règle NAT dit : "Quand quelqu'un frappe à la porte 25565, redirige-le vers mon serveur Minecraft !"
                </div>
            </div>

            <!-- Schéma visuel -->
            <div class="visual-schema">
                <div class="schema-box external">
                    <div class="schema-box-icon"><i class="fas fa-globe"></i></div>
                    <div class="schema-box-label">Internet</div>
                    <div class="schema-box-value" id="schema-external">:80</div>
                </div>
                <div class="schema-arrow"><i class="fas fa-arrow-right"></i></div>
                <div class="schema-box firewall">
                    <div class="schema-box-icon"><i class="fas fa-shield-alt"></i></div>
                    <div class="schema-box-label">Votre Firewall</div>
                    <div class="schema-box-value">NAT</div>
                </div>
                <div class="schema-arrow"><i class="fas fa-arrow-right"></i></div>
                <div class="schema-box internal">
                    <div class="schema-box-icon"><i class="fas fa-server"></i></div>
                    <div class="schema-box-label">Votre serveur</div>
                    <div class="schema-box-value" id="schema-internal">192.168.1.100:8080</div>
                </div>
            </div>

            <!-- Onglets niveau de difficulté -->
            <div class="difficulty-tabs">
                <button class="difficulty-tab beginner ${this.natFormLevel === 'beginner' ? 'active' : ''}" onclick="app.setNatFormLevel('beginner')">
                    <i class="fas fa-baby"></i> Débutant
                </button>
                <button class="difficulty-tab intermediate ${this.natFormLevel === 'intermediate' ? 'active' : ''}" onclick="app.setNatFormLevel('intermediate')">
                    <i class="fas fa-user"></i> Intermédiaire
                </button>
                <button class="difficulty-tab expert ${this.natFormLevel === 'expert' ? 'active' : ''}" onclick="app.setNatFormLevel('expert')">
                    <i class="fas fa-user-ninja"></i> Expert
                </button>
            </div>

            <!-- Formulaire principal -->
            <div class="nat-form-fields">
                
                <!-- CHAMP 1: NOM -->
                <div class="form-group field-help">
                    <div class="label-with-help">
                        <span class="label-text">?? Nom de la règle</span>
                        <span class="label-badge required">Obligatoire</span>
                    </div>
                    <input type="text" id="nat-rule-name" class="form-control" 
                           placeholder="Ex: Serveur Minecraft, Caméra salon, Site web..."
                           oninput="app.updateNatPreview()">
                    <div class="field-description">
                        <i class="fas fa-info-circle"></i>
                        Donnez un nom facile à retenir pour identifier cette règle plus tard.
                    </div>
                    <div class="help-tooltip">
                        <div class="help-tooltip-title"><i class="fas fa-tag"></i> À quoi sert le nom ?</div>
                        <div class="help-tooltip-text">
                            Le nom vous aide à vous souvenir pourquoi vous avez créé cette règle. 
                            Choisissez quelque chose de descriptif !
                        </div>
                        <div class="help-example">
                            <div class="help-example-header"><i class="fas fa-check-circle"></i> Bons exemples</div>
                            <div class="help-example-content">
                                ? "Serveur Minecraft maison"<br>
                                ? "Caméra de surveillance garage"<br>
                                ? "Accès NAS depuis l'extérieur"<br>
                                ? "Règle 1" (trop vague)
                            </div>
                        </div>
                    </div>
                    <i class="fas fa-question-circle field-help-icon pulse-attention"></i>
                </div>

                <!-- CHAMP 2: TYPE DE RÈGLE -->
                <div class="form-group field-help" id="nat-type-group" style="${this.natFormLevel === 'beginner' ? 'display:none;' : ''}">
                    <div class="label-with-help">
                        <span class="label-text">?? Type de redirection</span>
                        <span class="label-badge recommended">Par défaut: DNAT</span>
                    </div>
                    <select id="nat-rule-type" class="form-control" onchange="app.updateNatPreview()">
                        <option value="1" selected>DNAT (Redirection de port) - Le plus courant</option>
                        <option value="0">SNAT (Changer l'adresse source)</option>
                        <option value="3">PAT (Redirection avec changement de port)</option>
                    </select>
                    <div class="field-description">
                        <i class="fas fa-info-circle"></i>
                        <strong>DNAT</strong> = Redirige le trafic entrant vers votre réseau. C'est ce que vous voulez dans 95% des cas !
                    </div>
                    <div class="help-tooltip">
                        <div class="help-tooltip-title"><i class="fas fa-random"></i> Les différents types expliqués</div>
                        <div class="help-tooltip-text">
                            <strong>?? DNAT (Destination NAT)</strong><br>
                            Redirige le trafic venant d'Internet vers un appareil de votre réseau.
                            <br><br>
                            <strong>?? SNAT (Source NAT)</strong><br>
                            Change l'adresse source des paquets sortants (usage avancé).
                            <br><br>
                            <strong>?? PAT (Port Address Translation)</strong><br>
                            Comme DNAT mais peut aussi changer le numéro de port.
                        </div>
                        <div class="help-example">
                            <div class="help-example-header"><i class="fas fa-lightbulb"></i> Quand utiliser quoi ?</div>
                            <div class="help-example-content">
                                <strong>DNAT:</strong> Serveur web, jeux, caméras<br>
                                <strong>SNAT:</strong> VPN, connexions sortantes spéciales<br>
                                <strong>PAT:</strong> Quand les ports interne/externe diffèrent
                            </div>
                        </div>
                    </div>
                    <i class="fas fa-question-circle field-help-icon"></i>
                </div>

                <!-- CHAMP 3: PROTOCOLE -->
                <div class="form-group field-help">
                    <div class="label-with-help">
                        <span class="label-text">?? Protocole</span>
                        <span class="label-badge required">Obligatoire</span>
                    </div>
                    <select id="nat-rule-protocol" class="form-control" onchange="app.updateNatPreview()">
                        <option value="tcp">TCP - Connexions fiables (web, email, jeux)</option>
                        <option value="udp">UDP - Streaming, voix, certains jeux</option>
                        <option value="both">TCP + UDP - Les deux (si vous ne savez pas)</option>
                    </select>
                    <div class="field-description">
                        <i class="fas fa-info-circle"></i>
                        Si vous ne savez pas, choisissez <strong>"TCP + UDP"</strong> pour être sûr que ça fonctionne.
                    </div>
                    <div class="help-tooltip">
                        <div class="help-tooltip-title"><i class="fas fa-network-wired"></i> TCP vs UDP - C'est quoi ?</div>
                        <div class="help-tooltip-text">
                            Imaginez deux façons d'envoyer un colis :<br><br>
                            <strong>?? TCP</strong> = Courrier recommandé<br>
                            Le destinataire confirme la réception. Plus lent mais sûr.<br><br>
                            <strong>?? UDP</strong> = Carte postale<br>
                            Envoi rapide sans confirmation. Plus rapide mais peut se perdre.
                        </div>
                        <div class="help-example">
                            <div class="help-example-header"><i class="fas fa-list"></i> Exemples concrets</div>
                            <div class="help-example-content">
                                <strong>TCP:</strong> Sites web (80, 443), SSH (22), Minecraft (25565)<br>
                                <strong>UDP:</strong> DNS (53), VoIP, certains jeux (voix)<br>
                                <strong>Les deux:</strong> Discord, certains jeux multijoueur
                            </div>
                        </div>
                    </div>
                    <i class="fas fa-question-circle field-help-icon"></i>
                </div>

                <!-- CHAMP 4: PORT EXTERNE -->
                <div class="form-group field-help">
                    <div class="label-with-help">
                        <span class="label-text">?? Port externe (d'entrée)</span>
                        <span class="label-badge required">Obligatoire</span>
                    </div>
                    <input type="text" id="nat-rule-dest-port" class="form-control" 
                           placeholder="Ex: 80, 25565, 8080..."
                           oninput="app.updateNatPreview()">
                    <div class="field-description">
                        <i class="fas fa-info-circle"></i>
                        C'est le "numéro de porte" par lequel les visiteurs d'Internet vont frapper.
                    </div>
                    <div class="help-tooltip">
                        <div class="help-tooltip-title"><i class="fas fa-door-open"></i> Qu'est-ce qu'un port ?</div>
                        <div class="help-tooltip-text">
                            Un <strong>port</strong> est comme un numéro de porte dans un immeuble.<br><br>
                            Votre adresse IP = l'adresse de l'immeuble<br>
                            Le port = le numéro d'appartement<br><br>
                            Chaque service utilise une "porte" différente pour ne pas se mélanger !
                        </div>
                        <div class="help-example">
                            <div class="help-example-header"><i class="fas fa-bookmark"></i> Ports courants à connaître</div>
                            <div class="help-example-content">
                                <strong>80</strong> = Sites web (HTTP)<br>
                                <strong>443</strong> = Sites web sécurisés (HTTPS)<br>
                                <strong>22</strong> = SSH (accès distant sécurisé)<br>
                                <strong>25565</strong> = Minecraft<br>
                                <strong>3389</strong> = Bureau à distance Windows<br>
                                <strong>8080</strong> = Serveur web alternatif
                            </div>
                            <div class="help-example-result">
                                ?? <strong>Astuce:</strong> Utilisez un port > 1024 pour éviter les conflits avec les services système.
                            </div>
                        </div>
                    </div>
                    <i class="fas fa-question-circle field-help-icon"></i>
                </div>

                <!-- CHAMP 5: ADRESSE IP INTERNE -->
                <div class="form-group field-help">
                    <div class="label-with-help">
                        <span class="label-text">??? Adresse IP de l'appareil cible</span>
                        <span class="label-badge required">Obligatoire</span>
                    </div>
                    <input type="text" id="nat-rule-trans-addr" class="form-control" 
                           placeholder="Ex: 192.168.1.100"
                           oninput="app.updateNatPreview()">
                    <div class="field-description">
                        <i class="fas fa-info-circle"></i>
                        L'adresse IP locale de l'appareil qui doit recevoir les connexions (votre serveur, NAS, caméra...).
                    </div>
                    <div class="help-tooltip">
                        <div class="help-tooltip-title"><i class="fas fa-map-marker-alt"></i> Trouver l'adresse IP d'un appareil</div>
                        <div class="help-tooltip-text">
                            L'adresse IP est comme l'adresse postale de votre appareil sur le réseau.<br><br>
                            Elle ressemble à : <code>192.168.X.Y</code> ou <code>10.0.X.Y</code>
                        </div>
                        <div class="help-example">
                            <div class="help-example-header"><i class="fas fa-search"></i> Comment la trouver ?</div>
                            <div class="help-example-content">
                                <strong>Windows:</strong> Tapez "ipconfig" dans l'invite de commandes<br>
                                <strong>Mac/Linux:</strong> Tapez "ifconfig" ou "ip addr" dans le terminal<br>
                                <strong>Téléphone:</strong> Paramètres WiFi > Détails de la connexion<br>
                                <strong>Routeur:</strong> Liste des appareils connectés
                            </div>
                            <div class="help-example-result">
                                ?? <strong>Important:</strong> Utilisez une IP fixe ou une réservation DHCP pour éviter que l'adresse change !
                            </div>
                        </div>
                    </div>
                    <i class="fas fa-question-circle field-help-icon"></i>
                </div>

                <!-- CHAMP 6: PORT INTERNE -->
                <div class="form-group field-help">
                    <div class="label-with-help">
                        <span class="label-text">?? Port interne (de destination)</span>
                        <span class="label-badge optional">Optionnel</span>
                    </div>
                    <input type="text" id="nat-rule-trans-port" class="form-control" 
                           placeholder="Laissez vide = même que le port externe"
                           oninput="app.updateNatPreview()">
                    <div class="field-description">
                        <i class="fas fa-info-circle"></i>
                        Le port sur lequel votre appareil écoute. Laissez vide si c'est le même que le port externe.
                    </div>
                    <div class="help-tooltip">
                        <div class="help-tooltip-title"><i class="fas fa-exchange-alt"></i> Pourquoi un port différent ?</div>
                        <div class="help-tooltip-text">
                            Parfois, vous voulez que le port externe soit différent du port interne.<br><br>
                            C'est utile pour :<br>
                            - Cacher le vrai port de votre service<br>
                            - Avoir plusieurs serveurs sur le même port externe<br>
                            - Contourner des restrictions de fournisseur Internet
                        </div>
                        <div class="help-example">
                            <div class="help-example-header"><i class="fas fa-lightbulb"></i> Exemple concret</div>
                            <div class="help-example-content">
                                Votre serveur web écoute sur le port <strong>8080</strong><br>
                                Mais vous voulez y accéder via le port <strong>80</strong> (standard)<br><br>
                                ? Port externe: 80<br>
                                ? Port interne: 8080
                            </div>
                            <div class="help-example-result">
                                <strong>Résultat:</strong> Les visiteurs tapent "monsite.com" (port 80 par défaut) et sont redirigés vers votre serveur sur 8080.
                            </div>
                        </div>
                    </div>
                    <i class="fas fa-question-circle field-help-icon"></i>
                </div>

                <!-- Options avancées (cachées par défaut) -->
                <div id="nat-advanced-options" style="${this.natFormLevel !== 'expert' ? 'display:none;' : ''}">
                    <div class="help-accordion">
                        <div class="help-accordion-header" onclick="this.parentElement.classList.toggle('open')">
                            <span class="help-accordion-title">
                                <i class="fas fa-cogs"></i> Options avancées
                            </span>
                            <i class="fas fa-chevron-down help-accordion-toggle"></i>
                        </div>
                        <div class="help-accordion-content">
                            <div class="form-group">
                                <label>Adresse source (filtrage)</label>
                                <input type="text" id="nat-rule-source-addr" class="form-control" 
                                       placeholder="Laisser vide = tout le monde">
                                <div class="field-description">
                                    <i class="fas fa-shield-alt"></i>
                                    Limitez l'accès à certaines adresses IP seulement (sécurité renforcée).
                                </div>
                            </div>
                            <div class="form-group">
                                <label class="checkbox-label">
                                    <input type="checkbox" id="nat-rule-log" checked>
                                    Journaliser les connexions (recommandé)
                                </label>
                            </div>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Prévisualisation en direct -->
            <div class="live-preview">
                <div class="live-preview-header">
                    <i class="fas fa-eye"></i> Prévisualisation de votre règle
                </div>
                <div class="live-preview-content" id="nat-preview-content">
                    Remplissez les champs ci-dessus pour voir la prévisualisation...
                </div>
            </div>

            <!-- Exemples de configurations courantes -->
            <div class="concept-card info" style="margin-top: 20px;">
                <div class="concept-header">
                    <div class="concept-icon"><i class="fas fa-book"></i></div>
                    <div>
                        <div class="concept-title">Configurations pré-faites</div>
                        <div class="concept-subtitle">Cliquez pour pré-remplir le formulaire</div>
                    </div>
                </div>
                <div class="concept-body">
                    <div style="display: flex; flex-wrap: wrap; gap: 10px; margin-top: 10px;">
                        <button class="btn btn-sm btn-secondary" onclick="app.applyNatPreset('minecraft')">
                            <i class="fas fa-gamepad"></i> Minecraft
                        </button>
                        <button class="btn btn-sm btn-secondary" onclick="app.applyNatPreset('webserver')">
                            <i class="fas fa-globe"></i> Serveur Web
                        </button>
                        <button class="btn btn-sm btn-secondary" onclick="app.applyNatPreset('ssh')">
                            <i class="fas fa-terminal"></i> SSH
                        </button>
                        <button class="btn btn-sm btn-secondary" onclick="app.applyNatPreset('camera')">
                            <i class="fas fa-video"></i> Caméra IP
                        </button>
                        <button class="btn btn-sm btn-secondary" onclick="app.applyNatPreset('nas')">
                            <i class="fas fa-hdd"></i> NAS
                        </button>
                        <button class="btn btn-sm btn-secondary" onclick="app.applyNatPreset('rdp')">
                            <i class="fas fa-desktop"></i> Bureau distant
                        </button>
                    </div>
                </div>
            </div>
        `;
    },

    setNatFormLevel(level) {
        this.natFormLevel = level;
        
        // Mettre à jour les onglets
        document.querySelectorAll('.difficulty-tab').forEach(tab => {
            tab.classList.remove('active');
            if (tab.classList.contains(level)) {
                tab.classList.add('active');
            }
        });

        // Afficher/masquer les champs selon le niveau
        const typeGroup = document.getElementById('nat-type-group');
        const advancedOptions = document.getElementById('nat-advanced-options');

        if (typeGroup) {
            typeGroup.style.display = level === 'beginner' ? 'none' : 'block';
        }
        if (advancedOptions) {
            advancedOptions.style.display = level === 'expert' ? 'block' : 'none';
        }
    },

    updateNatPreview() {
        const name = document.getElementById('nat-rule-name')?.value || 'Ma règle';
        const protocol = document.getElementById('nat-rule-protocol')?.value || 'tcp';
        const destPort = document.getElementById('nat-rule-dest-port')?.value || '???';
        const transAddr = document.getElementById('nat-rule-trans-addr')?.value || '192.168.1.???';
        const transPort = document.getElementById('nat-rule-trans-port')?.value || destPort;

        // Mettre à jour le schéma
        const schemaExternal = document.getElementById('schema-external');
        const schemaInternal = document.getElementById('schema-internal');
        if (schemaExternal) schemaExternal.textContent = `:${destPort}`;
        if (schemaInternal) schemaInternal.textContent = `${transAddr}:${transPort}`;

        // Mettre à jour la prévisualisation
        const preview = document.getElementById('nat-preview-content');
        if (preview) {
            const protocolUpper = protocol === 'both' ? 'TCP + UDP' : protocol.toUpperCase();
            preview.innerHTML = `
                <div style="margin-bottom: 10px;">
                    <strong>?? Nom:</strong> ${this.escapeHtml(name)}
                </div>
                <div style="margin-bottom: 10px;">
                    <strong>?? Protocole:</strong> ${protocolUpper}
                </div>
                <div style="margin-bottom: 10px;">
                    <strong>?? Quand quelqu'un se connecte sur:</strong> votre_ip_publique:<span style="color: var(--accent-primary);">${destPort}</span>
                </div>
                <div>
                    <strong>?? Rediriger vers:</strong> <span style="color: var(--success);">${transAddr}:${transPort}</span>
                </div>
                ${destPort && transAddr ? `
                    <div style="margin-top: 15px; padding-top: 15px; border-top: 1px dashed var(--border-color); color: var(--text-secondary); font-size: 0.85rem;">
                        <i class="fas fa-check-circle" style="color: var(--success);"></i>
                        La règle semble correcte et prête à être créée !
                    </div>
                ` : `
                    <div style="margin-top: 15px; padding-top: 15px; border-top: 1px dashed var(--border-color); color: var(--warning); font-size: 0.85rem;">
                        <i class="fas fa-exclamation-triangle"></i>
                        Veuillez remplir tous les champs obligatoires.
                    </div>
                `}
            `;
        }
    },

    applyNatPreset(preset) {
        const presets = {
            minecraft: {
                name: 'Serveur Minecraft',
                protocol: 'tcp',
                destPort: '25565',
                transPort: '25565',
                description: 'Port standard pour les serveurs Minecraft Java Edition'
            },
            webserver: {
                name: 'Serveur Web HTTP',
                protocol: 'tcp',
                destPort: '80',
                transPort: '80',
                description: 'Port standard pour les sites web non sécurisés'
            },
            ssh: {
                name: 'Accès SSH',
                protocol: 'tcp',
                destPort: '22',
                transPort: '22',
                description: 'Accès distant sécurisé en ligne de commande'
            },
            camera: {
                name: 'Caméra IP',
                protocol: 'tcp',
                destPort: '8080',
                transPort: '80',
                description: 'Accès à une caméra de surveillance (port externe différent pour sécurité)'
            },
            nas: {
                name: 'NAS - Interface Web',
                protocol: 'tcp',
                destPort: '5000',
                transPort: '5000',
                description: 'Port courant pour les interfaces web de NAS Synology'
            },
            rdp: {
                name: 'Bureau à distance Windows',
                protocol: 'tcp',
                destPort: '3389',
                transPort: '3389',
                description: 'Remote Desktop Protocol pour contrôler un PC Windows à distance'
            }
        };

        const config = presets[preset];
        if (!config) return;

        document.getElementById('nat-rule-name').value = config.name;
        document.getElementById('nat-rule-protocol').value = config.protocol;
        document.getElementById('nat-rule-dest-port').value = config.destPort;
        document.getElementById('nat-rule-trans-port').value = config.transPort;

        // Garder l'adresse IP si déjà remplie
        const addrField = document.getElementById('nat-rule-trans-addr');
        if (!addrField.value) {
            addrField.placeholder = 'Entrez l\'IP de votre ' + config.name.toLowerCase();
        }

        this.updateNatPreview();
        
        this.showToast({ 
            title: 'Configuration appliquée', 
            message: `${config.name}: ${config.description}`, 
            severity: 0 
        });
    },

    async addNatRule() {
        const name = document.getElementById('nat-rule-name')?.value;
        const destPort = document.getElementById('nat-rule-dest-port')?.value;
        const transAddr = document.getElementById('nat-rule-trans-addr')?.value;

        // Validation
        if (!name) {
            this.showToast({ title: 'Erreur', message: 'Veuillez donner un nom à votre règle', severity: 2 });
            return;
        }
        if (!destPort) {
            this.showToast({ title: 'Erreur', message: 'Veuillez spécifier le port externe', severity: 2 });
            return;
        }
        if (!transAddr) {
            this.showToast({ title: 'Erreur', message: 'Veuillez entrer l\'adresse IP de destination', severity: 2 });
            return;
        }

        const protocol = document.getElementById('nat-rule-protocol')?.value || 'tcp';
        const transPort = document.getElementById('nat-rule-trans-port')?.value || destPort;

        // Si "both" est sélectionné, créer deux règles
        if (protocol === 'both') {
            try {
                await this.api('networkprotocols/nat/rules', {
                    method: 'POST',
                    body: JSON.stringify({
                        name: `${name} (TCP)`,
                        type: parseInt(document.getElementById('nat-rule-type')?.value) || 1,
                        protocol: 'tcp',
                        destinationPort: destPort,
                        translatedAddress: transAddr,
                        translatedPort: transPort
                    })
                });
                await this.api('networkprotocols/nat/rules', {
                    method: 'POST',
                    body: JSON.stringify({
                        name: `${name} (UDP)`,
                        type: parseInt(document.getElementById('nat-rule-type')?.value) || 1,
                        protocol: 'udp',
                        destinationPort: destPort,
                        translatedAddress: transAddr,
                        translatedPort: transPort
                    })
                });
                document.getElementById('modal').classList.remove('active');
                this.showToast({ title: 'Succès', message: 'Règles NAT TCP et UDP créées !', severity: 0 });
                this.loadNatRules();
            } catch (error) {
                this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
            }
        } else {
            const rule = {
                name: name,
                type: parseInt(document.getElementById('nat-rule-type')?.value) || 1,
                protocol: protocol,
                destinationPort: destPort,
                translatedAddress: transAddr,
                translatedPort: transPort
            };

            try {
                await this.api('networkprotocols/nat/rules', {
                    method: 'POST',
                    body: JSON.stringify(rule)
                });
                document.getElementById('modal').classList.remove('active');
                this.showToast({ title: 'Succès', message: 'Règle NAT créée avec succès !', severity: 0 });
                this.loadNatRules();
            } catch (error) {
                this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
            }
        }
    },

    async deleteNatRule(id) {
        if (!confirm('Supprimer cette règle NAT ?')) return;

        try {
            await this.api(`networkprotocols/nat/rules/${id}`, { method: 'DELETE' });
            this.showToast({ title: 'Succès', message: 'Règle supprimée', severity: 0 });
            this.loadNatRules();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    // ==========================================
    // SSH
    // ==========================================

    async loadSshConfig() {
        try {
            const config = await this.api('networkprotocols/ssh/config');
            
            document.getElementById('ssh-enabled').checked = config.enabled || false;
            document.getElementById('ssh-port').value = config.port || 22;
            document.getElementById('ssh-max-auth').value = config.maxAuthTries || 3;
            document.getElementById('ssh-password-auth').checked = config.passwordAuthentication !== false;
            document.getElementById('ssh-pubkey-auth').checked = config.pubkeyAuthentication !== false;
            document.getElementById('ssh-root-login').checked = config.rootLogin || false;
        } catch (error) {
            console.error('Error loading SSH config:', error);
        }
    },

    async loadSshKeys() {
        try {
            const keys = await this.api('networkprotocols/ssh/keys');
            const container = document.getElementById('ssh-keys-list');
            if (!container) return;

            if (!keys || !keys.length) {
                container.innerHTML = '<p class="empty-state">Aucune clé SSH autorisée</p>';
                return;
            }

            container.innerHTML = keys.map(key => `
                <div class="key-item">
                    <div class="key-info">
                        <span class="key-name">${this.escapeHtml(key.name)}</span>
                        <span class="key-fingerprint">${this.escapeHtml(key.fingerprint || '')}</span>
                        <span class="key-type">${this.escapeHtml(key.keyType)}</span>
                    </div>
                    <button class="btn btn-sm btn-danger" onclick="app.deleteSshKey(${key.id})" title="Supprimer">
                        <i class="fas fa-trash"></i>
                    </button>
                </div>
            `).join('');
        } catch (error) {
            console.error('Error loading SSH keys:', error);
        }
    },

    async loadSshSessions() {
        try {
            const sessions = await this.api('networkprotocols/ssh/sessions');
            const tbody = document.getElementById('ssh-sessions-table');
            if (!tbody) return;

            if (!sessions || !sessions.length) {
                tbody.innerHTML = '<tr><td colspan="5" class="empty-state">Aucune session active</td></tr>';
                return;
            }

            tbody.innerHTML = sessions.map(session => `
                <tr>
                    <td>${this.escapeHtml(session.user)}</td>
                    <td>${this.escapeHtml(session.remoteAddress)}:${session.remotePort}</td>
                    <td>${this.escapeHtml(session.terminal || '-')}</td>
                    <td>${this.formatDate(session.connectedAt)}</td>
                    <td>
                        <button class="btn btn-sm btn-danger" onclick="app.disconnectSshSession(${session.pid})" title="Déconnecter">
                            <i class="fas fa-times"></i>
                        </button>
                    </td>
                </tr>
            `).join('');
        } catch (error) {
            console.error('Error loading SSH sessions:', error);
        }
    },

    async toggleSsh() {
        await this.saveSshConfig();
    },

    async saveSshConfig() {
        const config = {
            enabled: document.getElementById('ssh-enabled')?.checked || false,
            port: parseInt(document.getElementById('ssh-port')?.value) || 22,
            passwordAuthentication: document.getElementById('ssh-password-auth')?.checked || false,
            pubkeyAuthentication: document.getElementById('ssh-pubkey-auth')?.checked || false,
            rootLogin: document.getElementById('ssh-root-login')?.checked || false,
            maxAuthTries: parseInt(document.getElementById('ssh-max-auth')?.value) || 3
        };

        try {
            await this.api('networkprotocols/ssh/config', {
                method: 'PUT',
                body: JSON.stringify(config)
            });
            this.showToast({ title: 'Succès', message: 'Configuration SSH enregistrée', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    showAddSshKeyModal() {
        document.getElementById('modal-title').innerHTML = '<i class="fas fa-key"></i> Ajouter une clé SSH';
        document.getElementById('modal-body').innerHTML = `
            <div class="form-group">
                <label>Nom de la clé</label>
                <input type="text" id="ssh-key-name" class="form-control" placeholder="Ex: Mon PC portable">
            </div>
            <div class="form-group">
                <label>Clé publique</label>
                <textarea id="ssh-key-public" class="form-control" rows="4" placeholder="ssh-rsa AAAA... ou ssh-ed25519 AAAA..."></textarea>
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-primary" onclick="app.addSshKey()">
                <i class="fas fa-plus"></i> Ajouter
            </button>
        `;
        document.getElementById('modal').classList.add('active');
    },

    async addSshKey() {
        const name = document.getElementById('ssh-key-name')?.value;
        const publicKey = document.getElementById('ssh-key-public')?.value;

        if (!name || !publicKey) {
            this.showToast({ title: 'Erreur', message: 'Nom et clé requis', severity: 2 });
            return;
        }

        try {
            await this.api('networkprotocols/ssh/keys', {
                method: 'POST',
                body: JSON.stringify({ name, publicKey })
            });
            document.getElementById('modal').classList.remove('active');
            this.showToast({ title: 'Succès', message: 'Clé SSH ajoutée', severity: 0 });
            this.loadSshKeys();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async deleteSshKey(id) {
        if (!confirm('Supprimer cette clé SSH ?')) return;

        try {
            await this.api(`networkprotocols/ssh/keys/${id}`, { method: 'DELETE' });
            this.showToast({ title: 'Succès', message: 'Clé supprimée', severity: 0 });
            this.loadSshKeys();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async disconnectSshSession(pid) {
        if (!confirm('Déconnecter cette session SSH ?')) return;

        try {
            await this.api(`networkprotocols/ssh/sessions/${pid}/disconnect`, { method: 'POST' });
            this.showToast({ title: 'Succès', message: 'Session déconnectée', severity: 0 });
            this.loadSshSessions();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    // ==========================================
    // NTP
    // ==========================================

    async loadNtpStatus() {
        try {
            const status = await this.api('networkprotocols/ntp/status');
            
            const syncCard = document.getElementById('ntp-sync-card');
            const syncStatus = document.getElementById('ntp-sync-status');
            
            if (syncStatus) {
                syncStatus.textContent = status.synchronized ? 'Synchronisé' : 'Non synchronisé';
            }
            if (syncCard) {
                syncCard.className = `stat-card ${status.synchronized ? 'success' : 'warning'}`;
            }

            document.getElementById('ntp-current-server').textContent = status.currentServer || '-';
            document.getElementById('ntp-offset').textContent = `${(status.offset || 0).toFixed(3)} ms`;

            // Config
            const config = await this.api('networkprotocols/ntp/config');
            document.getElementById('ntp-enabled').checked = config.enabled !== false;
            document.getElementById('ntp-is-server').checked = config.isServer || false;
        } catch (error) {
            console.error('Error loading NTP status:', error);
        }
    },

    async loadNtpServers() {
        try {
            const servers = await this.api('networkprotocols/ntp/servers');
            const container = document.getElementById('ntp-servers-list');
            if (!container) return;

            if (!servers || !servers.length) {
                container.innerHTML = '<p class="empty-state">Aucun serveur NTP configuré</p>';
                return;
            }

            const statusIcons = {
                0: 'unknown', 1: 'error', 2: 'syncing', 3: 'sync', 4: 'sync'
            };

            container.innerHTML = servers.map(server => `
                <div class="ntp-server-card">
                    <div class="server-header">
                        <span class="server-address">${this.escapeHtml(server.address)}</span>
                        <span class="server-status ${statusIcons[server.status] || 'unknown'}"></span>
                    </div>
                    <div class="server-stats">
                        <div class="stat-item">Stratum: <span>${server.stratum || '-'}</span></div>
                        <div class="stat-item">Offset: <span>${(server.offset || 0).toFixed(2)} ms</span></div>
                        <div class="stat-item">Delay: <span>${(server.delay || 0).toFixed(2)} ms</span></div>
                        <div class="stat-item">Jitter: <span>${(server.jitter || 0).toFixed(2)} ms</span></div>
                    </div>
                    ${server.prefer ? '<span class="badge">Préféré</span>' : ''}
                </div>
            `).join('');
        } catch (error) {
            console.error('Error loading NTP servers:', error);
        }
    },

    async loadTimezones() {
        try {
            const timezones = await this.api('networkprotocols/ntp/timezones');
            const select = document.getElementById('ntp-timezone');
            if (!select) return;

            const config = await this.api('networkprotocols/ntp/config');
            const currentTz = config.timezone || Intl.DateTimeFormat().resolvedOptions().timeZone;

            select.innerHTML = timezones.map(tz => 
                `<option value="${tz}" ${tz === currentTz ? 'selected' : ''}>${tz}</option>`
            ).join('');
        } catch (error) {
            console.error('Error loading timezones:', error);
        }
    },

    async toggleNtp() {
        const enabled = document.getElementById('ntp-enabled')?.checked;
        try {
            const config = await this.api('networkprotocols/ntp/config');
            config.enabled = enabled;
            await this.api('networkprotocols/ntp/config', {
                method: 'PUT',
                body: JSON.stringify(config)
            });
            this.showToast({ title: 'NTP', message: enabled ? 'NTP activé' : 'NTP désactivé', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async syncNtpNow() {
        try {
            await this.api('networkprotocols/ntp/sync', { method: 'POST' });
            this.showToast({ title: 'NTP', message: 'Synchronisation lancée', severity: 0 });
            setTimeout(() => this.loadNtpStatus(), 2000);
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async setTimezone() {
        const timezone = document.getElementById('ntp-timezone')?.value;
        if (!timezone) return;

        try {
            await this.api('networkprotocols/ntp/timezone', {
                method: 'PUT',
                body: JSON.stringify({ timezone })
            });
            this.showToast({ title: 'Succès', message: 'Fuseau horaire modifié', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    // ==========================================
    // SNMP
    // ==========================================

    async loadSnmpConfig() {
        try {
            const config = await this.api('networkprotocols/snmp/config');
            
            document.getElementById('snmp-enabled').checked = config.enabled || false;
            document.getElementById('snmp-version').value = config.version || 2;
            document.getElementById('snmp-port').value = config.port || 161;
            document.getElementById('snmp-read-community').value = config.readCommunity || 'public';
            document.getElementById('snmp-write-community').value = '';
            document.getElementById('snmp-contact').value = config.sysContact || '';
            document.getElementById('snmp-location').value = config.sysLocation || '';

            this.onSnmpVersionChange();

            // Charger les utilisateurs v3
            if (config.version === 3) {
                await this.loadSnmpUsers();
            }
        } catch (error) {
            console.error('Error loading SNMP config:', error);
        }
    },

    onSnmpVersionChange() {
        const version = parseInt(document.getElementById('snmp-version')?.value) || 2;
        const v1v2Config = document.getElementById('snmp-v1v2-config');
        const usersCard = document.getElementById('snmp-users-card');

        if (v1v2Config) {
            v1v2Config.style.display = version <= 2 ? 'block' : 'none';
        }
        if (usersCard) {
            usersCard.style.display = version === 3 ? 'block' : 'none';
            if (version === 3) {
                this.loadSnmpUsers();
            }
        }
    },

    async loadSnmpUsers() {
        try {
            const users = await this.api('networkprotocols/snmp/users');
            const container = document.getElementById('snmp-users-list');
            if (!container) return;

            if (!users || !users.length) {
                container.innerHTML = '<p class="empty-state">Aucun utilisateur SNMPv3</p>';
                return;
            }

            const levels = { 0: 'NoAuth', 1: 'AuthNoPriv', 2: 'AuthPriv' };

            container.innerHTML = users.map(user => `
                <div class="user-item">
                    <div class="user-info">
                        <span class="user-name">${this.escapeHtml(user.username)}</span>
                        <span class="user-level">${levels[user.securityLevel] || 'Unknown'}</span>
                    </div>
                    <button class="btn btn-sm btn-danger" onclick="app.deleteSnmpUser('${this.escapeHtml(user.username)}')" title="Supprimer">
                        <i class="fas fa-trash"></i>
                    </button>
                </div>
            `).join('');
        } catch (error) {
            console.error('Error loading SNMP users:', error);
        }
    },

    async loadSnmpTraps() {
        try {
            const traps = await this.api('networkprotocols/snmp/traps');
            const container = document.getElementById('snmp-traps-list');
            if (!container) return;

            if (!traps || !traps.length) {
                container.innerHTML = '<p class="empty-state">Aucun récepteur de traps</p>';
                return;
            }

            container.innerHTML = traps.map(trap => `
                <div class="trap-item">
                    <div class="trap-info">
                        <span class="trap-address">${this.escapeHtml(trap.address)}:${trap.port}</span>
                        <span class="trap-version">v${trap.version}</span>
                    </div>
                    <div class="trap-actions">
                        <button class="btn btn-sm btn-secondary" onclick="app.testTrap('${this.escapeHtml(trap.address)}')" title="Tester">
                            <i class="fas fa-paper-plane"></i>
                        </button>
                        <button class="btn btn-sm btn-danger" onclick="app.deleteTrapReceiver('${this.escapeHtml(trap.address)}')" title="Supprimer">
                            <i class="fas fa-trash"></i>
                        </button>
                    </div>
                </div>
            `).join('');
        } catch (error) {
            console.error('Error loading SNMP traps:', error);
        }
    },

    async toggleSnmp() {
        await this.saveSnmpConfig();
    },

    async saveSnmpConfig() {
        const config = {
            enabled: document.getElementById('snmp-enabled')?.checked || false,
            version: parseInt(document.getElementById('snmp-version')?.value) || 2,
            port: parseInt(document.getElementById('snmp-port')?.value) || 161,
            readCommunity: document.getElementById('snmp-read-community')?.value || 'public',
            writeCommunity: document.getElementById('snmp-write-community')?.value || undefined,
            sysContact: document.getElementById('snmp-contact')?.value,
            sysLocation: document.getElementById('snmp-location')?.value
        };

        try {
            await this.api('networkprotocols/snmp/config', {
                method: 'PUT',
                body: JSON.stringify(config)
            });
            this.showToast({ title: 'Succès', message: 'Configuration SNMP enregistrée', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    showAddSnmpUserModal() {
        document.getElementById('modal-title').innerHTML = '<i class="fas fa-user-shield"></i> Ajouter un utilisateur SNMPv3';
        document.getElementById('modal-body').innerHTML = `
            <div class="form-group">
                <label>Nom d'utilisateur</label>
                <input type="text" id="snmp-user-name" class="form-control">
            </div>
            <div class="form-group">
                <label>Niveau de sécurité</label>
                <select id="snmp-user-level" class="form-control" onchange="app.onSnmpUserLevelChange()">
                    <option value="0">NoAuthNoPriv</option>
                    <option value="1">AuthNoPriv</option>
                    <option value="2" selected>AuthPriv</option>
                </select>
            </div>
            <div id="snmp-auth-config">
                <div class="form-group">
                    <label>Protocole d'authentification</label>
                    <select id="snmp-user-auth-proto" class="form-control">
                        <option value="2">SHA</option>
                        <option value="1">MD5</option>
                    </select>
                </div>
                <div class="form-group">
                    <label>Mot de passe d'authentification</label>
                    <input type="password" id="snmp-user-auth-pass" class="form-control">
                </div>
            </div>
            <div id="snmp-priv-config">
                <div class="form-group">
                    <label>Protocole de chiffrement</label>
                    <select id="snmp-user-priv-proto" class="form-control">
                        <option value="2">AES128</option>
                        <option value="1">DES</option>
                    </select>
                </div>
                <div class="form-group">
                    <label>Mot de passe de chiffrement</label>
                    <input type="password" id="snmp-user-priv-pass" class="form-control">
                </div>
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-primary" onclick="app.addSnmpUser()">
                <i class="fas fa-plus"></i> Ajouter
            </button>
        `;
        document.getElementById('modal').classList.add('active');
    },

    onSnmpUserLevelChange() {
        const level = parseInt(document.getElementById('snmp-user-level')?.value) || 0;
        const authConfig = document.getElementById('snmp-auth-config');
        const privConfig = document.getElementById('snmp-priv-config');

        if (authConfig) authConfig.style.display = level >= 1 ? 'block' : 'none';
        if (privConfig) privConfig.style.display = level >= 2 ? 'block' : 'none';
    },

    async addSnmpUser() {
        const level = parseInt(document.getElementById('snmp-user-level')?.value) || 0;
        const user = {
            username: document.getElementById('snmp-user-name')?.value,
            securityLevel: level,
            authProtocol: level >= 1 ? parseInt(document.getElementById('snmp-user-auth-proto')?.value) : 0,
            authPassword: level >= 1 ? document.getElementById('snmp-user-auth-pass')?.value : undefined,
            privProtocol: level >= 2 ? parseInt(document.getElementById('snmp-user-priv-proto')?.value) : 0,
            privPassword: level >= 2 ? document.getElementById('snmp-user-priv-pass')?.value : undefined
        };

        try {
            await this.api('networkprotocols/snmp/users', {
                method: 'POST',
                body: JSON.stringify(user)
            });
            document.getElementById('modal').classList.remove('active');
            this.showToast({ title: 'Succès', message: 'Utilisateur SNMP ajouté', severity: 0 });
            this.loadSnmpUsers();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async deleteSnmpUser(username) {
        if (!confirm(`Supprimer l'utilisateur ${username} ?`)) return;

        try {
            await this.api(`networkprotocols/snmp/users/${encodeURIComponent(username)}`, { method: 'DELETE' });
            this.showToast({ title: 'Succès', message: 'Utilisateur supprimé', severity: 0 });
            this.loadSnmpUsers();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    showAddTrapReceiverModal() {
        document.getElementById('modal-title').innerHTML = '<i class="fas fa-bell"></i> Ajouter un récepteur de traps';
        document.getElementById('modal-body').innerHTML = `
            <div class="form-group">
                <label>Adresse</label>
                <input type="text" id="trap-address" class="form-control" placeholder="192.168.1.100">
            </div>
            <div class="form-group">
                <label>Port</label>
                <input type="number" id="trap-port" class="form-control" value="162">
            </div>
            <div class="form-group">
                <label>Version SNMP</label>
                <select id="trap-version" class="form-control">
                    <option value="2">v2c</option>
                    <option value="1">v1</option>
                </select>
            </div>
            <div class="form-group">
                <label>Communauté</label>
                <input type="text" id="trap-community" class="form-control" value="public">
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-primary" onclick="app.addTrapReceiver()">
                <i class="fas fa-plus"></i> Ajouter
            </button>
        `;
        document.getElementById('modal').classList.add('active');
    },

    async addTrapReceiver() {
        const receiver = {
            address: document.getElementById('trap-address')?.value,
            port: parseInt(document.getElementById('trap-port')?.value) || 162,
            version: parseInt(document.getElementById('trap-version')?.value) || 2,
            community: document.getElementById('trap-community')?.value || 'public',
            enabled: true
        };

        try {
            await this.api('networkprotocols/snmp/traps', {
                method: 'POST',
                body: JSON.stringify(receiver)
            });
            document.getElementById('modal').classList.remove('active');
            this.showToast({ title: 'Succès', message: 'Récepteur ajouté', severity: 0 });
            this.loadSnmpTraps();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async deleteTrapReceiver(address) {
        if (!confirm(`Supprimer le récepteur ${address} ?`)) return;

        try {
            await this.api(`networkprotocols/snmp/traps/${encodeURIComponent(address)}`, { method: 'DELETE' });
            this.showToast({ title: 'Succès', message: 'Récepteur supprimé', severity: 0 });
            this.loadSnmpTraps();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async testTrap(address) {
        try {
            await this.api(`networkprotocols/snmp/traps/${encodeURIComponent(address)}/test`, { method: 'POST' });
            this.showToast({ title: 'SNMP', message: 'Trap de test envoyé', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    // ==========================================
    // PROTOCOLS PAGE LOADER
    // ==========================================

    loadProtocols() {
        this.switchProtocolTab('ip-icmp');
    }
});
