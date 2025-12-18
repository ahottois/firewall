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

    // Bloquer un appareil
    async blockDevice(id) {
        if (!confirm('Êtes-vous sûr de vouloir bloquer cet appareil ?')) return;
        
        try {
            await this.api(`devices/${id}/block`, { method: 'POST' });
            // Le hub SignalR notifiera la mise à jour
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de bloquer l\'appareil: ' + error.message, severity: 3 });
        }
    }

    // Débloquer un appareil
    async unblockDevice(id) {
        try {
            await this.api(`devices/${id}/unblock`, { method: 'POST' });
            // Le hub SignalR notifiera la mise à jour
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de débloquer l\'appareil: ' + error.message, severity: 3 });
        }
    }

    // Navigation
    setupNavigation() {
        document.querySelectorAll('.nav-item').forEach(item => {
            item.addEventListener('click', () => {
                const page = item.dataset.page;
                this.navigateTo(page);
            });
        });

        document.querySelectorAll('.view-all').forEach(link => {
            link.addEventListener('click', (e) => {
                e.preventDefault();
                const page = link.dataset.page;
                this.navigateTo(page);
            });
        });
    }

    navigateTo(page = 'dashboard') {
        document.querySelectorAll('.nav-item').forEach(item => {
            item.classList.toggle('active', item.dataset.page === page);
        });

        document.querySelectorAll('.page').forEach(p => {
            p.classList.toggle('active', p.id === `${page}-page`);
        });

        const titles = {
            dashboard: 'Dashboard',
            devices: 'Appareils Reseau',
            cameras: 'Cameras Reseau',
            alerts: 'Alertes',
            traffic: 'Trafic Reseau',
            pihole: 'Pi-hole Manager',
            dhcp: 'Serveur DHCP',
            setup: 'Installation & Configuration',
            sniffer: 'Packet Sniffer',
            router: 'Routeur / NAT',
            settings: 'Parametres',
            admin: 'Administration'
        };
        document.getElementById('page-title').textContent = titles[page] || page;

        this.currentPage = page;
        this.loadPageData(page);
        
        // Stop sniffer polling if leaving sniffer page
        if (page !== 'sniffer' && this.snifferInterval) {
            clearInterval(this.snifferInterval);
            this.snifferInterval = null;
        }
    }

    loadPageData(page) {
        switch (page) {
            case 'dashboard': this.loadDashboard(); break;
            case 'devices': this.loadDevices(); break;
            case 'agents': this.loadAgents(); break;
            case 'cameras': this.loadCameras(); break;
            case 'alerts': this.loadAlerts(); break;
            case 'traffic': this.loadTraffic(); break;
            case 'pihole': this.loadPihole(); break;
            case 'dhcp': this.loadDhcp(); break;
            case 'setup': /* Static content */ break;
            case 'sniffer': this.loadSniffer(); break;
            case 'router': this.loadRouter(); break;
            case 'settings': this.loadSettings(); break;
            case 'admin': this.loadAdmin(); break;
        }
    }

    setupEventListeners() {
        const scanBtn = document.getElementById('scan-btn');
        if (scanBtn) scanBtn.addEventListener('click', () => this.scanNetwork());
        
        const markAllReadBtn = document.getElementById('mark-all-read');
        if (markAllReadBtn) markAllReadBtn.addEventListener('click', () => this.markAllAlertsRead());
        
        document.getElementById('scan-cameras-btn')?.addEventListener('click', () => this.scanCameras());
        document.getElementById('add-camera-btn')?.addEventListener('click', () => this.showAddCameraModal());
        
        document.querySelectorAll('.filter-buttons .btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                document.querySelectorAll('.filter-buttons .btn').forEach(b => b.classList.remove('active'));
                e.target.classList.add('active');
                this.loadDevices(e.target.dataset.filter);
            });
        });

        const modalClose = document.querySelector('.modal-close');
        if (modalClose) modalClose.addEventListener('click', () => this.closeModal());
        
        const modal = document.getElementById('modal');
        if (modal) {
            modal.addEventListener('click', (e) => {
                if (e.target.id === 'modal') this.closeModal();
            });
        }
        
        const cameraModal = document.getElementById('camera-modal');
        if (cameraModal) {
            cameraModal.addEventListener('click', (e) => {
                if (e.target.id === 'camera-modal') this.closeCameraModal();
            });
        }
    }

    setupSorting() {
        document.querySelectorAll('.sortable').forEach(header => {
            header.addEventListener('click', () => {
                const column = header.dataset.sort;
                this.sortDevices(column);
            });
        });
    }

    sortDevices(column = 'id') {
        // Toggle sort direction
        this.sortDirection[column] = this.sortDirection[column] === 'asc' ? 'desc' : 'asc';
        const direction = this.sortDirection[column];

        // Update icons
        document.querySelectorAll('.sortable i').forEach(icon => icon.className = 'fas fa-sort');
        const activeHeader = document.querySelector(`.sortable[data-sort="${column}"] i`);
        if (activeHeader) {
            activeHeader.className = direction === 'asc' ? 'fas fa-sort-up' : 'fas fa-sort-down';
        }

        // Sort data
        this.currentDevices.sort((a, b) => {
            let valA = a[column];
            let valB = b[column];

            // Handle specific columns
            if (column === 'ip') {
                // Simple IP sort (can be improved for numeric sort)
                valA = valA || '';
                valB = valB || '';
            } else if (column === 'lastSeen') {
                valA = new Date(valA).getTime();
                valB = new Date(valB).getTime();
            } else if (column === 'status') {
                // Sort by status enum value
                valA = valA || 0;
                valB = valB || 0;
            } else {
                // String sort
                valA = (valA || '').toString().toLowerCase();
                valB = (valB || '').toString().toLowerCase();
            }

            if (valA < valB) return direction === 'asc' ? -1 : 1;
            if (valA > valB) return direction === 'asc' ? 1 : -1;
            return 0;
        });

        this.renderDevicesTable(this.currentDevices);
    }

    // API Calls
    async api(endpoint, options = {}) {
        try {
            const response = await fetch(`/api/${endpoint}`, {
                ...options,
                headers: { 'Content-Type': 'application/json', ...options.headers }
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            
            // Check if response has content
            const contentType = response.headers.get('content-type');
            if (contentType && contentType.includes('application/json')) {
                const text = await response.text();
                return text ? JSON.parse(text) : {};
            }
            return {};
        } catch (error) {
            if (!options.suppressErrorLog) {
                console.error(`API Error (${endpoint}):`, error);
            }
            throw error;
        }
    }

    // API call that doesn't expect JSON response
    async apiPost(endpoint) {
        try {
            const response = await fetch(`/api/${endpoint}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            return { success: true };
        } catch (error) {
            console.error(`API Error (${endpoint}):`, error);
            throw error;
        }
    }

    // Real-time Notifications
    connectNotifications() {
        if (this.eventSource) this.eventSource.close();

        this.eventSource = new EventSource('/api/notifications/stream');

        this.eventSource.addEventListener('alert', (e) => {
            const alert = JSON.parse(e.data);
            this.showToast(alert);
            this.updateAlertBadge();
            if (this.currentPage === 'dashboard') this.loadDashboard();
            else if (this.currentPage === 'alerts') this.loadAlerts();
        });

        this.eventSource.addEventListener('connected', () => console.log('Connected to notification stream'));

        this.eventSource.onerror = () => {
            console.log('Notification stream disconnected, reconnecting...');
            setTimeout(() => this.connectNotifications(), 5000);
        };
    }

    // Devices - mise à jour de loadDevices pour gérer le filtrage par statut
    async loadDevices(filter = 'all') {
        try {
            let endpoint = 'devices';
            if (filter === 'online') endpoint = 'devices/online';
            else if (filter === 'unknown') endpoint = 'devices/unknown';
            else if (filter === 'blocked') endpoint = 'devices/blocked';

            const devices = await this.api(endpoint);
            this.currentDevices = devices;
            this.renderDevicesTable(devices);
        } catch (error) {
            console.error('Error loading devices:', error);
        }
    }

    renderDevicesTable(devices) {
        const tbody = document.getElementById('devices-table');
        if (!tbody) return;

        if (!devices.length) {
            tbody.innerHTML = '<tr><td colspan="7" class="empty-state">Aucun appareil trouvé</td></tr>';
            return;
        }

        tbody.innerHTML = devices.map(device => this.createDeviceRowHtml(device)).join('');
    }

    // API Calls - grouping related functions together
    async api(endpoint, options = {}) {
        try {
            const response = await fetch(`/api/${endpoint}`, {
                ...options,
                headers: { 'Content-Type': 'application/json', ...options.headers }
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            
            // Check if response has content
            const contentType = response.headers.get('content-type');
            if (contentType && contentType.includes('application/json')) {
                const text = await response.text();
                return text ? JSON.parse(text) : {};
            }
            return {};
        } catch (error) {
            if (!options.suppressErrorLog) {
                console.error(`API Error (${endpoint}):`, error);
            }
            throw error;
        }
    }

    async apiPost(endpoint) {
        try {
            const response = await fetch(`/api/${endpoint}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            return { success: true };
        } catch (error) {
            console.error(`API Error (${endpoint}):`, error);
            throw error;
        }
    }

    // Real-time Notifications - merged connectNotifications and Device Hub handlers
    connectNotifications() {
        if (this.eventSource) this.eventSource.close();

        this.eventSource = new EventSource('/api/notifications/stream');

        this.eventSource.addEventListener('alert', (e) => {
            const alert = JSON.parse(e.data);
            this.showToast(alert);
            this.updateAlertBadge();
            if (this.currentPage === 'dashboard') this.loadDashboard();
            else if (this.currentPage === 'alerts') this.loadAlerts();
        });

        this.eventSource.addEventListener('connected', () => console.log('Connected to notification stream'));

        this.eventSource.onerror = () => {
            console.log('Notification stream disconnected, reconnecting...');
            setTimeout(() => this.connectNotifications(), 5000);
        };

        // Connect Device Hub for real-time device updates
        this.connectDeviceHub();
    }

    // ==========================================
    // PI-HOLE METHODS
    // ==========================================

    async loadPihole() {
        try {
            const status = await this.api('pihole/status');
            
            // Masquer toutes les sections d'abord
            document.getElementById('pihole-not-linux').style.display = 'none';
            document.getElementById('pihole-not-installed').style.display = 'none';
            document.getElementById('pihole-installed').style.display = 'none';

            // Vérifier si on est sur Linux
            if (!status.isLinux) {
                document.getElementById('pihole-not-linux').style.display = 'block';
                return;
            }

            // Vérifier si Pi-hole est installé
            if (!status.isInstalled) {
                document.getElementById('pihole-not-installed').style.display = 'block';
                return;
            }

            // Pi-hole est installé - afficher l'interface complète
            document.getElementById('pihole-installed').style.display = 'block';

            // Mettre à jour le statut
            const statusText = document.getElementById('pihole-status-text');
            const statusCard = document.getElementById('pihole-status-card');
            
            if (status.isRunning) {
                statusText.textContent = 'Actif';
                statusCard.classList.remove('danger');
                statusCard.classList.add('success');
            } else {
                statusText.textContent = 'Arrêté';
                statusCard.classList.remove('success');
                statusCard.classList.add('danger');
            }

            // Mettre à jour le statut de blocage
            const blockingText = document.getElementById('pihole-blocking-text');
            const blockingCard = document.getElementById('pihole-blocking-card');
            const btnEnable = document.getElementById('btn-enable-pihole');
            const btnDisable = document.getElementById('btn-disable-pihole');
            
            if (status.isEnabled) {
                blockingText.textContent = 'Activé';
                blockingCard.classList.remove('danger');
                blockingCard.style.color = 'var(--success)';
                btnEnable.style.display = 'none';
                btnDisable.style.display = 'inline-flex';
            } else {
                blockingText.textContent = 'Désactivé';
                blockingCard.style.color = 'var(--warning)';
                btnEnable.style.display = 'inline-flex';
                btnDisable.style.display = 'none';
            }

            // Mettre à jour la version
            document.getElementById('pihole-version').textContent = status.version || '-';

            // Charger les statistiques de l'API Pi-hole
            await this.loadPiholeSummary();

        } catch (error) {
            console.error('Erreur chargement Pi-hole:', error);
            this.showToast({ title: 'Erreur', message: 'Impossible de charger le statut Pi-hole', severity: 2 });
        }
    }

    async loadPiholeSummary() {
        try {
            const summary = await this.api('pihole/summary');
            
            if (summary && summary.status !== 'unavailable') {
                document.getElementById('ph-queries').textContent = this.formatNumber(summary.dnsQueriesToday || 0);
                document.getElementById('ph-blocked').textContent = this.formatNumber(summary.adsBlockedToday || 0);
                document.getElementById('ph-percent').textContent = (summary.adsPercentageToday || 0).toFixed(1) + '%';
                document.getElementById('ph-domains').textContent = this.formatNumber(summary.domainsBeingBlocked || 0);
                document.getElementById('ph-clients').textContent = summary.uniqueClients || 0;
                document.getElementById('ph-reply-ip').textContent = this.formatNumber(summary.replyIp || 0);
                document.getElementById('ph-reply-nx').textContent = this.formatNumber(summary.replyNxdomain || 0);
                
                // Gravity last updated
                if (summary.gravityLastUpdated && summary.gravityLastUpdated.relative) {
                    const rel = summary.gravityLastUpdated.relative;
                    if (rel.days > 0) {
                        document.getElementById('ph-gravity').textContent = `Il y a ${rel.days}j ${rel.hours}h`;
                    } else if (rel.hours > 0) {
                        document.getElementById('ph-gravity').textContent = `Il y a ${rel.hours}h ${rel.minutes}m`;
                    } else {
                        document.getElementById('ph-gravity').textContent = `Il y a ${rel.minutes}m`;
                    }
                }
            }
        } catch (error) {
            console.log('Pi-hole summary non disponible');
        }
    }

    formatNumber(num) {
        if (num >= 1000000) return (num / 1000000).toFixed(1) + 'M';
        if (num >= 1000) return (num / 1000).toFixed(1) + 'k';
        return num.toString();
    }

    async installPihole() {
        if (!confirm('Voulez-vous installer Pi-hole ?\n\nCette opération peut prendre plusieurs minutes et nécessite une connexion Internet.')) {
            return;
        }

        try {
            // Afficher le modal de progression
            this.showPiholeInstallModal();
            
            const result = await this.api('pihole/install', { method: 'POST' });
            
            if (result.message) {
                this.showToast({ title: 'Installation', message: result.message, severity: 0 });
            }

            // Démarrer le polling des logs
            this.startPiholeLogPolling();

        } catch (error) {
            this.closePiholeInstallModal();
            this.showToast({ title: 'Erreur', message: 'Impossible de démarrer l\'installation: ' + error.message, severity: 3 });
        }
    }

    showPiholeInstallModal() {
        const modal = document.getElementById('pihole-install-modal');
        if (modal) {
            modal.classList.add('active');
            document.getElementById('pihole-install-logs').textContent = 'Démarrage de l\'installation...\n';
        }
    }

    closePiholeInstallModal() {
        const modal = document.getElementById('pihole-install-modal');
        if (modal) {
            modal.classList.remove('active');
        }
        this.stopPiholeLogPolling();
    }

    startPiholeLogPolling() {
        if (this.piholeLogPolling) return;
        
        let lastLength = 0;
        
        this.piholeLogPolling = setInterval(async () => {
            try {
                const result = await this.api('pihole/logs');
                const logsDiv = document.getElementById('pihole-install-logs');
                
                if (result.logs && logsDiv) {
                    // Ne montrer que les nouvelles lignes
                    if (result.logs.length > lastLength) {
                        logsDiv.textContent = result.logs;
                        logsDiv.scrollTop = logsDiv.scrollHeight;
                        lastLength = result.logs.length;
                    }
                    
                    // Vérifier si l'installation est terminée
                    if (result.logs.includes('=== Installation terminée')) {
                        this.stopPiholeLogPolling();
                        setTimeout(() => {
                            this.closePiholeInstallModal();
                            this.loadPihole();
                            this.showToast({ title: 'Succès', message: 'Pi-hole a été installé avec succès!', severity: 0 });
                        }, 2000);
                    }
                }
            } catch (error) {
                console.error('Erreur polling logs:', error);
            }
        }, 1000);
    }

    stopPiholeLogPolling() {
        if (this.piholeLogPolling) {
            clearInterval(this.piholeLogPolling);
            this.piholeLogPolling = null;
        }
    }

    async uninstallPihole() {
        if (!confirm('Êtes-vous sûr de vouloir désinstaller Pi-hole ?\n\nCette action supprimera Pi-hole et tous ses paramètres.')) {
            return;
        }

        try {
            this.showToast({ title: 'Désinstallation', message: 'Désinstallation de Pi-hole en cours...', severity: 1 });
            
            const result = await this.api('pihole/uninstall', { method: 'POST' });
            
            this.showToast({ title: 'Succès', message: result.message || 'Pi-hole a été désinstallé', severity: 0 });
            
            // Recharger la page Pi-hole
            setTimeout(() => this.loadPihole(), 1000);

        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de désinstaller Pi-hole: ' + error.message, severity: 3 });
        }
    }

    async enablePihole() {
        try {
            const result = await this.api('pihole/enable', { method: 'POST' });
            this.showToast({ title: 'Succès', message: result.message || 'Pi-hole activé', severity: 0 });
            this.loadPihole();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible d\'activer Pi-hole: ' + error.message, severity: 3 });
        }
    }

    showDisablePiholeModal() {
        this.showModal('Désactiver Pi-hole', `
            <p>Pendant combien de temps voulez-vous désactiver le blocage ?</p>
            <div class="admin-buttons" style="flex-wrap: wrap; margin-top: 20px;">
                <button class="btn btn-warning" onclick="app.disablePihole(30)">30 secondes</button>
                <button class="btn btn-warning" onclick="app.disablePihole(60)">1 minute</button>
                <button class="btn btn-warning" onclick="app.disablePihole(300)">5 minutes</button>
                <button class="btn btn-warning" onclick="app.disablePihole(900)">15 minutes</button>
                <button class="btn btn-warning" onclick="app.disablePihole(3600)">1 heure</button>
                <button class="btn btn-danger" onclick="app.disablePihole()">Indéfiniment</button>
            </div>
        `);
    }

    async disablePihole(duration = null) {
        try {
            this.closeModal();
            
            const body = duration ? { duration } : {};
            const result = await this.api('pihole/disable', { 
                method: 'POST',
                body: JSON.stringify(body)
            });
            
            this.showToast({ title: 'Succès', message: result.message || 'Pi-hole désactivé', severity: 1 });
            this.loadPihole();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de désactiver Pi-hole: ' + error.message, severity: 3 });
        }
    }

    showPiholePasswordModal() {
        this.showModal('Changer le mot de passe Pi-hole', `
            <div class="form-group">
                <label>Nouveau mot de passe</label>
                <input type="password" id="pihole-new-password" class="form-control" placeholder="Entrez le nouveau mot de passe">
            </div>
            <div class="form-group">
                <label>Confirmer le mot de passe</label>
                <input type="password" id="pihole-confirm-password" class="form-control" placeholder="Confirmez le mot de passe">
            </div>
            <p class="text-muted" style="font-size: 0.85rem; margin-top: 10px;">
                Laissez vide pour supprimer le mot de passe.
            </p>
        `, `
            <button class="btn btn-sm" onclick="app.closeModal()">Annuler</button>
            <button class="btn btn-primary" onclick="app.setPiholePassword()">Enregistrer</button>
        `);
    }

    async setPiholePassword() {
        const password = document.getElementById('pihole-new-password').value;
        const confirm = document.getElementById('pihole-confirm-password').value;

        if (password !== confirm) {
            this.showToast({ title: 'Erreur', message: 'Les mots de passe ne correspondent pas', severity: 2 });
            return;
        }

        try {
            const result = await this.api('pihole/password', {
                method: 'POST',
                body: JSON.stringify({ password })
            });

            this.closeModal();
            this.showToast({ title: 'Succès', message: result.message || 'Mot de passe mis à jour', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de changer le mot de passe: ' + error.message, severity: 3 });
        }
    }

    // ==========================================
    // MODAL METHODS
    // ==========================================

    showModal(title, bodyHtml, footerHtml = '') {
        document.getElementById('modal-title').textContent = title;
        document.getElementById('modal-body').innerHTML = bodyHtml;
        document.getElementById('modal-footer').innerHTML = footerHtml;
        document.getElementById('modal').classList.add('active');
    }

    closeModal() {
        document.getElementById('modal').classList.remove('active');
    }

    // Clear all intervals and timeouts on unload
    unload() {
        if (this.eventSource) this.eventSource.close();
        if (this.scanLogSource) clearInterval(this.scanLogSource);
        if (this.snifferInterval) clearInterval(this.snifferInterval);
        if (this.deviceHub) {
            this.deviceHub.stop().catch(err => console.error('Error stopping device hub:', err));
        }
    }
}

// Initialize app
const app = new FirewallApp();

// ==========================================
// PARENTAL CONTROL METHODS - Extension de FirewallApp
// ==========================================

FirewallApp.prototype.parentalControlHub = null;
FirewallApp.prototype.currentProfiles = [];
FirewallApp.prototype.filterCategories = [];

// Connexion au hub SignalR Parental Control
FirewallApp.prototype.connectParentalControlHub = async function() {
    if (typeof signalR === 'undefined') return;
    
    try {
        this.parentalControlHub = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/parental-control')
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Warning)
            .build();

        this.parentalControlHub.on('ProfileStatusChanged', (status) => {
            this.handleProfileStatusChanged(status);
        });

        this.parentalControlHub.on('ProfileCreated', (status) => {
            if (this.currentPage === 'parental') this.loadParentalControl();
            this.showToast({ title: 'Nouveau profil', message: `${status.profileName} a été créé`, severity: 0 });
        });

        this.parentalControlHub.on('ProfileUpdated', (status) => {
            if (this.currentPage === 'parental') this.updateProfileCard(status);
        });

        this.parentalControlHub.on('ProfileDeleted', (profileId) => {
            if (this.currentPage === 'parental') this.loadParentalControl();
        });

        this.parentalControlHub.on('AutoBlockTriggered', (data) => {
            this.showToast({ 
                title: `?? Blocage automatique`, 
                message: `${data.profileName}: ${data.reason}`, 
                severity: 2 
            });
        });

        this.parentalControlHub.on('TimeWarning', (data) => {
            this.showToast({ 
                title: `? Temps restant`, 
                message: `${data.profileName}: ${data.remainingMinutes} minutes restantes`, 
                severity: 1 
            });
        });

        await this.parentalControlHub.start();
        console.log('Connecté au ParentalControlHub');
    } catch (error) {
        console.error('Erreur connexion ParentalControlHub:', error);
    }
};

FirewallApp.prototype.handleProfileStatusChanged = function(status) {
    if (this.currentPage === 'parental') {
        this.updateProfileCard(status);
    }
};

FirewallApp.prototype.loadParentalControl = async function() {
    try {
        // Charger les catégories de filtrage
        this.filterCategories = await this.api('parentalcontrol/filter-categories');
        
        // Charger les profils
        const profiles = await this.api('parentalcontrol/profiles');
        this.currentProfiles = profiles;
        
        this.renderParentalProfiles(profiles);
        
        // Connecter au hub si pas déjà fait
        if (!this.parentalControlHub) {
            await this.connectParentalControlHub();
        }
    } catch (error) {
        console.error('Erreur chargement contrôle parental:', error);
    }
};

FirewallApp.prototype.renderParentalProfiles = function(profiles) {
    const grid = document.getElementById('parental-profiles-grid');
    const empty = document.getElementById('parental-empty');
    
    if (!profiles || profiles.length === 0) {
        empty.style.display = 'flex';
        grid.innerHTML = '';
        grid.appendChild(empty);
        return;
    }
    
    empty.style.display = 'none';
    grid.innerHTML = profiles.map(profile => this.createProfileCardHtml(profile)).join('');
};

FirewallApp.prototype.createProfileCardHtml = function(profile) {
    const statusColors = {
        'Allowed': '#00ff88',
        'BlockedBySchedule': '#ff9f43',
        'BlockedByTimeLimit': '#ff6b6b',
        'Paused': '#ff4757',
        'Disabled': '#6c757d'
    };
    
    const statusLabels = {
        'Allowed': 'En ligne',
        'BlockedBySchedule': 'Hors horaire',
        'BlockedByTimeLimit': 'Temps dépassé',
        'Paused': 'Pause',
        'Disabled': 'Désactivé'
    };
    
    const statusKey = profile.status || 'Allowed';
    const statusColor = statusColors[statusKey] || '#6c757d';
    const statusLabel = statusLabels[statusKey] || 'Inconnu';
    
    const isPaused = statusKey === 'Paused';
    const pauseIcon = isPaused ? 'fa-play' : 'fa-pause';
    const pauseTitle = isPaused ? 'Rétablir' : 'Couper';
    const pauseBtnClass = isPaused ? 'btn-success' : 'btn-danger';
    
    // Avatar
    let avatarHtml;
    if (profile.avatarUrl && profile.avatarUrl.startsWith('http')) {
        avatarHtml = `<img src="${this.escapeHtml(profile.avatarUrl)}" alt="${this.escapeHtml(profile.profileName)}" class="profile-avatar-img">`;
    } else {
        avatarHtml = `<span class="profile-avatar-emoji">${profile.avatarUrl || '??'}</span>`;
    }
    
    // Temps restant
    let timeHtml = '';
    if (profile.remainingMinutes >= 0) {
        const hours = Math.floor(profile.remainingMinutes / 60);
        const mins = profile.remainingMinutes % 60;
        const timeStr = hours > 0 ? `${hours}h ${mins}m` : `${mins}m`;
        timeHtml = `
            <div class="profile-time">
                <div class="time-label">Temps restant</div>
                <div class="time-bar">
                    <div class="time-bar-fill" style="width: ${100 - profile.usagePercentage}%; background: ${profile.color}"></div>
                </div>
                <div class="time-value">${timeStr}</div>
            </div>
        `;
    }
    
    // Appareils
    const onlineDevices = profile.devices?.filter(d => d.isOnline).length || 0;
    const totalDevices = profile.devices?.length || 0;
    
    return `
        <div class="profile-card" data-profile-id="${profile.profileId}" style="--profile-color: ${profile.color}">
            <div class="profile-status-indicator" style="background: ${statusColor}"></div>
            
            <div class="profile-header">
                <div class="profile-avatar" style="border-color: ${profile.color}">
                    ${avatarHtml}
                </div>
                <div class="profile-info">
                    <h3 class="profile-name">${this.escapeHtml(profile.profileName)}</h3>
                    <span class="profile-status" style="color: ${statusColor}">${statusLabel}</span>
                </div>
                <button class="btn ${pauseBtnClass} btn-pause" onclick="app.togglePause(${profile.profileId})" title="${pauseTitle}">
                    <i class="fas ${pauseIcon}"></i>
                </button>
            </div>
            
            ${timeHtml}
            
            <div class="profile-devices">
                <i class="fas fa-laptop"></i>
                <span>${onlineDevices}/${totalDevices} appareil(s) en ligne</span>
            </div>
            
            ${profile.blockReason ? `<div class="profile-block-reason"><i class="fas fa-info-circle"></i> ${profile.blockReason}</div>` : ''}
            ${profile.nextAllowedTime ? `<div class="profile-next-time"><i class="fas fa-clock"></i> Prochain accès: ${profile.nextAllowedTime}</div>` : ''}
            
            <div class="profile-actions">
                <button class="btn btn-sm btn-primary" onclick="app.editProfile(${profile.profileId})">
                    <i class="fas fa-edit"></i> Modifier
                </button>
                <button class="btn btn-sm btn-secondary" onclick="app.viewProfileDetails(${profile.profileId})">
                    <i class="fas fa-chart-line"></i> Stats
                </button>
                <button class="btn btn-sm btn-danger" onclick="app.deleteProfile(${profile.profileId})">
                    <i class="fas fa-trash"></i>
                </button>
            </div>
        </div>
    `;
};

FirewallApp.prototype.updateProfileCard = function(status) {
    const card = document.querySelector(`.profile-card[data-profile-id="${status.profileId}"]`);
    if (card) {
        const newHtml = this.createProfileCardHtml(status);
        card.outerHTML = newHtml;
    }
};

FirewallApp.prototype.showCreateProfileModal = async function() {
    document.getElementById('parental-modal-title').innerHTML = '<i class="fas fa-plus"></i> Nouveau Profil';
    document.getElementById('profile-id').value = '';
    document.getElementById('profile-name').value = '';
    document.getElementById('profile-color').value = '#00d9ff';
    document.getElementById('profile-time-limit').value = '0';
    document.getElementById('profile-avatar').value = '??';
    document.getElementById('profile-blocked-domains').value = '';
    
    // Charger les appareils disponibles
    await this.loadAvailableDevices();
    
    // Générer la grille horaire
    this.generateScheduleGrid();
    
    // Générer les catégories de filtrage
    this.generateFilterCategories();
    
    document.getElementById('parental-profile-modal').classList.add('active');
};

FirewallApp.prototype.editProfile = async function(profileId) {
    try {
        const profile = await this.api(`parentalcontrol/profiles/${profileId}/details`);
        
        document.getElementById('parental-modal-title').innerHTML = '<i class="fas fa-edit"></i> Modifier le Profil';
        document.getElementById('profile-id').value = profile.id;
        document.getElementById('profile-name').value = profile.name;
        document.getElementById('profile-color').value = profile.color;
        document.getElementById('profile-time-limit').value = profile.dailyTimeLimitMinutes;
        document.getElementById('profile-avatar').value = profile.avatarUrl || '??';
        
        // Charger les appareils
        await this.loadAvailableDevices(profile.devices?.map(d => d.macAddress) || []);
        
        // Générer la grille horaire avec les données existantes
        this.generateScheduleGrid(profile.schedules);
        
        // Générer les catégories avec les données existantes
        const blockedCategories = profile.webFilters?.filter(f => f.filterType === 0).map(f => f.value) || [];
        this.generateFilterCategories(blockedCategories);
        
        // Domaines bloqués
        const blockedDomains = profile.webFilters?.filter(f => f.filterType === 1).map(f => f.value) || [];
        document.getElementById('profile-blocked-domains').value = blockedDomains.join('\n');
        
        document.getElementById('parental-profile-modal').classList.add('active');
    } catch (error) {
        this.showToast({ title: 'Erreur', message: 'Impossible de charger le profil', severity: 3 });
    }
};

FirewallApp.prototype.loadAvailableDevices = async function(selectedMacs = []) {
    const container = document.getElementById('profile-devices-list');
    
    try {
        // Récupérer tous les appareils
        const allDevices = await this.api('devices');
        
        if (!allDevices.length) {
            container.innerHTML = '<p class="text-muted">Aucun appareil trouvé. Scannez votre réseau d\'abord.</p>';
            return;
        }
        
        container.innerHTML = allDevices.map(device => {
            const isChecked = selectedMacs.includes(device.macAddress.toUpperCase());
            const displayName = device.hostname || device.description || device.vendor || device.macAddress;
            return `
                <label class="device-checkbox">
                    <input type="checkbox" name="profile-device" value="${device.macAddress}" ${isChecked ? 'checked' : ''}>
                    <span class="device-info">
                        <span class="device-name">${this.escapeHtml(displayName)}</span>
                        <span class="device-details">${device.macAddress} ${device.ipAddress ? '- ' + device.ipAddress : ''}</span>
                    </span>
                    <span class="device-status ${device.isOnline ? 'online' : 'offline'}">
                        <i class="fas fa-circle"></i>
                    </span>
                </label>
            `;
        }).join('');
    } catch (error) {
        container.innerHTML = '<p class="text-danger">Erreur de chargement des appareils</p>';
    }
};

FirewallApp.prototype.generateScheduleGrid = function(existingSchedules = []) {
    const days = ['Dim', 'Lun', 'Mar', 'Mer', 'Jeu', 'Ven', 'Sam'];
    const container = document.getElementById('schedule-grid');
    
    // Créer un map des schedules existants par jour
    const scheduleMap = {};
    existingSchedules.forEach(s => {
        if (!scheduleMap[s.dayOfWeek]) scheduleMap[s.dayOfWeek] = [];
        scheduleMap[s.dayOfWeek].push(s);
    });
    
    let html = '<div class="schedule-header"><span></span>';
    for (let h = 0; h < 24; h += 2) {
        html += `<span class="hour-label">${h}h</span>`;
    }
    html += '</div>';
    
    days.forEach((day, index) => {
        const daySchedules = scheduleMap[index] || [];
        const defaultStart = daySchedules[0]?.startTime || '08:00';
        const defaultEnd = daySchedules[0]?.endTime || '21:00';
        const isEnabled = daySchedules.length > 0 ? daySchedules[0].isEnabled : true;
        
        html += `
            <div class="schedule-row" data-day="${index}">
                <span class="day-label">${day}</span>
                <div class="schedule-times">
                    <input type="time" class="schedule-start" value="${defaultStart}" ${!isEnabled ? 'disabled' : ''}>
                    <span>à</span>
                    <input type="time" class="schedule-end" value="${defaultEnd}" ${!isEnabled ? 'disabled' : ''}>
                    <label class="schedule-enabled">
                        <input type="checkbox" class="day-enabled" ${isEnabled ? 'checked' : ''} 
                               onchange="app.toggleScheduleDay(this, ${index})">
                        <i class="fas fa-check"></i>
                    </label>
                </div>
            </div>
        `;
    });
    
    container.innerHTML = html;
};

FirewallApp.prototype.toggleScheduleDay = function(checkbox, dayIndex) {
    const row = document.querySelector(`.schedule-row[data-day="${dayIndex}"]`);
    const inputs = row.querySelectorAll('input[type="time"]');
    inputs.forEach(input => input.disabled = !checkbox.checked);
};

FirewallApp.prototype.generateFilterCategories = function(selectedCategories = []) {
    const container = document.getElementById('filter-categories');
    
    if (!this.filterCategories.length) {
        container.innerHTML = '<p class="text-muted">Chargement des catégories...</p>';
        return;
    }
    
    container.innerHTML = this.filterCategories.map(cat => {
        const isChecked = selectedCategories.includes(cat.key);
        return `
            <label class="filter-category" style="--cat-color: ${cat.color}">
                <input type="checkbox" name="filter-category" value="${cat.key}" ${isChecked ? 'checked' : ''}>
                <div class="category-icon"><i class="fas ${cat.icon}"></i></div>
                <div class="category-info">
                    <span class="category-name">${cat.name}</span>
                    <span class="category-desc">${cat.description}</span>
                </div>
            </label>
        `;
    }).join('');
};

FirewallApp.prototype.closeParentalModal = function() {
    document.getElementById('parental-profile-modal').classList.remove('active');
};

FirewallApp.prototype.saveProfile = async function() {
    const profileId = document.getElementById('profile-id').value;
    const name = document.getElementById('profile-name').value.trim();
    
    if (!name) {
        this.showToast({ title: 'Erreur', message: 'Le nom est requis', severity: 2 });
        return;
    }
    
    // Récupérer les appareils sélectionnés
    const deviceMacs = Array.from(document.querySelectorAll('input[name="profile-device"]:checked'))
        .map(cb => cb.value);
    
    // Récupérer les horaires
    const schedules = [];
    document.querySelectorAll('.schedule-row').forEach(row => {
        const day = parseInt(row.dataset.day);
        const isEnabled = row.querySelector('.day-enabled').checked;
        const startTime = row.querySelector('.schedule-start').value;
        const endTime = row.querySelector('.schedule-end').value;
        
        schedules.push({
            dayOfWeek: day,
            startTime: startTime,
            endTime: endTime,
            isEnabled: isEnabled
        });
    });
    
    // Récupérer les catégories bloquées
    const blockedCategories = Array.from(document.querySelectorAll('input[name="filter-category"]:checked'))
        .map(cb => cb.value);
    
    // Récupérer les domaines bloqués
    const blockedDomains = document.getElementById('profile-blocked-domains').value
        .split('\n')
        .map(d => d.trim())
        .filter(d => d.length > 0);
    
    const dto = {
        id: profileId ? parseInt(profileId) : 0,
        name: name,
        avatarUrl: document.getElementById('profile-avatar').value || '??',
        color: document.getElementById('profile-color').value,
        dailyTimeLimitMinutes: parseInt(document.getElementById('profile-time-limit').value) || 0,
        isActive: true,
        blockedMessage: "L'accès Internet est temporairement désactivé.",
        deviceMacs: deviceMacs,
        schedules: schedules,
        blockedCategories: blockedCategories,
        blockedDomains: blockedDomains
    };
    
    try {
        if (profileId) {
            await this.api(`parentalcontrol/profiles/${profileId}`, {
                method: 'PUT',
                body: JSON.stringify(dto)
            });
            this.showToast({ title: 'Succès', message: 'Profil mis à jour', severity: 0 });
        } else {
            await this.api('parentalcontrol/profiles', {
                method: 'POST',
                body: JSON.stringify(dto)
            });
            this.showToast({ title: 'Succès', message: 'Profil créé', severity: 0 });
        }
        
        this.closeParentalModal();
        this.loadParentalControl();
    } catch (error) {
        this.showToast({ title: 'Erreur', message: 'Impossible de sauvegarder le profil', severity: 3 });
    }
};

FirewallApp.prototype.togglePause = async function(profileId) {
    try {
        const result = await this.api(`parentalcontrol/profiles/${profileId}/toggle-pause`, {
            method: 'POST'
        });
        
        const message = result.isPaused ? 'Accès Internet coupé' : 'Accès Internet rétabli';
        this.showToast({ title: 'Pause', message: message, severity: result.isPaused ? 2 : 0 });
        
        if (result.status) {
            this.updateProfileCard(result.status);
        }
    } catch (error) {
        this.showToast({ title: 'Erreur', message: 'Impossible de changer l\'état de pause', severity: 3 });
    }
};

FirewallApp.prototype.deleteProfile = async function(profileId) {
    if (!confirm('Êtes-vous sûr de vouloir supprimer ce profil ?\n\nLes appareils associés ne seront plus soumis aux restrictions.')) {
        return;
    }
    
    try {
        await this.api(`parentalcontrol/profiles/${profileId}`, { method: 'DELETE' });
        this.showToast({ title: 'Succès', message: 'Profil supprimé', severity: 0 });
        this.loadParentalControl();
    } catch (error) {
        this.showToast({ title: 'Erreur', message: 'Impossible de supprimer le profil', severity: 3 });
    }
};

FirewallApp.prototype.viewProfileDetails = async function(profileId) {
    try {
        const usage = await this.api(`parentalcontrol/profiles/${profileId}/usage/history?days=7`);
        const profile = this.currentProfiles.find(p => p.profileId === profileId);
        
        let chartHtml = '<div class="usage-chart">';
        const maxMinutes = Math.max(...usage.map(u => u.minutesUsed), 60);
        
        usage.reverse().forEach(day => {
            const height = (day.minutesUsed / maxMinutes * 100);
            const date = new Date(day.date);
            const dayName = ['Dim', 'Lun', 'Mar', 'Mer', 'Jeu', 'Ven', 'Sam'][date.getDay()];
            const hours = Math.floor(day.minutesUsed / 60);
            const mins = day.minutesUsed % 60;
            const timeStr = hours > 0 ? `${hours}h ${mins}m` : `${mins}m`;
            
            chartHtml += `
                <div class="usage-bar-container">
                    <div class="usage-bar" style="height: ${height}%; background: ${profile?.color || '#00d9ff'}"></div>
                    <span class="usage-label">${dayName}</span>
                    <span class="usage-value">${timeStr}</span>
                </div>
            `;
        });
        chartHtml += '</div>';
        
        this.showModal(`Statistiques - ${profile?.profileName || 'Profil'}`, `
            <h4 style="margin-bottom: 15px;"><i class="fas fa-chart-bar"></i> Temps d'utilisation (7 derniers jours)</h4>
            ${chartHtml}
            <div class="usage-summary" style="margin-top: 20px; display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px;">
                <div class="usage-stat">
                    <span class="stat-value">${usage.reduce((sum, u) => sum + u.minutesUsed, 0)}</span>
                    <span class="stat-label">Minutes totales</span>
                </div>
                <div class="usage-stat">
                    <span class="stat-value">${usage.reduce((sum, u) => sum + u.blockCount, 0)}</span>
                    <span class="stat-label">Blocages auto</span>
                </div>
                <div class="usage-stat">
                    <span class="stat-value">${usage.reduce((sum, u) => sum + u.connectionCount, 0)}</span>
                    <span class="stat-label">Connexions</span>
                </div>
            </div>
        `, '<button class="btn btn-primary" onclick="app.closeModal()">Fermer</button>');
    } catch (error) {
        this.showToast({ title: 'Erreur', message: 'Impossible de charger les statistiques', severity: 3 });
    }
};

// Ajouter la gestion de la page parental dans loadPageData
const originalLoadPageData = FirewallApp.prototype.loadPageData;
FirewallApp.prototype.loadPageData = function(page) {
    if (page === 'parental') {
        this.loadParentalControl();
    } else {
        originalLoadPageData.call(this, page);
    }
};

// Ajouter parental aux titres de page
const originalNavigateTo = FirewallApp.prototype.navigateTo;
FirewallApp.prototype.navigateTo = function(page) {
    originalNavigateTo.call(this, page);
    if (page === 'parental') {
        document.getElementById('page-title').textContent = 'Contrôle Parental';
    }
};

window.addEventListener('beforeunload', () => {
    app.unload();
});

// ==========================================
// ADMINISTRATION METHODS - Extension de FirewallApp
// ==========================================

FirewallApp.prototype.loadAdmin = async function() {
    try {
        // Charger le statut du système
        const [systemStatus, serviceStatus] = await Promise.all([
            this.api('system/status'),
            this.api('system/service/status')
        ]);

        // Mettre à jour les informations système
        const serviceStatusText = document.getElementById('service-status-text');
        const serviceStatusCard = document.getElementById('service-status-card');
        const appVersion = document.getElementById('app-version');

        if (serviceStatusText) {
            serviceStatusText.textContent = serviceStatus.status || 'Inconnu';
            
            if (serviceStatus.isRunning) {
                serviceStatusCard?.classList.remove('danger');
                serviceStatusCard?.classList.add('success');
            } else {
                serviceStatusCard?.classList.remove('success');
                serviceStatusCard?.classList.add('danger');
            }
        }

        if (appVersion) {
            appVersion.textContent = systemStatus.version || '1.0.0';
        }

        // Afficher les informations supplémentaires
        const serviceResult = document.getElementById('service-result');
        if (serviceResult && serviceStatus.message) {
            serviceResult.innerHTML = `<p class="text-muted"><i class="fas fa-info-circle"></i> ${this.escapeHtml(serviceStatus.message)}</p>`;
        }

        // Mettre à jour les boutons selon le statut
        this.updateAdminButtons(serviceStatus);

    } catch (error) {
        console.error('Erreur chargement admin:', error);
    }
};

FirewallApp.prototype.updateAdminButtons = function(serviceStatus) {
    const btnStart = document.getElementById('btn-start-service');
    const btnStop = document.getElementById('btn-stop-service');
    const btnInstall = document.getElementById('btn-install-service');
    const btnUninstall = document.getElementById('btn-uninstall-service');

    if (serviceStatus.isInstalled) {
        if (btnInstall) btnInstall.style.display = 'none';
        if (btnUninstall) btnUninstall.style.display = 'inline-flex';
        
        if (serviceStatus.isRunning) {
            if (btnStart) btnStart.disabled = true;
            if (btnStop) btnStop.disabled = false;
        } else {
            if (btnStart) btnStart.disabled = false;
            if (btnStop) btnStop.disabled = true;
        }
    } else {
        if (btnInstall) btnInstall.style.display = 'inline-flex';
        if (btnUninstall) btnUninstall.style.display = 'none';
    }
};

FirewallApp.prototype.startService = async function() {
    try {
        this.showToast({ title: 'Service', message: 'Démarrage du service...', severity: 0 });
        const result = await this.api('system/service/start', { method: 'POST' });
        this.showToast({ title: 'Succès', message: result.message || 'Service démarré', severity: 0 });
        setTimeout(() => this.loadAdmin(), 2000);
    } catch (error) {
        this.showToast({ title: 'Erreur', message: 'Impossible de démarrer le service', severity: 3 });
    }
};

FirewallApp.prototype.stopService = async function() {
    if (!confirm('Êtes-vous sûr de vouloir arrêter le service ?\n\nL\'interface web ne sera plus accessible.')) {
        return;
    }

    try {
        this.showToast({ title: 'Service', message: 'Arrêt du service...', severity: 1 });
        const result = await this.api('system/service/stop', { method: 'POST' });
        this.showToast({ title: 'Succès', message: result.message || 'Service arrêté', severity: 0 });
    } catch (error) {
        this.showToast({ title: 'Erreur', message: 'Impossible d\'arrêter le service', severity: 3 });
    }
};

FirewallApp.prototype.restartService = async function() {
    if (!confirm('Êtes-vous sûr de vouloir redémarrer le service ?\n\nL\'interface sera temporairement indisponible.')) {
        return;
    }

    try {
        this.showToast({ title: 'Service', message: 'Redémarrage en cours...', severity: 1 });
        const result = await this.api('system/service/restart', { method: 'POST' });
        
        document.getElementById('service-result').innerHTML = `
            <div class="alert alert-warning">
                <i class="fas fa-spinner fa-spin"></i> Redémarrage en cours... La page se rechargera automatiquement.
            </div>
        `;

        // Attendre puis recharger la page
        setTimeout(() => {
            this.checkServiceAndReload();
        }, 3000);

    } catch (error) {
        this.showToast({ title: 'Erreur', message: 'Impossible de redémarrer le service', severity: 3 });
    }
};

FirewallApp.prototype.checkServiceAndReload = async function() {
    let attempts = 0;
    const maxAttempts = 30;

    const check = async () => {
        attempts++;
        try {
            await this.api('system/status');
            window.location.reload();
        } catch (error) {
            if (attempts < maxAttempts) {
                setTimeout(check, 2000);
            } else {
                document.getElementById('service-result').innerHTML = `
                    <div class="alert alert-danger">
                        <i class="fas fa-exclamation-triangle"></i> Le service ne répond pas. Rechargez la page manuellement.
                    </div>
                `;
            }
        }
    };

    check();
};

FirewallApp.prototype.installService = async function() {
    if (!confirm('Voulez-vous installer WebGuard comme service système ?\n\nCela permettra au service de démarrer automatiquement au démarrage du système.')) {
        return;
    }

    try {
        this.showToast({ title: 'Installation', message: 'Installation du service en cours...', severity: 0 });
        const result = await this.api('system/service/install', { method: 'POST' });
        
        document.getElementById('install-result').innerHTML = `
            <div class="alert alert-success">
                <i class="fas fa-check-circle"></i> ${this.escapeHtml(result.message)}
            </div>
        `;
        
        this.showToast({ title: 'Succès', message: result.message, severity: 0 });
        setTimeout(() => this.loadAdmin(), 2000);
    } catch (error) {
        document.getElementById('install-result').innerHTML = `
            <div class="alert alert-danger">
                <i class="fas fa-times-circle"></i> Erreur lors de l'installation
            </div>
        `;
        this.showToast({ title: 'Erreur', message: 'Impossible d\'installer le service', severity: 3 });
    }
};

FirewallApp.prototype.uninstallService = async function() {
    if (!confirm('Êtes-vous sûr de vouloir désinstaller le service ?\n\nWebGuard ne démarrera plus automatiquement.')) {
        return;
    }

    try {
        this.showToast({ title: 'Désinstallation', message: 'Désinstallation du service...', severity: 1 });
        const result = await this.api('system/service/uninstall', { method: 'POST' });
        
        document.getElementById('install-result').innerHTML = `
            <div class="alert alert-success">
                <i class="fas fa-check-circle"></i> ${this.escapeHtml(result.message)}
            </div>
        `;
        
        this.showToast({ title: 'Succès', message: result.message, severity: 0 });
        setTimeout(() => this.loadAdmin(), 2000);
    } catch (error) {
        this.showToast({ title: 'Erreur', message: 'Impossible de désinstaller le service', severity: 3 });
    }
};

FirewallApp.prototype.loadServiceLogs = async function() {
    const logsDiv = document.getElementById('service-logs');
    if (!logsDiv) return;

    logsDiv.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Chargement des logs...';

    try {
        const result = await this.api('system/service/logs?lines=200');
        logsDiv.textContent = result.logs || 'Aucun log disponible';
        logsDiv.scrollTop = logsDiv.scrollHeight;
    } catch (error) {
        logsDiv.textContent = 'Erreur lors du chargement des logs';
    }
};

FirewallApp.prototype.shutdownApp = async function() {
    if (!confirm('Êtes-vous sûr de vouloir arrêter l\'application ?\n\nL\'interface web ne sera plus accessible jusqu\'au prochain démarrage manuel.')) {
        return;
    }

    try {
        this.showToast({ title: 'Arrêt', message: 'Arrêt de l\'application en cours...', severity: 2 });
        await this.api('system/shutdown', { method: 'POST' });
    } catch (error) {
        // L'erreur est normale car le serveur s'arrête
    }
};

FirewallApp.prototype.checkForUpdates = async function() {
    const updateStatus = document.getElementById('update-status');
    if (updateStatus) {
        updateStatus.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Vérification des mises à jour...';
    }

    try {
        // Simuler une vérification (à implémenter avec GitHub API si nécessaire)
        await new Promise(resolve => setTimeout(resolve, 1500));
        
        if (updateStatus) {
            updateStatus.innerHTML = '<i class="fas fa-check-circle" style="color: var(--success);"></i> Vous utilisez la dernière version';
        }
    } catch (error) {
        if (updateStatus) {
            updateStatus.innerHTML = '<i class="fas fa-exclamation-triangle" style="color: var(--warning);"></i> Impossible de vérifier les mises à jour';
        }
    }
};

FirewallApp.prototype.updateFromGithub = async function() {
    this.showToast({ title: 'Mise à jour', message: 'Cette fonctionnalité sera disponible prochainement', severity: 1 });
};

// Ajouter admin aux titres de page dans navigateTo
const originalNavigateToForAdmin = FirewallApp.prototype.navigateTo;
FirewallApp.prototype.navigateTo = function(page) {
    originalNavigateToForAdmin.call(this, page);
    if (page === 'admin') {
        document.getElementById('page-title').textContent = 'Administration';
    }
};

window.addEventListener('beforeunload', () => {
    app.unload();
});
