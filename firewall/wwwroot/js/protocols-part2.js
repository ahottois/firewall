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

    showAddNatRuleModal() {
        document.getElementById('modal-title').innerHTML = '<i class="fas fa-exchange-alt"></i> Ajouter une règle NAT';
        document.getElementById('modal-body').innerHTML = `
            <div class="form-group">
                <label>Nom</label>
                <input type="text" id="nat-rule-name" class="form-control" placeholder="Ex: Redirection Web">
            </div>
            <div class="form-group">
                <label>Type</label>
                <select id="nat-rule-type" class="form-control">
                    <option value="1">DNAT (Port Forwarding)</option>
                    <option value="0">SNAT (Source NAT)</option>
                    <option value="3">PAT</option>
                </select>
            </div>
            <div class="form-group">
                <label>Protocole</label>
                <select id="nat-rule-protocol" class="form-control">
                    <option value="tcp">TCP</option>
                    <option value="udp">UDP</option>
                    <option value="all">Tous</option>
                </select>
            </div>
            <div class="form-row">
                <div class="form-group">
                    <label>Port externe</label>
                    <input type="text" id="nat-rule-dest-port" class="form-control" placeholder="80">
                </div>
                <div class="form-group">
                    <label>Adresse interne</label>
                    <input type="text" id="nat-rule-trans-addr" class="form-control" placeholder="192.168.1.100">
                </div>
                <div class="form-group">
                    <label>Port interne</label>
                    <input type="text" id="nat-rule-trans-port" class="form-control" placeholder="8080">
                </div>
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-primary" onclick="app.addNatRule()">
                <i class="fas fa-plus"></i> Ajouter
            </button>
        `;
        document.getElementById('modal').classList.add('active');
    },

    async addNatRule() {
        const rule = {
            name: document.getElementById('nat-rule-name')?.value,
            type: parseInt(document.getElementById('nat-rule-type')?.value) || 1,
            protocol: document.getElementById('nat-rule-protocol')?.value || 'tcp',
            destinationPort: document.getElementById('nat-rule-dest-port')?.value,
            translatedAddress: document.getElementById('nat-rule-trans-addr')?.value,
            translatedPort: document.getElementById('nat-rule-trans-port')?.value
        };

        try {
            await this.api('networkprotocols/nat/rules', {
                method: 'POST',
                body: JSON.stringify(rule)
            });
            document.getElementById('modal').classList.remove('active');
            this.showToast({ title: 'Succès', message: 'Règle NAT ajoutée', severity: 0 });
            this.loadNatRules();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
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
