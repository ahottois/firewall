// Network Firewall Monitor - Frontend Application

// Icon mapping - Font Awesome classes
const Icons = {
    shield: '<i class="fas fa-shield-alt"></i>',
    dashboard: '<i class="fas fa-chart-line"></i>',
    laptop: '<i class="fas fa-laptop"></i>',
    video: '<i class="fas fa-video"></i>',
    bell: '<i class="fas fa-bell"></i>',
    network: '<i class="fas fa-network-wired"></i>',
    settings: '<i class="fas fa-cog"></i>',
    search: '<i class="fas fa-search"></i>',
    check: '<i class="fas fa-check"></i>',
    times: '<i class="fas fa-times"></i>',
    eye: '<i class="fas fa-eye"></i>',
    sync: '<i class="fas fa-sync"></i>',
    download: '<i class="fas fa-download"></i>',
    upload: '<i class="fas fa-upload"></i>',
    plus: '<i class="fas fa-plus"></i>',
    lock: '<i class="fas fa-lock"></i>',
    unlock: '<i class="fas fa-unlock"></i>',
    warning: '<i class="fas fa-exclamation-triangle"></i>',
    danger: '<i class="fas fa-exclamation-circle"></i>',
    info: '<i class="fas fa-info-circle"></i>',
    online: '<i class="fas fa-circle" style="color: #00ff88;"></i>',
    offline: '<i class="fas fa-circle" style="color: #ff4757;"></i>',
    unknown: '<i class="fas fa-question-circle"></i>',
    newDevice: '<i class="fas fa-plus-circle"></i>',
    portScan: '<i class="fas fa-search"></i>',
    arpSpoof: '<i class="fas fa-user-secret"></i>',
    traffic: '<i class="fas fa-chart-bar"></i>',
    box: '<i class="fas fa-box"></i>',
    calendar: '<i class="fas fa-calendar"></i>',
    clock: '<i class="fas fa-clock"></i>',
    key: '<i class="fas fa-key"></i>',
    checkCircle: '<i class="fas fa-check-circle" style="color: #00ff88;"></i>',
    timesCircle: '<i class="fas fa-times-circle" style="color: #ff4757;"></i>',
    spinner: '<i class="fas fa-spinner fa-spin"></i>',
    trash: '<i class="fas fa-trash"></i>',
    redo: '<i class="fas fa-redo"></i>',
    terminal: '<i class="fas fa-terminal"></i>',
    ban: '<i class="fas fa-ban"></i>',
    unlockAlt: '<i class="fas fa-unlock-alt"></i>'
};

class FirewallApp {
    constructor() {
        this.currentPage = 'dashboard';
        this.eventSource = null;
        this.scanLogSource = null;
        this.currentCameraId = null;
        this.currentSnapshot = null;
        this.currentDevices = [];
        this.currentAgents = [];
        this.sortDirection = {};
        this.snifferInterval = null;
        this.deviceHub = null;
        this.piholeLogPolling = null; // Pour le polling des logs d'installation
        this.alertHub = null;
        this.init();
    }

    init() {
        this.setupNavigation();
        this.setupEventListeners();
        this.setupSorting();
        this.connectNotifications();
        this.connectDeviceHub();
        this.loadDashboard();
        this.startAutoRefresh();
    }

    // ==========================================
    // UTILITY METHODS
    // ==========================================
    
