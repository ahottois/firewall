// app-pages.js - Méthodes de chargement des pages manquantes

Object.assign(FirewallApp.prototype, {

    // ==========================================
    // DEVICES PAGE
    // ==========================================

    async loadDevices() {
        try {
            const devices = await this.api('devices');
            this.currentDevices = devices;
            this.renderDevicesTable(devices);
        } catch (error) {
            console.error('Error loading devices:', error);
        }
    },

    renderDevicesTable(devices) {
        const tbody = document.getElementById('devices-table');
        if (!tbody) return;

        if (!devices || !devices.length) {
            tbody.innerHTML = '<tr><td colspan="7" class="empty-state">Aucun appareil détecté</td></tr>';
            return;
        }

        tbody.innerHTML = devices.map(device => {
            const statusClass = this.getStatusClass(device.status);
            const statusText = this.getStatusText(device.status);
            const isBlocked = device.isBlocked;
            
            return `
                <tr class="${isBlocked ? 'blocked-row' : ''}">
                    <td>
                        <span class="status-indicator ${statusClass}"></span>
                        ${statusText}
                    </td>
                    <td class="device-mac">${this.escapeHtml(device.macAddress)}</td>
                    <td>${this.escapeHtml(device.ipAddress || '-')}</td>
                    <td>${this.escapeHtml(device.vendor || 'Inconnu')}</td>
                    <td>${this.escapeHtml(device.description || device.hostname || '-')}</td>
                    <td>${this.formatDate(device.lastSeen)}</td>
                    <td class="actions">
                        <button class="btn btn-sm btn-primary" onclick="app.viewDevice(${device.id})" title="Détails">
                            <i class="fas fa-eye"></i>
                        </button>
                        ${!device.isKnown ? `
                            <button class="btn btn-sm btn-success" onclick="app.approveDevice(${device.id})" title="Approuver">
                                <i class="fas fa-check"></i>
                            </button>
                        ` : ''}
                        ${isBlocked ? `
                            <button class="btn btn-sm btn-warning" onclick="app.unblockDevice(${device.id})" title="Débloquer">
                                <i class="fas fa-unlock"></i>
                            </button>
                        ` : `
                            <button class="btn btn-sm btn-danger" onclick="app.blockDevice(${device.id})" title="Bloquer">
                                <i class="fas fa-ban"></i>
                            </button>
                        `}
                        <button class="btn btn-sm btn-danger" onclick="app.deleteDevice(${device.id})" title="Supprimer">
                            <i class="fas fa-trash"></i>
                        </button>
                    </td>
                </tr>
            `;
        }).join('');
    },

    filterDevices(filter) {
        let filtered = this.currentDevices;
        
        switch (filter) {
            case 'online':
                filtered = this.currentDevices.filter(d => d.status === 1 || d.status === 'Online');
                break;
            case 'unknown':
                filtered = this.currentDevices.filter(d => !d.isKnown);
                break;
            case 'blocked':
                filtered = this.currentDevices.filter(d => d.isBlocked);
                break;
        }
        
        this.renderDevicesTable(filtered);
    },

    async blockDevice(id) {
        try {
            await this.api(`devices/${id}/block`, { method: 'POST' });
            this.loadDevices();
            this.showToast({ title: 'Succès', message: 'Appareil bloqué', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async unblockDevice(id) {
        try {
            await this.api(`devices/${id}/unblock`, { method: 'POST' });
            this.loadDevices();
            this.showToast({ title: 'Succès', message: 'Appareil débloqué', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async scanNetwork() {
        const btn = document.getElementById('scan-network-btn');
        const icon = document.getElementById('scan-icon');
        const status = document.getElementById('scan-status');
        const logsPanel = document.getElementById('scan-logs-panel');
        const logsEl = document.getElementById('device-scan-logs');
        
        if (btn) btn.disabled = true;
        if (icon) icon.className = 'fas fa-spinner fa-spin';
        if (status) status.textContent = 'Scan en cours...';
        if (logsPanel) logsPanel.style.display = 'block';
        if (logsEl) logsEl.textContent = '';

        try {
            const result = await this.api('devices/scan', { method: 'POST' });
            
            if (logsEl && result.logs) {
                logsEl.textContent = result.logs.join('\n');
            }
            
            this.showToast({ title: 'Scan terminé', message: `${result.devicesFound || 0} appareils trouvés`, severity: 0 });
            await this.loadDevices();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        } finally {
            if (btn) btn.disabled = false;
            if (icon) icon.className = 'fas fa-radar';
            if (status) status.textContent = '';
        }
    },

    clearScanLogs() {
        const logsEl = document.getElementById('device-scan-logs');
        if (logsEl) logsEl.textContent = '';
    },

    showAddDeviceModal() {
        document.getElementById('modal-title').innerHTML = '<i class="fas fa-plus"></i> Ajouter un appareil';
        document.getElementById('modal-body').innerHTML = `
            <div class="form-group">
                <label>Adresse MAC</label>
                <input type="text" id="new-device-mac" class="form-control" placeholder="AA:BB:CC:DD:EE:FF">
            </div>
            <div class="form-group">
                <label>Adresse IP (optionnel)</label>
                <input type="text" id="new-device-ip" class="form-control" placeholder="192.168.1.100">
            </div>
            <div class="form-group">
                <label>Description</label>
                <input type="text" id="new-device-description" class="form-control" placeholder="Mon appareil">
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-primary" onclick="app.addDevice()">
                <i class="fas fa-plus"></i> Ajouter
            </button>
        `;
        document.getElementById('modal').classList.add('active');
    },

    async addDevice() {
        const device = {
            macAddress: document.getElementById('new-device-mac')?.value,
            ipAddress: document.getElementById('new-device-ip')?.value,
            description: document.getElementById('new-device-description')?.value,
            isKnown: true
        };

        try {
            await this.api('devices', {
                method: 'POST',
                body: JSON.stringify(device)
            });
            document.getElementById('modal').classList.remove('active');
            this.showToast({ title: 'Succès', message: 'Appareil ajouté', severity: 0 });
            this.loadDevices();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async cleanupDevices() {
        if (!confirm('Supprimer les appareils Docker/fantômes ?')) return;
        
        try {
            await this.api('devices/cleanup', { method: 'POST' });
            this.showToast({ title: 'Succès', message: 'Appareils nettoyés', severity: 0 });
            this.loadDevices();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async purgeDevices() {
        if (!confirm('ATTENTION: Supprimer TOUS les appareils ? Cette action est irréversible.')) return;
        
        try {
            await this.api('devices/purge', { method: 'DELETE' });
            this.showToast({ title: 'Succès', message: 'Tous les appareils supprimés', severity: 0 });
            this.loadDevices();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    // ==========================================
    // PI-HOLE PAGE
    // ==========================================

    async loadPihole() {
        try {
            const status = await this.api('pihole/status');
            
            // Vérifier si Pi-hole est disponible sur ce système
            if (status.notLinux) {
                document.getElementById('pihole-not-linux').style.display = 'block';
                document.getElementById('pihole-not-installed').style.display = 'none';
                document.getElementById('pihole-installed').style.display = 'none';
                return;
            }

            if (!status.installed) {
                document.getElementById('pihole-not-linux').style.display = 'none';
                document.getElementById('pihole-not-installed').style.display = 'block';
                document.getElementById('pihole-installed').style.display = 'none';
                return;
            }

            // Pi-hole est installé
            document.getElementById('pihole-not-linux').style.display = 'none';
            document.getElementById('pihole-not-installed').style.display = 'none';
            document.getElementById('pihole-installed').style.display = 'block';

            // Mettre à jour le statut
            const statusText = document.getElementById('pihole-status-text');
            const statusCard = document.getElementById('pihole-status-card');
            if (statusText) statusText.textContent = status.running ? 'Actif' : 'Inactif';
            if (statusCard) statusCard.className = `stat-card ${status.running ? 'success' : 'danger'}`;

            // Mettre à jour le blocage
            const blockingText = document.getElementById('pihole-blocking-text');
            const blockingCard = document.getElementById('pihole-blocking-card');
            if (blockingText) blockingText.textContent = status.blocking ? 'Activé' : 'Désactivé';
            if (blockingCard) blockingCard.className = `stat-card ${status.blocking ? 'success' : 'warning'}`;

            // Version
            const versionEl = document.getElementById('pihole-version');
            if (versionEl) versionEl.textContent = status.version || '-';

            // Boutons activer/désactiver
            document.getElementById('btn-enable-pihole').style.display = status.blocking ? 'none' : 'inline-flex';
            document.getElementById('btn-disable-pihole').style.display = status.blocking ? 'inline-flex' : 'none';

            // Charger les statistiques
            if (status.running) {
                await this.loadPiholeStats();
            }
        } catch (error) {
            console.error('Error loading Pi-hole:', error);
        }
    },

    async loadPiholeStats() {
        try {
            const stats = await this.api('pihole/stats');
            
            document.getElementById('ph-queries').textContent = this.formatNumber(stats.totalQueries || 0);
            document.getElementById('ph-blocked').textContent = this.formatNumber(stats.blockedQueries || 0);
            document.getElementById('ph-percent').textContent = `${(stats.percentBlocked || 0).toFixed(1)}%`;
            document.getElementById('ph-domains').textContent = this.formatNumber(stats.domainsBlocked || 0);
            document.getElementById('ph-clients').textContent = stats.uniqueClients || 0;
            document.getElementById('ph-gravity').textContent = stats.gravityLastUpdated || 'Jamais';
            document.getElementById('ph-reply-ip').textContent = stats.replyIP || 0;
            document.getElementById('ph-reply-nx').textContent = stats.replyNXDOMAIN || 0;
        } catch (error) {
            console.error('Error loading Pi-hole stats:', error);
        }
    },

    formatNumber(num) {
        if (num >= 1000000) return (num / 1000000).toFixed(1) + 'M';
        if (num >= 1000) return (num / 1000).toFixed(1) + 'K';
        return num.toString();
    },

    async installPihole() {
        this.showToast({ title: 'Pi-hole', message: 'Installation en cours...', severity: 0 });
        
        try {
            await this.api('pihole/install', { method: 'POST' });
            this.showToast({ title: 'Pi-hole', message: 'Installation terminée', severity: 0 });
            this.loadPihole();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    // ==========================================
    // PARENTAL CONTROL PAGE
    // ==========================================

    async loadParental() {
        try {
            const profiles = await this.api('parental/profiles');
            this.renderParentalProfiles(profiles);
        } catch (error) {
            console.error('Error loading parental control:', error);
        }
    },

    renderParentalProfiles(profiles) {
        const grid = document.getElementById('parental-profiles-grid');
        const empty = document.getElementById('parental-empty');
        
        if (!profiles || !profiles.length) {
            if (empty) empty.style.display = 'block';
            return;
        }

        if (empty) empty.style.display = 'none';
        
        const profilesHtml = profiles.map(profile => {
            const isActive = profile.isActive;
            const statusClass = isActive ? 'online' : 'offline';
            
            return `
                <div class="profile-card ${statusClass}">
                    <div class="profile-avatar">
                        <i class="fas fa-user-circle"></i>
                    </div>
                    <h4>${this.escapeHtml(profile.name)}</h4>
                    <p class="text-muted">${profile.linkedDevices?.length || 0} appareil(s)</p>
                    <span class="status-badge ${statusClass}">
                        ${isActive ? 'Internet actif' : 'Internet désactivé'}
                    </span>
                    <div class="profile-actions">
                        <button class="btn btn-sm btn-primary" onclick="app.editProfile(${profile.id})">
                            <i class="fas fa-edit"></i>
                        </button>
                        <button class="btn btn-sm ${isActive ? 'btn-warning' : 'btn-success'}" 
                                onclick="app.toggleProfileInternet(${profile.id}, ${!isActive})">
                            <i class="fas fa-${isActive ? 'pause' : 'play'}"></i>
                        </button>
                        <button class="btn btn-sm btn-danger" onclick="app.deleteProfile(${profile.id})">
                            <i class="fas fa-trash"></i>
                        </button>
                    </div>
                </div>
            `;
        }).join('');
        
        grid.innerHTML = profilesHtml;
    },

    showCreateProfileModal() {
        document.getElementById('modal-title').innerHTML = '<i class="fas fa-child"></i> Créer un profil';
        document.getElementById('modal-body').innerHTML = `
            <div class="form-group">
                <label>Nom de l'enfant</label>
                <input type="text" id="profile-name" class="form-control" placeholder="Ex: Thomas">
            </div>
            <div class="form-group">
                <label>Âge</label>
                <input type="number" id="profile-age" class="form-control" min="1" max="18" value="10">
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-primary" onclick="app.createProfile()">
                <i class="fas fa-plus"></i> Créer
            </button>
        `;
        document.getElementById('modal').classList.add('active');
    },

    async createProfile() {
        const profile = {
            name: document.getElementById('profile-name')?.value,
            age: parseInt(document.getElementById('profile-age')?.value) || 10
        };

        try {
            await this.api('parental/profiles', {
                method: 'POST',
                body: JSON.stringify(profile)
            });
            document.getElementById('modal').classList.remove('active');
            this.showToast({ title: 'Succès', message: 'Profil créé', severity: 0 });
            this.loadParental();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async toggleProfileInternet(profileId, enable) {
        try {
            await this.api(`parental/profiles/${profileId}/internet`, {
                method: 'POST',
                body: JSON.stringify({ enabled: enable })
            });
            this.loadParental();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async deleteProfile(profileId) {
        if (!confirm('Supprimer ce profil ?')) return;

        try {
            await this.api(`parental/profiles/${profileId}`, { method: 'DELETE' });
            this.showToast({ title: 'Succès', message: 'Profil supprimé', severity: 0 });
            this.loadParental();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    // ==========================================
    // ROUTER / SECURITY RULES PAGE
    // ==========================================

    async loadRouter() {
        await Promise.all([
            this.loadRouterInterfaces(),
            this.loadRouterMappings()
        ]);
    },

    async loadRouterInterfaces() {
        try {
            const interfaces = await this.api('router/interfaces');
            const container = document.getElementById('router-interfaces-list');
            if (!container) return;

            if (!interfaces || !interfaces.length) {
                container.innerHTML = '<p class="empty-state">Aucune interface trouvée</p>';
                return;
            }

            container.innerHTML = interfaces.map(iface => `
                <div class="interface-card ${iface.status === 'Up' ? 'online' : 'offline'}">
                    <div class="interface-header">
                        <span class="interface-name">${this.escapeHtml(iface.name)}</span>
                        <span class="interface-status ${iface.status === 'Up' ? 'online' : 'offline'}">
                            ${iface.status === 'Up' ? 'Actif' : 'Inactif'}
                        </span>
                    </div>
                    <div class="interface-details">
                        <div><strong>Type:</strong> ${this.escapeHtml(iface.type || '-')}</div>
                        <div><strong>IP:</strong> ${this.escapeHtml(iface.ipAddress || '-')}</div>
                        <div><strong>MAC:</strong> ${this.escapeHtml(iface.macAddress || '-')}</div>
                        <div><strong>Vitesse:</strong> ${iface.speed || '-'} Mbps</div>
                    </div>
                </div>
            `).join('');
        } catch (error) {
            console.error('Error loading router interfaces:', error);
        }
    },

    async loadRouterMappings() {
        try {
            const mappings = await this.api('router/mappings');
            const tbody = document.getElementById('router-mappings-table');
            if (!tbody) return;

            if (!mappings || !mappings.length) {
                tbody.innerHTML = '<tr><td colspan="5" class="empty-state">Aucune règle de transfert</td></tr>';
                return;
            }

            tbody.innerHTML = mappings.map(m => `
                <tr>
                    <td>${this.escapeHtml(m.name || '-')}</td>
                    <td>${m.externalPort}</td>
                    <td>${this.escapeHtml(m.internalIp)}:${m.internalPort}</td>
                    <td>${this.escapeHtml(m.protocol || 'TCP')}</td>
                    <td>
                        <button class="btn btn-sm btn-danger" onclick="app.deleteMapping(${m.id})" title="Supprimer">
                            <i class="fas fa-trash"></i>
                        </button>
                    </td>
                </tr>
            `).join('');
        } catch (error) {
            console.error('Error loading router mappings:', error);
        }
    },

    showAddMappingModal() {
        document.getElementById('modal-title').innerHTML = '<i class="fas fa-exchange-alt"></i> Ajouter une règle NAT';
        document.getElementById('modal-body').innerHTML = `
            <div class="form-group">
                <label>Nom de la règle</label>
                <input type="text" id="mapping-name" class="form-control" placeholder="Ex: Serveur Web">
            </div>
            <div class="form-row">
                <div class="form-group">
                    <label>Port externe</label>
                    <input type="number" id="mapping-external-port" class="form-control" placeholder="80">
                </div>
                <div class="form-group">
                    <label>Protocole</label>
                    <select id="mapping-protocol" class="form-control">
                        <option value="TCP">TCP</option>
                        <option value="UDP">UDP</option>
                        <option value="Both">TCP & UDP</option>
                    </select>
                </div>
            </div>
            <div class="form-row">
                <div class="form-group">
                    <label>IP interne</label>
                    <input type="text" id="mapping-internal-ip" class="form-control" placeholder="192.168.1.100">
                </div>
                <div class="form-group">
                    <label>Port interne</label>
                    <input type="number" id="mapping-internal-port" class="form-control" placeholder="80">
                </div>
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-primary" onclick="app.addMapping()">
                <i class="fas fa-plus"></i> Ajouter
            </button>
        `;
        document.getElementById('modal').classList.add('active');
    },

    async addMapping() {
        const mapping = {
            name: document.getElementById('mapping-name')?.value,
            externalPort: parseInt(document.getElementById('mapping-external-port')?.value),
            internalIp: document.getElementById('mapping-internal-ip')?.value,
            internalPort: parseInt(document.getElementById('mapping-internal-port')?.value),
            protocol: document.getElementById('mapping-protocol')?.value
        };

        try {
            await this.api('router/mappings', {
                method: 'POST',
                body: JSON.stringify(mapping)
            });
            document.getElementById('modal').classList.remove('active');
            this.showToast({ title: 'Succès', message: 'Règle ajoutée', severity: 0 });
            this.loadRouterMappings();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async deleteMapping(id) {
        if (!confirm('Supprimer cette règle ?')) return;

        try {
            await this.api(`router/mappings/${id}`, { method: 'DELETE' });
            this.showToast({ title: 'Succès', message: 'Règle supprimée', severity: 0 });
            this.loadRouterMappings();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    // ==========================================
    // SETTINGS PAGE
    // ==========================================

    async loadSettings() {
        try {
            const [system, interfaces] = await Promise.all([
                this.api('settings/system').catch(() => ({})),
                this.api('settings/interfaces').catch(() => [])
            ]);

            // Informations système
            const systemInfo = document.getElementById('system-info');
            if (systemInfo) {
                systemInfo.innerHTML = `
                    <div class="info-row"><strong>Système:</strong> ${this.escapeHtml(system.os || '-')}</div>
                    <div class="info-row"><strong>Hostname:</strong> ${this.escapeHtml(system.hostname || '-')}</div>
                    <div class="info-row"><strong>Architecture:</strong> ${this.escapeHtml(system.architecture || '-')}</div>
                    <div class="info-row"><strong>Processeurs:</strong> ${system.processorCount || '-'}</div>
                    <div class="info-row"><strong>Mémoire:</strong> ${system.totalMemory || '-'}</div>
                    <div class="info-row"><strong>Uptime:</strong> ${system.uptime || '-'}</div>
                `;
            }

            // Interfaces réseau
            const interfacesList = document.getElementById('interfaces-list');
            if (interfacesList && interfaces.length) {
                interfacesList.innerHTML = interfaces.map(iface => `
                    <div class="interface-card ${iface.status === 'Up' ? 'online' : 'offline'}">
                        <div class="interface-header">
                            <span class="interface-name">${this.escapeHtml(iface.name)}</span>
                            <span class="status-badge ${iface.status === 'Up' ? 'online' : 'offline'}">
                                ${iface.status === 'Up' ? 'Actif' : 'Inactif'}
                            </span>
                        </div>
                        <div class="interface-details">
                            <div><strong>Type:</strong> ${this.escapeHtml(iface.type || '-')}</div>
                            <div><strong>IPv4:</strong> ${this.escapeHtml(iface.ipv4 || '-')}</div>
                            <div><strong>IPv6:</strong> ${this.escapeHtml(iface.ipv6 || '-')}</div>
                            <div><strong>MAC:</strong> ${this.escapeHtml(iface.mac || '-')}</div>
                        </div>
                    </div>
                `).join('');
            }
        } catch (error) {
            console.error('Error loading settings:', error);
        }
    },

    // ==========================================
    // ADMIN PAGE
    // ==========================================

    async loadAdmin() {
        try {
            const status = await this.api('admin/status').catch(() => ({}));
            
            // Statut du service
            const statusText = document.getElementById('service-status-text');
            const statusCard = document.getElementById('service-status-card');
            if (statusText) statusText.textContent = status.isRunning ? 'En cours' : 'Arrete';
            if (statusCard) statusCard.className = `stat-card ${status.isRunning ? 'success' : 'danger'}`;

            // Version
            const versionEl = document.getElementById('app-version');
            if (versionEl) versionEl.textContent = status.currentVersion || '1.0.0';

            // Verifier les mises a jour et charger l'historique
            await this.checkForUpdates();
            await this.loadVersionHistory();
        } catch (error) {
            console.error('Error loading admin:', error);
        }
    },

    async startService() {
        try {
            await this.api('admin/service/start', { method: 'POST' });
            this.showToast({ title: 'Succès', message: 'Service démarré', severity: 0 });
            this.loadAdmin();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async stopService() {
        try {
            await this.api('admin/service/stop', { method: 'POST' });
            this.showToast({ title: 'Succès', message: 'Service arrêté', severity: 0 });
            this.loadAdmin();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async restartService() {
        try {
            await this.api('admin/service/restart', { method: 'POST' });
            this.showToast({ title: 'Succès', message: 'Service redémarré', severity: 0 });
            setTimeout(() => this.loadAdmin(), 3000);
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async installService() {
        const result = document.getElementById('install-result');
        if (result) result.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Installation...';

        try {
            await this.api('admin/service/install', { method: 'POST' });
            if (result) result.innerHTML = '<span class="text-success">Service installé avec succès</span>';
            this.showToast({ title: 'Succès', message: 'Service installé', severity: 0 });
        } catch (error) {
            if (result) result.innerHTML = `<span class="text-danger">Erreur: ${this.escapeHtml(error.message)}</span>`;
        }
    },

    async uninstallService() {
        if (!confirm('Désinstaller le service ?')) return;

        try {
            await this.api('admin/service/uninstall', { method: 'POST' });
            this.showToast({ title: 'Succès', message: 'Service désinstallé', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async checkForUpdates() {
        const statusEl = document.getElementById('update-status');
        const changelogEl = document.getElementById('update-changelog');
        const currentCommitEl = document.getElementById('current-commit');
        
        if (statusEl) statusEl.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Verification...';

        try {
            const update = await this.api('admin/updates/check');
            
            // Afficher le commit actuel
            if (currentCommitEl) {
                currentCommitEl.textContent = update.localCommit || '--';
            }
            
            if (statusEl) {
                if (!update.success) {
                    statusEl.innerHTML = `<span class="text-warning"><i class="fas fa-exclamation-triangle"></i> ${this.escapeHtml(update.error || 'Erreur')}</span>`;
                    statusEl.className = 'update-status';
                } else if (update.updateAvailable) {
                    statusEl.innerHTML = `
                        <i class="fas fa-arrow-circle-up" style="font-size: 2rem; color: var(--warning);"></i>
                        <div class="update-info">
                            <div class="update-title"><i class="fas fa-exclamation-circle"></i> Mise a jour disponible!</div>
                            <div class="update-details">
                                Version actuelle: <code>${this.escapeHtml(update.localCommit)}</code> &rarr; 
                                Derniere: <code>${this.escapeHtml(update.remoteCommit)}</code>
                                <br>
                                <small>${this.escapeHtml(update.latestCommitMessage || '')}</small>
                            </div>
                        </div>
                    `;
                    statusEl.className = 'update-status available';
                    
                    // Afficher le changelog
                    if (changelogEl && update.commits && update.commits.length > 0) {
                        changelogEl.style.display = 'block';
                        const changelogList = document.getElementById('changelog-list');
                        if (changelogList) {
                            changelogList.innerHTML = update.commits.map(c => `
                                <div class="changelog-entry ${this.getCommitType(c.message)}">
                                    <span class="entry-sha">${this.escapeHtml((c.sha || '').substring(0, 7))}</span>
                                    <span class="entry-message">${this.escapeHtml(c.message || '')}</span>
                                    <span class="entry-meta">
                                        ${c.author ? this.escapeHtml(c.author) : ''}
                                        ${c.date ? '<br>' + this.formatDate(c.date) : ''}
                                    </span>
                                </div>
                            `).join('');
                        }
                    }
                } else {
                    statusEl.innerHTML = `
                        <i class="fas fa-check-circle" style="font-size: 2rem; color: var(--success);"></i>
                        <div class="update-info">
                            <div class="update-title"><i class="fas fa-check"></i> Vous etes a jour</div>
                            <div class="update-details">
                                Version: <code>${this.escapeHtml(update.localCommit)}</code>
                                <br>
                                <small>Derniere verification: ${new Date().toLocaleString('fr-FR')}</small>
                            </div>
                        </div>
                    `;
                    statusEl.className = 'update-status up-to-date';
                    if (changelogEl) changelogEl.style.display = 'none';
                }
            }
        } catch (error) {
            if (statusEl) {
                statusEl.innerHTML = '<span class="text-muted">Impossible de verifier</span>';
                statusEl.className = 'update-status';
            }
        }
    },

    async loadVersionHistory() {
        const timelineEl = document.getElementById('version-timeline');
        const releasesCard = document.getElementById('releases-card');
        const releasesListEl = document.getElementById('releases-list');
        
        if (!timelineEl) return;

        try {
            const history = await this.api('admin/updates/history?count=15');
            
            if (!history.success) {
                timelineEl.innerHTML = `
                    <div class="timeline-empty">
                        <i class="fas fa-exclamation-circle"></i>
                        <p>Impossible de charger l'historique</p>
                        <small>${this.escapeHtml(history.error || '')}</small>
                    </div>
                `;
                return;
            }

            if (!history.commits || history.commits.length === 0) {
                timelineEl.innerHTML = `
                    <div class="timeline-empty">
                        <i class="fas fa-history"></i>
                        <p>Aucun historique disponible</p>
                    </div>
                `;
                return;
            }

            // Afficher la timeline des commits
            timelineEl.innerHTML = history.commits.map((commit, index) => {
                const isCurrent = commit.isCurrent;
                const isLatest = commit.isLatest;
                const commitType = this.getCommitType(commit.message);
                
                return `
                    <div class="timeline-item ${isCurrent ? 'current' : ''} ${isLatest ? 'latest' : ''}">
                        <div class="timeline-header">
                            <div class="timeline-commit">
                                <span class="commit-sha">${this.escapeHtml(commit.shortSha)}</span>
                                <div class="commit-badges">
                                    ${isCurrent ? '<span class="commit-badge current"><i class="fas fa-check"></i> Installe</span>' : ''}
                                    ${isLatest ? '<span class="commit-badge latest"><i class="fas fa-star"></i> Dernier</span>' : ''}
                                    ${!isCurrent && index < history.commits.findIndex(c => c.isCurrent) ? '<span class="commit-badge new">Nouveau</span>' : ''}
                                </div>
                            </div>
                            <span class="timeline-date">${commit.date ? this.formatRelativeDate(commit.date) : ''}</span>
                        </div>
                        <div class="timeline-message">${this.escapeHtml(commit.message)}</div>
                        <div class="timeline-meta">
                            <span class="commit-type ${commitType}">
                                <i class="fas ${this.getCommitTypeIcon(commitType)}"></i>
                                ${this.getCommitTypeLabel(commitType)}
                            </span>
                            <span><i class="fas fa-user"></i> ${this.escapeHtml(commit.author || 'Inconnu')}</span>
                            ${commit.date ? `<span><i class="fas fa-calendar"></i> ${this.formatDate(commit.date)}</span>` : ''}
                        </div>
                    </div>
                `;
            }).join('');

            // Afficher les releases si disponibles
            if (history.releases && history.releases.length > 0 && releasesCard && releasesListEl) {
                releasesCard.style.display = 'block';
                releasesListEl.innerHTML = history.releases.map(release => `
                    <div class="release-item">
                        <div class="release-header">
                            <div class="release-tag">
                                <span class="release-tag-name">
                                    <i class="fas fa-tag"></i>
                                    ${this.escapeHtml(release.name || release.tagName)}
                                </span>
                                <span class="release-badge ${release.isPrerelease ? 'prerelease' : 'stable'}">
                                    ${release.isPrerelease ? 'Pre-release' : 'Stable'}
                                </span>
                            </div>
                            <span class="release-date">
                                ${release.publishedAt ? this.formatDate(release.publishedAt) : ''}
                            </span>
                        </div>
                        ${release.body ? `<div class="release-body">${this.escapeHtml(release.body)}</div>` : ''}
                        <div class="release-footer">
                            <a href="${this.escapeHtml(release.htmlUrl)}" target="_blank" class="btn btn-sm btn-secondary">
                                <i class="fas fa-external-link-alt"></i> Voir sur GitHub
                            </a>
                        </div>
                    </div>
                `).join('');
            } else if (releasesCard) {
                releasesCard.style.display = 'none';
            }

        } catch (error) {
            console.error('Error loading version history:', error);
            timelineEl.innerHTML = `
                <div class="timeline-empty">
                    <i class="fas fa-exclamation-triangle"></i>
                    <p>Erreur de chargement</p>
                    <small>${this.escapeHtml(error.message || '')}</small>
                </div>
            `;
        }
    },

    getCommitType(message) {
        if (!message) return 'other';
        const lowerMessage = message.toLowerCase();
        if (lowerMessage.startsWith('fix') || lowerMessage.includes('bugfix') || lowerMessage.includes('correction')) return 'fix';
        if (lowerMessage.startsWith('feat') || lowerMessage.includes('feature') || lowerMessage.includes('ajout')) return 'feature';
        if (lowerMessage.startsWith('refactor')) return 'refactor';
        if (lowerMessage.startsWith('doc') || lowerMessage.includes('documentation')) return 'docs';
        if (lowerMessage.startsWith('style') || lowerMessage.includes('css') || lowerMessage.includes('ui')) return 'style';
        if (lowerMessage.startsWith('perf') || lowerMessage.includes('performance') || lowerMessage.includes('optimiz')) return 'perf';
        return 'other';
    },

    getCommitTypeIcon(type) {
        const icons = {
            'feature': 'fa-plus-circle',
            'fix': 'fa-bug',
            'refactor': 'fa-code',
            'docs': 'fa-file-alt',
            'style': 'fa-paint-brush',
            'perf': 'fa-tachometer-alt',
            'other': 'fa-code-branch'
        };
        return icons[type] || icons.other;
    },

    getCommitTypeLabel(type) {
        const labels = {
            'feature': 'Fonctionnalite',
            'fix': 'Correction',
            'refactor': 'Refactoring',
            'docs': 'Documentation',
            'style': 'Style/UI',
            'perf': 'Performance',
            'other': 'Autre'
        };
        return labels[type] || labels.other;
    },

    formatRelativeDate(dateStr) {
        if (!dateStr) return '';
        const date = new Date(dateStr);
        const now = new Date();
        const diffMs = now - date;
        const diffMins = Math.floor(diffMs / 60000);
        const diffHours = Math.floor(diffMs / 3600000);
        const diffDays = Math.floor(diffMs / 86400000);

        if (diffMins < 1) return 'A l\'instant';
        if (diffMins < 60) return `Il y a ${diffMins} min`;
        if (diffHours < 24) return `Il y a ${diffHours}h`;
        if (diffDays < 7) return `Il y a ${diffDays}j`;
        if (diffDays < 30) return `Il y a ${Math.floor(diffDays / 7)} sem`;
        return date.toLocaleDateString('fr-FR');
    },

    // ==========================================
    // SNIFFER PAGE
    // ==========================================

    async loadSniffer() {
        // Charger l'état actuel du sniffer
        try {
            const status = await this.api('sniffer/status');
            this.updateSnifferUI(status.isRunning);
        } catch (error) {
            console.error('Error loading sniffer:', error);
        }
    },

    updateSnifferUI(isRunning) {
        const startBtn = document.getElementById('btn-start-sniffer');
        const stopBtn = document.getElementById('btn-stop-sniffer');
        
        if (startBtn) startBtn.style.display = isRunning ? 'none' : 'inline-flex';
        if (stopBtn) stopBtn.style.display = isRunning ? 'inline-flex' : 'none';
    },

    async startSniffer() {
        try {
            await this.api('sniffer/start', { method: 'POST' });
            this.updateSnifferUI(true);
            this.startSnifferPolling();
            this.showToast({ title: 'Sniffer', message: 'Capture démarrée', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async stopSniffer() {
        try {
            await this.api('sniffer/stop', { method: 'POST' });
            this.updateSnifferUI(false);
            this.stopSnifferPolling();
            this.showToast({ title: 'Sniffer', message: 'Capture arrêtée', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    startSnifferPolling() {
        if (this.snifferInterval) return;
        
        this.snifferInterval = setInterval(async () => {
            try {
                const packets = await this.api('sniffer/packets?limit=100');
                this.renderSnifferPackets(packets);
            } catch (error) {
                console.error('Error fetching packets:', error);
            }
        }, 1000);
    },

    stopSnifferPolling() {
        if (this.snifferInterval) {
            clearInterval(this.snifferInterval);
            this.snifferInterval = null;
        }
    },

    renderSnifferPackets(packets) {
        const tbody = document.getElementById('sniffer-packets-table');
        if (!tbody) return;

        if (!packets || !packets.length) {
            tbody.innerHTML = '<tr><td colspan="6" class="empty-state">Aucun paquet capturé</td></tr>';
            return;
        }

        // Appliquer les filtres
        const filterIp = document.getElementById('sniffer-filter-ip')?.value;
        const filterPort = document.getElementById('sniffer-filter-port')?.value;
        const filterProto = document.getElementById('sniffer-filter-proto')?.value;
        const filterDirection = document.getElementById('sniffer-filter-direction')?.value;

        let filtered = packets;
        if (filterIp) {
            filtered = filtered.filter(p => p.sourceIp?.includes(filterIp) || p.destIp?.includes(filterIp));
        }
        if (filterPort) {
            const port = parseInt(filterPort);
            filtered = filtered.filter(p => p.sourcePort === port || p.destPort === port);
        }
        if (filterProto) {
            filtered = filtered.filter(p => p.protocol === filterProto);
        }
        if (filterDirection) {
            filtered = filtered.filter(p => p.direction === filterDirection);
        }

        tbody.innerHTML = filtered.map(p => `
            <tr>
                <td>${this.formatTime(p.timestamp)}</td>
                <td>${this.escapeHtml(p.sourceIp)}:${p.sourcePort}</td>
                <td>${this.escapeHtml(p.destIp)}:${p.destPort}</td>
                <td><span class="badge">${this.escapeHtml(p.protocol)}</span></td>
                <td>${p.size} octets</td>
                <td><span class="badge ${p.direction === 'Inbound' ? 'inbound' : p.direction === 'Outbound' ? 'outbound' : ''}">${this.escapeHtml(p.direction)}</span></td>
            </tr>
        `).join('');
    },

    formatTime(timestamp) {
        if (!timestamp) return '-';
        const date = new Date(timestamp);
        return date.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3 });
    },

    clearSnifferPackets() {
        const tbody = document.getElementById('sniffer-packets-table');
        if (tbody) {
            tbody.innerHTML = '<tr><td colspan="6" class="empty-state">Aucun paquet capturé</td></tr>';
        }
    }
});