    escapeHtml(text) {
        if (!text) return '';
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    formatDate(dateString) {
        if (!dateString) return '-';
        const date = new Date(dateString);
        const now = new Date();
        const diff = now - date;
        
        if (diff < 60000) return 'À l\'instant';
        if (diff < 3600000) return `Il y a ${Math.floor(diff / 60000)} min`;
        if (diff < 86400000) return `Il y a ${Math.floor(diff / 3600000)} h`;
        
        return date.toLocaleDateString('fr-FR', {
            day: '2-digit',
            month: '2-digit',
            hour: '2-digit',
            minute: '2-digit'
        });
    }

    getStatusClass(status) {
        const statusMap = {
            0: 'unknown', 'Unknown': 'unknown',
            1: 'online', 'Online': 'online',
            2: 'offline', 'Offline': 'offline',
            3: 'blocked', 'Blocked': 'blocked'
        };
        return statusMap[status] || 'unknown';
    }

    getStatusText(status) {
        const statusMap = {
            0: 'Inconnu', 'Unknown': 'Inconnu',
            1: 'En ligne', 'Online': 'En ligne',
            2: 'Hors ligne', 'Offline': 'Hors ligne',
            3: 'Bloqué', 'Blocked': 'Bloqué'
        };
        return statusMap[status] || 'Inconnu';
    }

    // ==========================================
    // TOAST NOTIFICATIONS
    // ==========================================

    showToast(alert) {
        const container = document.getElementById('toast-container');
        if (!container) return;

        const severityClasses = ['info', 'warning', 'danger', 'critical'];
        const severityClass = severityClasses[alert.severity] || 'info';

        const toast = document.createElement('div');
        toast.className = `toast ${severityClass}`;
        toast.innerHTML = `
            <div class="toast-header">
                <strong>${this.escapeHtml(alert.title || 'Notification')}</strong>
                <button class="toast-close">&times;</button>
            </div>
            <div class="toast-body">${this.escapeHtml(alert.message)}</div>
        `;

        toast.querySelector('.toast-close').addEventListener('click', () => {
            toast.remove();
        });

        container.appendChild(toast);

        // Auto-remove after 5 seconds
        setTimeout(() => {
            if (toast.parentElement) toast.remove();
        }, 5000);
    }

    // ==========================================
    // DASHBOARD
    // ==========================================

    async loadDashboard() {
        try {
            // Load security dashboard data
            const [security, devices, alerts] = await Promise.all([
                this.api('security/dashboard').catch(() => ({})),
                this.api('devices').catch(() => []),
                this.api('alerts?count=5').catch(() => [])
            ]);

            // Update traffic overview
            this.updateTrafficCharts(security);
            this.updateTopThreats(security.recentThreats || []);
            this.updateFirewallRules();
            this.updateSystemStatus(security);

        } catch (error) {
            console.error('Error loading dashboard:', error);
        }
    }

    updateTrafficCharts(security) {
        const stats = security.threatStats || {};
        const allowed = stats.allowedCount || 0;
        const blocked = stats.blockedCount || 0;
        const total = allowed + blocked || 1;

        const allowedPercent = Math.round((allowed / total) * 100);
        const blockedPercent = Math.round((blocked / total) * 100);

        document.getElementById('allowed-percent').textContent = `${allowedPercent}%`;
        document.getElementById('blocked-percent').textContent = `${blockedPercent}%`;

        // Update donut charts
        const allowedChart = document.getElementById('allowed-traffic-chart');
        const blockedChart = document.getElementById('blocked-traffic-chart');
        
        if (allowedChart) {
            allowedChart.style.setProperty('--percent', allowedPercent);
        }
        if (blockedChart) {
            blockedChart.style.setProperty('--percent', blockedPercent);
        }
    }

    updateTopThreats(threats) {
        const container = document.getElementById('top-threats-list');
        if (!container) return;

        if (!threats.length) {
            container.innerHTML = '<p class="empty-state">Aucune menace détectée</p>';
            return;
        }

        container.innerHTML = threats.slice(0, 5).map(threat => `
            <div class="threat-item">
                <span class="threat-name">${this.escapeHtml(threat.sourceIp || 'Inconnu')}</span>
                <span class="threat-type">${this.escapeHtml(threat.threatType || 'N/A')}</span>
                <span class="threat-count">${threat.count || 1}</span>
            </div>
        `).join('');
    }

    updateFirewallRules() {
        const tbody = document.getElementById('firewall-rules-body');
        if (!tbody) return;

        // Example static rules - can be made dynamic
        tbody.innerHTML = `
            <tr>
                <td>R001</td>
                <td><span class="status-badge online">Allow</span></td>
                <td>192.168.1.0/24</td>
                <td>Any</td>
                <td>All</td>
                <td><span class="status-badge online">Active</span></td>
            </tr>
            <tr>
                <td>R002</td>
                <td><span class="status-badge blocked">Block</span></td>
                <td>External</td>
                <td>22/TCP</td>
                <td>SSH</td>
                <td><span class="status-badge online">Active</span></td>
            </tr>
        `;
    }

    updateSystemStatus(security) {
        const score = security.securityScore || {};
        const cpuText = document.getElementById('cpu-status-text');
        if (cpuText) {
            cpuText.textContent = `Score de sécurité: ${score.overallScore || 0}/100`;
        }
    }

    startAutoRefresh() {
        // Auto-refresh dashboard every 30 seconds
        setInterval(() => {
            if (this.currentPage === 'dashboard') {
                this.loadDashboard();
            }
        }, 30000);
    }

    updateAlertBadge() {
        const badge = document.getElementById('alert-badge');
        if (badge) {
            this.api('alerts/unread/count').then(data => {
                const count = data.count || 0;
                badge.textContent = count;
                badge.style.display = count > 0 ? 'inline-flex' : 'none';
            }).catch(() => {});
        }
    }

    // ==========================================
    // ALERTS & LOGS
    // ==========================================

    async loadAlerts() {
        try {
            const [alerts, stats] = await Promise.all([
                this.api('alerts?count=50'),
                this.api('alerts/stats').catch(() => ({}))
            ]);

            // Update stats
            document.getElementById('total-alerts-count').textContent = stats.total || 0;
            document.getElementById('critical-alerts-count').textContent = stats.critical || 0;
            document.getElementById('blocked-attempts-count').textContent = stats.blocked || 0;
            document.getElementById('last-24h-count').textContent = stats.last24h || 0;

            // Render alerts table
            this.renderAlertsTable(alerts);
            
            // Load security logs
            this.loadSecurityLogs();

        } catch (error) {
            console.error('Error loading alerts:', error);
        }
    }

    renderAlertsTable(alerts) {
        const tbody = document.getElementById('alerts-table');
        if (!tbody) return;

        if (!alerts.length) {
            tbody.innerHTML = '<tr><td colspan="6" class="empty-state">Aucune alerte</td></tr>';
            return;
        }

        tbody.innerHTML = alerts.map(alert => `
            <tr class="${alert.isRead ? '' : 'unread'}">
                <td><span class="severity-badge severity-${alert.severity}">${this.getSeverityText(alert.severity)}</span></td>
                <td>${this.escapeHtml(alert.type)}</td>
                <td>${this.escapeHtml(alert.message)}</td>
                <td>${this.escapeHtml(alert.sourceIp || '-')}</td>
                <td>${this.formatDate(alert.timestamp)}</td>
                <td>
                    <button class="btn btn-sm" onclick="app.markAlertRead(${alert.id})" title="Marquer comme lu">
                        <i class="fas fa-check"></i>
                    </button>
                </td>
            </tr>
        `).join('');
    }

    getSeverityText(severity) {
        const severities = ['Info', 'Moyen', 'Haut', 'Critique'];
        return severities[severity] || 'Info';
    }

    async loadSecurityLogs() {
        try {
            const logs = await this.api('logs/security?count=50');
            const tbody = document.getElementById('security-logs-table');
            if (!tbody) return;

            if (!logs.length) {
                tbody.innerHTML = '<tr><td colspan="6" class="empty-state">Aucun log de sécurité</td></tr>';
                return;
            }

            tbody.innerHTML = logs.map(log => `
                <tr>
                    <td>${this.formatDate(log.timestamp)}</td>
                    <td><span class="severity-badge severity-${log.severity}">${log.severity}</span></td>
                    <td>${this.escapeHtml(log.category)}</td>
                    <td>${this.escapeHtml(log.actionTaken)}</td>
                    <td>${this.escapeHtml(log.message)}</td>
                    <td>${this.escapeHtml(log.sourceIp || '-')}</td>
                </tr>
            `).join('');
        } catch (error) {
            console.error('Error loading security logs:', error);
        }
    }

    filterLogs() {
        // Implement log filtering
        this.loadSecurityLogs();
    }

    async markAlertRead(id) {
        try {
            await this.api(`alerts/${id}/read`, { method: 'POST' });
            this.loadAlerts();
        } catch (error) {
            console.error('Error marking alert as read:', error);
        }
    }

    // ==========================================
    // AGENTS
    // ==========================================

    async loadAgents() {
        try {
            const agents = await this.api('agents');
            this.currentAgents = agents;

            const grid = document.getElementById('agents-grid');
            const empty = document.getElementById('agents-empty');

            if (!agents.length) {
                grid.innerHTML = '';
                empty.style.display = 'block';
                return;
            }

            empty.style.display = 'none';
            grid.innerHTML = agents.map(agent => this.createAgentCard(agent)).join('');
        } catch (error) {
            console.error('Error loading agents:', error);
        }
    }

    createAgentCard(agent) {
        const isOnline = agent.status === 1 || agent.status === 'Online';
        const statusClass = isOnline ? 'online' : 'offline';
        const statusText = isOnline ? 'En ligne' : 'Hors ligne';
        
        // Icône OS
        let osIcon = 'fa-server';
        const osLower = (agent.os || '').toLowerCase();
        if (osLower.includes('linux') || osLower.includes('ubuntu') || osLower.includes('debian')) {
            osIcon = 'fab fa-linux';
        } else if (osLower.includes('windows')) {
            osIcon = 'fab fa-windows';
        } else if (osLower.includes('mac') || osLower.includes('darwin')) {
            osIcon = 'fab fa-apple';
        }
        
        // Barres de progression pour les métriques
        const cpuPercent = agent.cpuUsage || 0;
        const memPercent = agent.memoryUsage || 0;
        const diskPercent = agent.diskUsage || 0;
        
        // Couleurs des métriques selon le niveau
        const getCpuColor = (val) => val > 80 ? 'var(--danger)' : val > 50 ? 'var(--warning)' : 'var(--accent-secondary)';
        const getMemColor = (val) => val > 85 ? 'var(--danger)' : val > 70 ? 'var(--warning)' : 'var(--success)';
        
        return `
            <div class="agent-card ${statusClass}">
                <div class="card-header">
                    <h4>${this.escapeHtml(agent.hostname)}</h4>
                    <span class="status-badge ${statusClass}">${statusText}</span>
                </div>
                <div class="card-body">
                    <div class="agent-metric">
                        <span class="agent-metric-label">
                            <i class="fas fa-network-wired"></i>
                            IP
                        </span>
                        <span class="agent-metric-value">${this.escapeHtml(agent.ipAddress || '-')}</span>
                    </div>
                    <div class="agent-metric">
                        <span class="agent-metric-label">
                            <i class="${osIcon}"></i>
                            OS
                        </span>
                        <span class="agent-metric-value" style="font-size: 0.8rem;">${this.escapeHtml(agent.os || 'Inconnu')}</span>
                    </div>
                    <div class="agent-metric">
                        <span class="agent-metric-label">
                            <i class="fas fa-microchip"></i>
                            CPU
                        </span>
                        <div class="agent-metric-bar">
                            <div class="agent-metric-bar-fill cpu" style="width: ${cpuPercent}%; background: ${getCpuColor(cpuPercent)};"></div>
                        </div>
                        <span class="agent-metric-value">${cpuPercent.toFixed(1)}%</span>
                    </div>
                    <div class="agent-metric">
                        <span class="agent-metric-label">
                            <i class="fas fa-memory"></i>
                            RAM
                        </span>
                        <div class="agent-metric-bar">
                            <div class="agent-metric-bar-fill memory" style="width: ${memPercent}%; background: ${getMemColor(memPercent)};"></div>
                        </div>
                        <span class="agent-metric-value">${memPercent.toFixed(1)}%</span>
                    </div>
                </div>
                <div class="card-footer">
                    <button class="btn btn-sm btn-primary" onclick="app.showAgentDetails(${agent.id})">
                        <i class="fas fa-info-circle"></i> Détails
                    </button>
                    <button class="btn btn-sm btn-danger" onclick="app.deleteAgent(${agent.id})">
                        <i class="fas fa-trash"></i> Supprimer
                    </button>
                </div>
            </div>
        `;
    }

    async showAgentDetails(agentId) {
        try {
            const agent = this.currentAgents.find(a => a.id === agentId);
            if (!agent) {
                this.showToast({ title: 'Erreur', message: 'Agent non trouvé', severity: 2 });
                return;
            }

            // Remplir les informations de base
            document.getElementById('agent-detail-hostname').textContent = agent.hostname || 'Inconnu';
            document.getElementById('agent-detail-os').textContent = agent.os || 'Inconnu';
            document.getElementById('agent-detail-ip').textContent = agent.ipAddress || 'Inconnue';
            document.getElementById('agent-detail-seen').textContent = this.formatDate(agent.lastSeen);

            // Informations matériel
            document.getElementById('agent-hardware-info').innerHTML = `
                <p><strong>CPU:</strong> ${agent.cpuUsage?.toFixed(1) || 0}%</p>
                <p><strong>Mémoire:</strong> ${agent.memoryUsage?.toFixed(1) || 0}%</p>
                <p><strong>Disque:</strong> ${agent.diskUsage?.toFixed(1) || 0}%</p>
            `;

            // Informations réseau
            document.getElementById('agent-network-info').innerHTML = `
                <p><strong>IP:</strong> ${this.escapeHtml(agent.ipAddress || '-')}</p>
                <p><strong>MAC:</strong> ${this.escapeHtml(agent.macAddress || '-')}</p>
            `;

            // Informations système
            document.getElementById('agent-system-info').innerHTML = `
                <p><strong>OS:</strong> ${this.escapeHtml(agent.os || '-')}</p>
                <p><strong>Version Agent:</strong> ${this.escapeHtml(agent.version || '1.0.0')}</p>
                <p><strong>Enregistré le:</strong> ${this.formatDate(agent.registeredAt)}</p>
            `;

            // Détails JSON supplémentaires si disponibles
            if (agent.detailsJson) {
                try {
                    const details = JSON.parse(agent.detailsJson);
                    
                    if (details.hardware) {
                        document.getElementById('agent-hardware-info').innerHTML += `
                            ${details.hardware.cpuModel ? `<p><strong>Processeur:</strong> ${this.escapeHtml(details.hardware.cpuModel)}</p>` : ''}
                            ${details.hardware.totalRam ? `<p><strong>RAM totale:</strong> ${details.hardware.totalRam}</p>` : ''}
                            ${details.hardware.diskTotal ? `<p><strong>Disque total:</strong> ${details.hardware.diskTotal}</p>` : ''}
                        `;
                    }
                    
                    if (details.network && details.network.interfaces) {
                        let networkHtml = document.getElementById('agent-network-info').innerHTML;
                        networkHtml += '<p style="margin-top: 10px;"><strong>Interfaces:</strong></p>';
                        details.network.interfaces.forEach(iface => {
                            networkHtml += `<p style="font-size: 0.85rem; margin-left: 10px;">• ${this.escapeHtml(iface.name)}: ${this.escapeHtml(iface.ip || '-')}</p>`;
                        });
                        document.getElementById('agent-network-info').innerHTML = networkHtml;
                    }
                    
                    if (details.system) {
                        document.getElementById('agent-system-info').innerHTML += `
                            ${details.system.hostname ? `<p><strong>Hostname:</strong> ${this.escapeHtml(details.system.hostname)}</p>` : ''}
                            ${details.system.uptime ? `<p><strong>Uptime:</strong> ${this.escapeHtml(details.system.uptime)}</p>` : ''}
                            ${details.system.kernel ? `<p><strong>Kernel:</strong> ${this.escapeHtml(details.system.kernel)}</p>` : ''}
                        `;
                    }
                } catch (e) {
                    console.warn('Impossible de parser detailsJson:', e);
                }
            }

            // Afficher le modal
            document.getElementById('agent-details-modal').classList.add('active');
        } catch (error) {
            console.error('Error showing agent details:', error);
            this.showToast({ title: 'Erreur', message: 'Impossible d\'afficher les détails', severity: 2 });
        }
    }

    showInstallAgentModal() {
        document.getElementById('install-agent-modal').classList.add('active');
    }

    async getInstallScript(os) {
        try {
            const result = await this.api(`agents/install-script/${os}`);
            const container = document.getElementById('install-script-container');
            const command = document.getElementById('install-command');
            
            command.textContent = result.script;
            container.style.display = 'block';
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de générer le script', severity: 2 });
        }
    }

    copyInstallCommand() {
        const command = document.getElementById('install-command').textContent;
        navigator.clipboard.writeText(command).then(() => {
            this.showToast({ title: 'Copié', message: 'Commande copiée dans le presse-papiers', severity: 0 });
        });
    }

    async deleteAgent(id) {
        if (!confirm('Supprimer cet agent ?')) return;
        
        try {
            await this.api(`agents/${id}`, { method: 'DELETE' });
            this.loadAgents();
            this.showToast({ title: 'Succès', message: 'Agent supprimé', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de supprimer l\'agent', severity: 2 });
        }
    }

    // ==========================================
    // DEVICE METHODS
    // ==========================================

    async viewDevice(id) {
        try {
            const device = await this.api(`devices/${id}`);
            
            document.getElementById('device-id').value = device.id;
            document.getElementById('device-mac').value = device.macAddress;
            document.getElementById('device-ip').value = device.ipAddress || '';
            document.getElementById('device-vendor').value = device.vendor || '';
            document.getElementById('device-hostname').value = device.hostname || '';
            document.getElementById('device-description').value = device.description || '';
            document.getElementById('device-is-known').checked = device.isKnown;
            document.getElementById('device-is-trusted').checked = device.isTrusted;
            document.getElementById('device-first-seen').textContent = this.formatDate(device.firstSeen);
            document.getElementById('device-last-seen').textContent = this.formatDate(device.lastSeen);

            document.getElementById('device-details-modal').classList.add('active');
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de charger les détails', severity: 2 });
        }
    }

    async saveDeviceDetails() {
        const id = document.getElementById('device-id').value;
        const data = {
            ipAddress: document.getElementById('device-ip').value,
            vendor: document.getElementById('device-vendor').value,
            hostname: document.getElementById('device-hostname').value,
            description: document.getElementById('device-description').value,
            isKnown: document.getElementById('device-is-known').checked,
            isTrusted: document.getElementById('device-is-trusted').checked
        };

        try {
            await this.api(`devices/${id}`, {
                method: 'PUT',
                body: JSON.stringify(data)
            });

            document.getElementById('device-details-modal').classList.remove('active');
            this.loadDevices();
            this.showToast({ title: 'Succès', message: 'Appareil mis à jour', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de sauvegarder', severity: 2 });
        }
    }

    async approveDevice(id) {
        try {
            await this.api(`devices/${id}/approve`, { method: 'POST' });
            this.loadDevices();
            this.showToast({ title: 'Succès', message: 'Appareil approuvé', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible d\'approuver', severity: 2 });
        }
    }

    async deleteDevice(id) {
        if (!confirm('Supprimer cet appareil ?')) return;

        try {
            await this.api(`devices/${id}`, { method: 'DELETE' });
            this.loadDevices();
            this.showToast({ title: 'Succès', message: 'Appareil supprimé', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de supprimer', severity: 2 });
        }
    }

    // ==========================================
    // OTHER PAGE LOADERS (stubs)
    // ==========================================

    async loadCameras() { /* Implement as needed */ }
    async loadTraffic() { /* Implement as needed */ }
    async loadDhcp() { /* Implement as needed */ }
    async loadSniffer() { /* Implement as needed */ }
    async loadRouter() { /* Implement as needed */ }
    async loadSettings() { /* Implement as needed */ }
    async loadAdmin() { /* Implement as needed */ }
    
    showAddMappingModal() { /* Implement as needed */ }
    showAddDeviceModal() { /* Implement as needed */ }
    saveDhcpConfig() { /* Implement as needed */ }

    // SignalR Device Hub Connection
    async connectDeviceHub() {
        if (typeof signalR === 'undefined') {
            console.warn('SignalR not loaded, real-time updates disabled');
            return;
        }

        try {
            this.deviceHub = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/devices')
                .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
                .configureLogging(signalR.LogLevel.Warning)
                .build();

            // Nouveaux appareils découverts
            this.deviceHub.on('DeviceDiscovered', (device) => {
                console.log('Nouvel appareil découvert:', device);
                this.handleDeviceDiscovered(device);
            });

            // Appareil mis à jour
            this.deviceHub.on('DeviceUpdated', (device) => {
                console.log('Appareil mis à jour:', device);
                this.handleDeviceUpdated(device);
            });

            // Changement de statut (online/offline)
            this.deviceHub.on('DeviceStatusChanged', (device) => {
                console.log('Statut appareil changé:', device);
                this.handleDeviceStatusChanged(device);
            });

            // Appareil bloqué
            this.deviceHub.on('DeviceBlocked', (device) => {
                console.log('Appareil bloqué:', device);
                this.handleDeviceBlocked(device);
            });

            // Appareil débloqué
            this.deviceHub.on('DeviceUnblocked', (device) => {
                console.log('Appareil débloqué:', device);
                this.handleDeviceUnblocked(device);
            });

            // Progression du scan
            this.deviceHub.on('ScanProgress', (data) => {
                this.handleScanProgress(data);
            });

            // Scan terminé
            this.deviceHub.on('ScanComplete', (data) => {
                this.handleScanComplete(data);
            });

            this.deviceHub.onclose(() => {
                console.log('DeviceHub déconnecté');
            });

            this.deviceHub.onreconnecting(() => {
                console.log('DeviceHub reconnexion en cours...');
            });

            this.deviceHub.onreconnected(() => {
                console.log('DeviceHub reconnecté');
            });

            await this.deviceHub.start();
            console.log('Connecté au DeviceHub');
        } catch (error) {
            console.error('Erreur connexion DeviceHub:', error);
            setTimeout(() => this.connectDeviceHub(), 5000);
        }
    }

    // Handlers SignalR
    handleDeviceDiscovered(device) {
        // Ajouter à la liste si on est sur la page devices
        const existingIndex = this.currentDevices.findIndex(d => d.macAddress === device.macAddress);
        if (existingIndex === -1) {
            this.currentDevices.push(device);
        } else {
            this.currentDevices[existingIndex] = device;
        }
        
        if (this.currentPage === 'devices') {
            this.addOrUpdateDeviceRow(device);
        }
        
        this.showToast({
            title: 'Nouvel appareil',
            message: `${device.macAddress} (${device.ipAddress || 'IP inconnue'})`,
            severity: 0
        });
    }

    handleDeviceUpdated(device) {
        const index = this.currentDevices.findIndex(d => d.id === device.id || d.macAddress === device.macAddress);
        if (index !== -1) {
            this.currentDevices[index] = device;
        }
        
        if (this.currentPage === 'devices') {
            this.addOrUpdateDeviceRow(device);
        }
    }

    handleDeviceStatusChanged(device) {
        const index = this.currentDevices.findIndex(d => d.id === device.id || d.macAddress === device.macAddress);
        if (index !== -1) {
            this.currentDevices[index] = device;
        }
        
        if (this.currentPage === 'devices') {
            this.addOrUpdateDeviceRow(device);
        }
    }

    handleDeviceBlocked(device) {
        const index = this.currentDevices.findIndex(d => d.id === device.id);
        if (index !== -1) {
            this.currentDevices[index] = device;
        }
        
        if (this.currentPage === 'devices') {
            this.addOrUpdateDeviceRow(device);
        }
        
        this.showToast({
            title: 'Appareil bloqué',
            message: `${device.macAddress} a été bloqué`,
            severity: 2
        });
    }

    handleDeviceUnblocked(device) {
        const index = this.currentDevices.findIndex(d => d.id === device.id);
        if (index !== -1) {
            this.currentDevices[index] = device;
        }
        
        if (this.currentPage === 'devices') {
            this.addOrUpdateDeviceRow(device);
        }
        
        this.showToast({
            title: 'Appareil débloqué',
            message: `${device.macAddress} a été débloqué`,
            severity: 0
        });
    }

    handleScanProgress(data) {
        const scanStatus = document.getElementById('scan-status');
        if (scanStatus) {
            scanStatus.textContent = `Scan: ${data.scanned}/${data.total} (${data.found} trouvés)`;
        }
    }

    handleScanComplete(data) {
        const scanStatus = document.getElementById('scan-status');
        if (scanStatus) {
            scanStatus.textContent = `Scan terminé: ${data.totalDevices} appareils actifs`;
            setTimeout(() => { scanStatus.textContent = ''; }, 5000);
        }
    }

    // Ajouter ou mettre à jour une ligne dans le tableau
    addOrUpdateDeviceRow(device) {
        const tbody = document.getElementById('devices-table');
        if (!tbody) return;

        const existingRow = tbody.querySelector(`tr[data-mac="${device.macAddress}"]`);
        const rowHtml = this.createDeviceRowHtml(device);

        if (existingRow) {
            existingRow.outerHTML = rowHtml;
            // Animation de mise à jour
            const newRow = tbody.querySelector(`tr[data-mac="${device.macAddress}"]`);
            if (newRow) {
                newRow.classList.add('row-updated');
                setTimeout(() => newRow.classList.remove('row-updated'), 1000);
            }
        } else {
            tbody.insertAdjacentHTML('afterbegin', rowHtml);
            // Animation d'ajout
            const newRow = tbody.querySelector(`tr[data-mac="${device.macAddress}"]`);
            if (newRow) {
                newRow.classList.add('row-new');
                setTimeout(() => newRow.classList.remove('row-new'), 2000);
            }
        }
    }

    createDeviceRowHtml(device) {
        const isBlocked = device.status === 3 || device.status === 'Blocked';
        return `
            <tr data-mac="${this.escapeHtml(device.macAddress)}" data-id="${device.id}">
                <td><span class="status-badge ${this.getStatusClass(device.status)}">${this.getStatusText(device.status)}</span></td>
                <td class="device-mac">${this.escapeHtml(device.macAddress)}</td>
                <td>${this.escapeHtml(device.ipAddress || '-')}</td>
                <td>${this.escapeHtml(device.vendor || 'Inconnu')}</td>
                <td>${this.escapeHtml(device.description || device.hostname || '-')}</td>
                <td>${this.formatDate(device.lastSeen)}</td>
                <td>
                    <div class="action-buttons" style="display: flex; gap: 5px;">
                        ${!device.isTrusted ? `<button class="btn btn-sm btn-success" onclick="app.approveDevice(${device.id})" title="Approuver">${Icons.check}</button>` : ''}
                        <button class="btn btn-sm btn-primary" onclick="app.viewDevice(${device.id})" title="Détails">${Icons.eye}</button>
                        ${isBlocked 
                            ? `<button class="btn btn-sm btn-warning" onclick="app.unblockDevice(${device.id})" title="Débloquer">${Icons.unlockAlt}</button>`
                            : `<button class="btn btn-sm btn-danger" onclick="app.blockDevice(${device.id})" title="Bloquer">${Icons.ban}</button>`
                        }
                        <button class="btn btn-sm btn-secondary" onclick="app.deleteDevice(${device.id})" title="Supprimer">${Icons.trash}</button>
                    </div>
                </td>
            </tr>
        `;
    }

    // ==========================================
    // NETWORK SCAN METHODS
    // ==========================================

    async scanNetwork() {
        const btn = document.getElementById('scan-network-btn');
        const icon = document.getElementById('scan-icon');
        const scanStatus = document.getElementById('scan-status');
        
        if (!btn) return;
        
        // Désactiver le bouton et montrer l'animation
        btn.disabled = true;
        btn.classList.add('scanning');
        if (icon) {
            icon.className = 'fas fa-spinner fa-spin';
        }
        if (scanStatus) {
            scanStatus.textContent = 'Scan en cours...';
            scanStatus.style.color = 'var(--accent-primary)';
        }

        try {
            this.showToast({ title: 'Scan réseau', message: 'Démarrage du scan du réseau local...', severity: 0 });
            
            const result = await this.api('devices/scan', { method: 'POST' });
            
            this.showToast({ 
                title: 'Scan terminé', 
                message: result.message || `${result.devicesFound || 0} appareil(s) découvert(s)`, 
                severity: 0 
            });

            // Recharger la liste des appareils
            await this.loadDevices();
            
            if (scanStatus) {
                scanStatus.textContent = `Scan terminé: ${result.devicesFound || 0} appareil(s)`;
                scanStatus.style.color = 'var(--success)';
                setTimeout(() => { scanStatus.textContent = ''; }, 5000);
            }

        } catch (error) {
            console.error('Erreur scan réseau:', error);
            this.showToast({ title: 'Erreur', message: 'Impossible de scanner le réseau: ' + error.message, severity: 3 });
            
            if (scanStatus) {
                scanStatus.textContent = 'Erreur de scan';
                scanStatus.style.color = 'var(--danger)';
            }
        } finally {
            // Réactiver le bouton
            btn.disabled = false;
            btn.classList.remove('scanning');
            if (icon) {
                icon.className = 'fas fa-search';
            }
        }
    }

    // ==========================================
    // DEVICES LIST
    // ==========================================

    async loadDevices() {
        try {
            console.log('Chargement des appareils...');
            const devices = await this.api('devices');
            console.log('Appareils reçus:', devices);
            
            if (!Array.isArray(devices)) {
                console.error('Réponse API invalide:', devices);
                this.currentDevices = [];
            } else {
                this.currentDevices = devices;
            }
            
            this.renderDevicesTable(this.currentDevices);
        } catch (error) {
            console.error('Error loading devices:', error);
            this.showToast({ title: 'Erreur', message: 'Impossible de charger les appareils', severity: 2 });
        }
    }

    renderDevicesTable(devices) {
        const tbody = document.getElementById('devices-table');
        if (!tbody) {
            console.error('Element devices-table non trouvé');
            return;
        }

        console.log('Rendu du tableau avec', devices.length, 'appareils');

        if (!devices || !devices.length) {
            tbody.innerHTML = '<tr><td colspan="7" class="empty-state">Aucun appareil détecté</td></tr>';
            return;
        }

        tbody.innerHTML = devices.map(device => this.createDeviceRowHtml(device)).join('');
    }

    async blockDevice(id) {
        if (!confirm('Bloquer cet appareil ?')) return;

        try {
            await this.api(`devices/${id}/block`, { method: 'POST' });
            this.loadDevices();
            this.showToast({ title: 'Succès', message: 'Appareil bloqué', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message || 'Impossible de bloquer', severity: 2 });
        }
    }

    async unblockDevice(id) {
        try {
            await this.api(`devices/${id}/unblock`, { method: 'POST' });
            this.loadDevices();
            this.showToast({ title: 'Succès', message: 'Appareil débloqué', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de débloquer', severity: 2 });
        }
    }

    // ==========================================
    // API HELPER
    // ==========================================

    async api(endpoint, options = {}) {
        const url = `/api/${endpoint}`;
        const defaultOptions = {
            headers: {
                'Content-Type': 'application/json'
            }
        };

        const response = await fetch(url, { ...defaultOptions, ...options });
        
        if (!response.ok) {
            const errorData = await response.json().catch(() => ({}));
            throw new Error(errorData.message || `HTTP ${response.status}`);
        }

        const text = await response.text();
        return text ? JSON.parse(text) : {};
    }

    // ==========================================
    // NAVIGATION
    // ==========================================

    setupNavigation() {
        document.querySelectorAll('.nav-item').forEach(item => {
            item.addEventListener('click', () => {
                const page = item.dataset.page;
                this.navigateTo(page);
            });
        });
    }

    navigateTo(page) {
        // Update nav
        document.querySelectorAll('.nav-item').forEach(item => {
            item.classList.toggle('active', item.dataset.page === page);
        });

        // Update pages
        document.querySelectorAll('.page').forEach(p => {
            p.classList.toggle('active', p.id === `${page}-page`);
        });

        // Update title
        const titles = {
            dashboard: 'Dashboard',
            devices: 'Appareils',
            agents: 'Agents',
            cameras: 'Caméras',
            alerts: 'Alertes & Logs',
            traffic: 'Trafic',
            pihole: 'Pi-hole',
            dhcp: 'DHCP',
            setup: 'Installation',
            sniffer: 'Sniffer',
            router: 'Règles & Policies',
            settings: 'Paramètres',
            admin: 'Administration',
            parental: 'Contrôle Parental',
            reports: 'Rapports'
        };
        document.getElementById('page-title').textContent = titles[page] || page;

        this.currentPage = page;

        // Load page data
        switch (page) {
            case 'dashboard': this.loadDashboard(); break;
            case 'devices': this.loadDevices(); break;
            case 'agents': this.loadAgents(); break;
            case 'alerts': this.loadAlerts(); break;
            case 'pihole': this.loadPihole(); break;
        }
    }

    // ==========================================
    // EVENT LISTENERS
    // ==========================================

    setupEventListeners() {
        // Modal close buttons
        document.querySelectorAll('.modal-close').forEach(btn => {
            btn.addEventListener('click', () => {
                btn.closest('.modal').classList.remove('active');
            });
        });

        // Click outside modal to close
        document.querySelectorAll('.modal').forEach(modal => {
            modal.addEventListener('click', (e) => {
                if (e.target === modal) {
                    modal.classList.remove('active');
                }
            });
        });

        // Device filter buttons
        document.querySelectorAll('.filter-buttons .btn').forEach(btn => {
            btn.addEventListener('click', () => {
                document.querySelectorAll('.filter-buttons .btn').forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
                this.filterDevices(btn.dataset.filter);
            });
        });
    }

    filterDevices(filter) {
        let devices = this.currentDevices;
        
        switch (filter) {
            case 'online':
                devices = devices.filter(d => d.status === 1 || d.status === 'Online');
                break;
            case 'unknown':
                devices = devices.filter(d => !d.isKnown && !d.isTrusted);
                break;
            case 'blocked':
                devices = devices.filter(d => d.status === 3 || d.status === 'Blocked' || d.isBlocked);
                break;
        }
        
        this.renderDevicesTable(devices);
    }

    // ==========================================
    // SORTING
    // ==========================================

    setupSorting() {
        document.querySelectorAll('.sortable').forEach(th => {
            th.addEventListener('click', () => {
                const column = th.dataset.sort;
                this.sortDevices(column);
            });
        });
    }

    sortDevices(column) {
        const direction = this.sortDirection[column] === 'asc' ? 'desc' : 'asc';
        this.sortDirection[column] = direction;

        this.currentDevices.sort((a, b) => {
            let valA = a[column] || '';
            let valB = b[column] || '';

            if (typeof valA === 'string') valA = valA.toLowerCase();
            if (typeof valB === 'string') valB = valB.toLowerCase();

            if (valA < valB) return direction === 'asc' ? -1 : 1;
            if (valA > valB) return direction === 'asc' ? 1 : -1;
            return 0;
        });

        this.renderDevicesTable(this.currentDevices);
    }

    // ==========================================
    // NOTIFICATIONS (SSE)
    // ==========================================

    connectNotifications() {
        // Initialize AlertHub if available
        if (typeof AlertHub !== 'undefined') {
            this.alertHub = new AlertHub();
        }
    }

    // ==========================================
    // PI-HOLE
    // ==========================================

    async loadPihole() {
        try {
            const status = await this.api('pihole/status');
            
            document.getElementById('pihole-not-linux').style.display = 'none';
            document.getElementById('pihole-not-installed').style.display = 'none';
            document.getElementById('pihole-installed').style.display = 'none';

            if (!status.isLinux) {
                document.getElementById('pihole-not-linux').style.display = 'block';
                return;
            }

            if (!status.isInstalled) {
                document.getElementById('pihole-not-installed').style.display = 'block';
                return;
            }

            document.getElementById('pihole-installed').style.display = 'block';
            
            // Update stats
            document.getElementById('pihole-status-text').textContent = status.isRunning ? 'Actif' : 'Inactif';
            document.getElementById('pihole-blocking-text').textContent = status.blockingEnabled ? 'Activé' : 'Désactivé';
            document.getElementById('pihole-version').textContent = status.version || '-';

            if (status.stats) {
                document.getElementById('ph-queries').textContent = status.stats.dnsQueriesToday || 0;
                document.getElementById('ph-blocked').textContent = status.stats.adsBlockedToday || 0;
                document.getElementById('ph-percent').textContent = (status.stats.adsPercentageToday || 0).toFixed(1) + '%';
                document.getElementById('ph-domains').textContent = status.stats.domainsBeingBlocked || 0;
                document.getElementById('ph-clients').textContent = status.stats.uniqueClients || 0;
            }

        } catch (error) {
            console.error('Error loading Pi-hole status:', error);
        }
    }

    async installPihole() {
        if (!confirm('Installer Pi-hole ? Cette opération peut prendre plusieurs minutes.')) return;
        
        document.getElementById('pihole-install-modal').classList.add('active');
        document.getElementById('pihole-install-logs').textContent = 'Démarrage de l\'installation...\n';

        try {
            await this.api('pihole/install', { method: 'POST' });
            this.showToast({ title: 'Succès', message: 'Pi-hole installé avec succès', severity: 0 });
            this.loadPihole();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Échec de l\'installation', severity: 2 });
        }
    }

    closePiholeInstallModal() {
        document.getElementById('pihole-install-modal').classList.remove('active');
    }

    // ==========================================
    // PARENTAL CONTROL
    // ==========================================

    showCreateProfileModal() {
        document.getElementById('parental-profile-modal').classList.add('active');
        document.getElementById('parental-modal-title').innerHTML = '<i class="fas fa-child"></i> Nouveau Profil';
        document.getElementById('profile-id').value = '';
    }

    closeParentalModal() {
        document.getElementById('parental-profile-modal').classList.remove('active');
    }

    async saveProfile() {
        // Implement profile saving
        this.showToast({ title: 'Info', message: 'Fonctionnalité en cours de développement', severity: 0 });
    }
}

// Initialize application
const app = new FirewallApp();
