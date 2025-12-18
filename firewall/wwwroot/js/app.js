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

    scanLogs = [];
    
    addScanLog(message, type = 'info') {
        const timestamp = new Date().toLocaleTimeString('fr-FR');
        const colors = {
            'info': '#00d9ff',
            'success': '#00ff88',
            'warning': '#ffaa00',
            'error': '#ff4757'
        };
        const color = colors[type] || colors.info;
        const logLine = `<span style="color: ${color}">[${timestamp}] ${message}</span>`;
        this.scanLogs.push(logLine);
        
        const logsPanel = document.getElementById('scan-logs-panel');
        const logsContainer = document.getElementById('device-scan-logs');
        
        if (logsPanel && logsContainer) {
            logsPanel.style.display = 'block';
            logsContainer.innerHTML = this.scanLogs.join('\n');
            logsContainer.scrollTop = logsContainer.scrollHeight;
        }
    }
    
    clearScanLogs() {
        this.scanLogs = [];
        const logsContainer = document.getElementById('device-scan-logs');
        if (logsContainer) {
            logsContainer.innerHTML = '';
        }
    }

    async cleanupDevices() {
        if (!confirm('Supprimer tous les appareils fantomes (Docker, MAC aleatoires sur reseaux virtuels) ?')) {
            return;
        }

        try {
            this.showToast({ title: 'Nettoyage', message: 'Suppression des appareils fantomes...', severity: 0 });
            
            const result = await this.api('devices/cleanup', { method: 'POST' });
            
            this.showToast({ 
                title: 'Nettoyage termine', 
                message: result.message || `${result.deletedCount} appareils supprimes`, 
                severity: 0 
            });

            // Recharger la liste
            await this.loadDevices();
        } catch (error) {
            console.error('Erreur cleanup:', error);
            this.showToast({ title: 'Erreur', message: 'Erreur lors du nettoyage: ' + error.message, severity: 2 });
        }
    }

    async scanNetwork() {
        const btn = document.getElementById('scan-network-btn');
        const icon = document.getElementById('scan-icon');
        const scanStatus = document.getElementById('scan-status');
        
        if (!btn) return;
        
        // Afficher le panneau de logs
        this.clearScanLogs();
        this.addScanLog('Demarrage du scan reseau...', 'info');
        
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
            this.addScanLog('Appel API /api/devices/scan...', 'info');
            
            const result = await this.api('devices/scan', { method: 'POST' });
            
            this.addScanLog(`Reponse API: ${JSON.stringify(result)}`, 'success');
            this.addScanLog(`Scan termine: ${result.devicesFound || 0} appareil(s) decouvert(s)`, 'success');
            
            this.showToast({ 
                title: 'Scan termine', 
                message: result.message || `${result.devicesFound || 0} appareil(s) decouvert(s)`, 
                severity: 0 
            });

            // Recharger la liste des appareils
            this.addScanLog('Rechargement de la liste des appareils...', 'info');
            await this.loadDevices();
            this.addScanLog(`Liste rechargee: ${this.currentDevices.length} appareil(s) en base`, 'success');
            
            if (scanStatus) {
                scanStatus.textContent = `Scan termine: ${result.devicesFound || 0} appareil(s)`;
                scanStatus.style.color = 'var(--success)';
                setTimeout(() => { scanStatus.textContent = ''; }, 5000);
            }

        } catch (error) {
            console.error('Erreur scan reseau:', error);
            this.addScanLog(`ERREUR: ${error.message}`, 'error');
            this.showToast({ title: 'Erreur', message: 'Impossible de scanner le reseau: ' + error.message, severity: 3 });
            
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
            this.addScanLog('Appel API /api/devices...', 'info');
            
            const devices = await this.api('devices');
            
            console.log('Appareils recus:', devices);
            this.addScanLog(`API devices: ${devices.length} appareil(s) recu(s)`, devices.length > 0 ? 'success' : 'warning');
            
            if (!Array.isArray(devices)) {
                console.error('Reponse API invalide:', devices);
                this.addScanLog('ERREUR: Reponse API invalide', 'error');
                this.currentDevices = [];
            } else {
                this.currentDevices = devices;
            }
            
            this.renderDevicesTable(this.currentDevices);
        } catch (error) {
            console.error('Error loading devices:', error);
            this.addScanLog(`ERREUR chargement: ${error.message}`, 'error');
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

        // Update title - Titres uniformisés en français
        const titles = {
            dashboard: 'Tableau de bord',
            devices: 'Appareils',
            agents: 'Agents',
            cameras: 'Cameras',
            alerts: 'Journaux',
            traffic: 'Trafic reseau',
            pihole: 'Pi-hole',
            dhcp: 'Serveur DHCP',
            setup: 'Installation',
            sniffer: 'Analyse reseau',
            router: 'Regles de securite',
            settings: 'Parametres',
            admin: 'Administration',
            parental: 'Controle parental',
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
            case 'parental': this.loadParental(); break;
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
            document.getElementById('pihole-version').textContent = status.version || '-' ;

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

    parentalProfiles = [];
    parentalHub = null;
    filterCategories = [];

    async loadParental() {
        try {
            // Charger les profils et les catégories de filtrage
            const [profiles, categories] = await Promise.all([
                this.api('parentalcontrol/profiles'),
                this.api('parentalcontrol/filter-categories')
            ]);

            this.parentalProfiles = profiles;
            this.filterCategories = categories;

            this.renderParentalProfiles(profiles);
            this.connectParentalHub();
        } catch (error) {
            console.error('Erreur chargement contrôle parental:', error);
            this.showToast({ title: 'Erreur', message: 'Impossible de charger les profils', severity: 2 });
        }
    }

    renderParentalProfiles(profiles) {
        const grid = document.getElementById('parental-profiles-grid');
        const empty = document.getElementById('parental-empty');

        if (!grid) return;

        if (!profiles || !profiles.length) {
            grid.innerHTML = '';
            if (empty) empty.style.display = 'block';
            return;
        }

        if (empty) empty.style.display = 'none';
        grid.innerHTML = profiles.map(profile => this.createProfileCard(profile)).join('');
    }

    createProfileCard(profile) {
        const statusColors = {
            0: 'var(--success)',      // Allowed
            1: 'var(--warning)',      // BlockedBySchedule
            2: 'var(--danger)',       // BlockedByTimeLimit
            3: 'var(--accent-secondary)', // Paused
            4: 'var(--text-muted)'    // Disabled
        };

        const statusTexts = {
            0: 'Accès autorisé',
            1: 'Hors plage horaire',
            2: 'Temps dépassé',
            3: 'En pause',
            4: 'Désactivé'
        };

        const statusColor = statusColors[profile.status] || statusColors[0];
        const statusText = statusTexts[profile.status] || 'Inconnu';
        const isBlocked = profile.status !== 0;
        const profileColor = profile.color || '#00d9ff';

        // Avatar
        let avatarContent = '';
        if (profile.avatarUrl && profile.avatarUrl.startsWith('http')) {
            avatarContent = `<img src="${this.escapeHtml(profile.avatarUrl)}" class="profile-avatar-img" alt="${this.escapeHtml(profile.profileName)}">`;
        } else {
            avatarContent = `<span class="profile-avatar-emoji">${this.escapeHtml(profile.avatarUrl || '👤')}</span>`;
        }

        // Barre de temps
        let timeBar = '';
        if (profile.remainingMinutes >= 0) {
            const percentage = profile.usagePercentage || 0;
            const barColor = percentage > 90 ? 'var(--danger)' : percentage > 70 ? 'var(--warning)' : profileColor;
            const remaining = profile.remainingMinutes;
            const hours = Math.floor(remaining / 60);
            const mins = remaining % 60;
            const timeText = hours > 0 ? `${hours}h ${mins}min restantes` : `${mins} min restantes`;
            
            timeBar = `
                <div class="profile-time">
                    <div class="time-label">Temps d'écran aujourd'hui</div>
                    <div class="time-bar">
                        <div class="time-bar-fill" style="width: ${percentage}%; background: ${barColor};"></div>
                    </div>
                    <div class="time-value">${timeText}</div>
                </div>
            `;
        }

        // Appareils
        const onlineDevices = profile.devices?.filter(d => d.isOnline).length || 0;
        const totalDevices = profile.devices?.length || 0;

        // Bouton pause/play
        const pauseBtn = profile.status === 3 
            ? `<button class="btn btn-success btn-pause" onclick="app.unpauseProfile(${profile.profileId})" title="Rétablir"><i class="fas fa-play"></i></button>`
            : `<button class="btn btn-warning btn-pause" onclick="app.pauseProfile(${profile.profileId})" title="Pause"><i class="fas fa-pause"></i></button>`;

        return `
            <div class="profile-card" style="--profile-color: ${profileColor};">
                <div class="profile-status-indicator" style="background: ${statusColor};"></div>
                
                <div class="profile-header">
                    <div class="profile-avatar" style="border-color: ${profileColor};">
                        ${avatarContent}
                    </div>
                    <div class="profile-info">
                        <div class="profile-name">${this.escapeHtml(profile.profileName)}</div>
                        <div class="profile-status" style="color: ${statusColor};">
                            <i class="fas fa-circle" style="font-size: 0.6rem;"></i> ${statusText}
                        </div>
                    </div>
                    ${pauseBtn}
                </div>

                ${timeBar}

                <div class="profile-devices">
                    <i class="fas fa-laptop"></i>
                    <span>${onlineDevices}/${totalDevices} appareil(s) en ligne</span>
                </div>

                ${profile.blockReason ? `
                    <div class="profile-block-reason">
                        <i class="fas fa-ban"></i> ${this.escapeHtml(profile.blockReason)}
                    </div>
                ` : ''}

                ${profile.nextAllowedTime ? `
                    <div class="profile-next-time">
                        <i class="fas fa-clock"></i> Prochain accès: ${this.escapeHtml(profile.nextAllowedTime)}
                    </div>
                ` : ''}

                ${profile.currentSlotEnds ? `
                    <div class="profile-next-time" style="background: rgba(63, 185, 80, 0.1); color: var(--success);">
                        <i class="fas fa-hourglass-half"></i> Fin du créneau: ${this.escapeHtml(profile.currentSlotEnds)}
                    </div>
                ` : ''}

                <div class="profile-actions">
                    <button class="btn btn-sm btn-primary" onclick="app.editProfile(${profile.profileId})">
                        <i class="fas fa-edit"></i> Modifier
                    </button>
                    <button class="btn btn-sm btn-secondary" onclick="app.viewProfileUsage(${profile.profileId})">
                        <i class="fas fa-chart-bar"></i> Historique
                    </button>
                    <button class="btn btn-sm btn-danger" onclick="app.deleteProfile(${profile.profileId})">
                        <i class="fas fa-trash"></i>
                    </button>
                </div>
            </div>
        `;
    }

    async showCreateProfileModal() {
        document.getElementById('parental-profile-modal').classList.add('active');
        document.getElementById('parental-modal-title').innerHTML = '<i class="fas fa-child"></i> Nouveau Profil';
        document.getElementById('profile-id').value = '';
        
        // Réinitialiser les champs
        document.getElementById('profile-name').value = '';
        document.getElementById('profile-color').value = '#00d9ff';
        document.getElementById('profile-time-limit').value = '0';
        document.getElementById('profile-avatar').value = '🧒';
        document.getElementById('profile-blocked-domains').value = '';

        // Charger les appareils disponibles
        await this.loadAvailableDevices();
        
        // Générer le planning horaire
        this.generateScheduleGrid();
        
        // Générer les catégories de filtrage
        this.generateFilterCategories();
    }

    async editProfile(profileId) {
        try {
            const profile = await this.api(`parentalcontrol/profiles/${profileId}/details`);
            
            document.getElementById('parental-profile-modal').classList.add('active');
            document.getElementById('parental-modal-title').innerHTML = `<i class="fas fa-child"></i> Modifier: ${this.escapeHtml(profile.name)}`;
            document.getElementById('profile-id').value = profile.id;
            
            document.getElementById('profile-name').value = profile.name || '';
            document.getElementById('profile-color').value = profile.color || '#00d9ff';
            document.getElementById('profile-time-limit').value = profile.dailyTimeLimitMinutes || 0;
            document.getElementById('profile-avatar').value = profile.avatarUrl || '🧒';

            // Charger les appareils et cocher ceux du profil
            await this.loadAvailableDevices(profile.devices?.map(d => d.macAddress) || []);
            
            // Générer le planning avec les valeurs existantes
            this.generateScheduleGrid(profile.schedules);
            
            // Générer les filtres avec les valeurs existantes
            const blockedCategories = profile.webFilters?.filter(f => f.filterType === 0).map(f => f.value) || [];
            const blockedDomains = profile.webFilters?.filter(f => f.filterType === 1).map(f => f.value) || [];
            
            this.generateFilterCategories(blockedCategories);
            document.getElementById('profile-blocked-domains').value = blockedDomains.join('\n');

        } catch (error) {
            console.error('Erreur chargement profil:', error);
            this.showToast({ title: 'Erreur', message: 'Impossible de charger le profil', severity: 2 });
        }
    }

    async loadAvailableDevices(selectedMacs = []) {
        const container = document.getElementById('profile-devices-list');
        if (!container) return;

        try {
            // Charger tous les appareils
            const devices = await this.api('devices');
            
            if (!devices.length) {
                container.innerHTML = '<p class="text-muted">Aucun appareil disponible. Scannez d\'abord le réseau.</p>';
                return;
            }

            container.innerHTML = devices.map(device => `
                <label class="device-checkbox">
                    <input type="checkbox" value="${this.escapeHtml(device.macAddress)}" 
                           ${selectedMacs.includes(device.macAddress.toUpperCase()) ? 'checked' : ''}>
                    <div style="flex: 1;">
                        <strong>${this.escapeHtml(device.hostname || device.description || device.macAddress)}</strong>
                        <br>
                        <small style="color: var(--text-secondary);">
                            ${this.escapeHtml(device.ipAddress || '')} - ${this.escapeHtml(device.vendor || 'Inconnu')}
                        </small>
                    </div>
                    <span class="status-badge ${this.getStatusClass(device.status)}">${this.getStatusText(device.status)}</span>
                </label>
            `).join('');
        } catch (error) {
            container.innerHTML = '<p class="text-muted text-danger">Erreur lors du chargement des appareils</p>';
        }
    }

    generateScheduleGrid(existingSchedules = []) {
        const container = document.getElementById('schedule-grid');
        if (!container) return;

        const days = ['Dim', 'Lun', 'Mar', 'Mer', 'Jeu', 'Ven', 'Sam'];
        
        container.innerHTML = days.map((day, index) => {
            const schedule = existingSchedules.find(s => s.dayOfWeek === index) || {
                startTime: '08:00',
                endTime: '21:00',
                isEnabled: true
            };
            
            return `
                <div class="schedule-row">
                    <span class="day-label">${day}</span>
                    <div class="schedule-times">
                        <input type="time" class="schedule-start" data-day="${index}" value="${schedule.startTime}" ${!schedule.isEnabled ? 'disabled' : ''}>
                        <span>à</span>
                        <input type="time" class="schedule-end" data-day="${index}" value="${schedule.endTime}" ${!schedule.isEnabled ? 'disabled' : ''}>
                    </div>
                    <label class="schedule-enabled">
                        <input type="checkbox" class="schedule-enabled-check" data-day="${index}" ${schedule.isEnabled ? 'checked' : ''} 
                               onchange="app.toggleScheduleDay(this)">
                        <i class="fas fa-check"></i>
                    </label>
                </div>
            `;
        }).join('');
    }

    toggleScheduleDay(checkbox) {
        const day = checkbox.dataset.day;
        const row = checkbox.closest('.schedule-row');
        const inputs = row.querySelectorAll('input[type="time"]');
        inputs.forEach(input => input.disabled = !checkbox.checked);
    }

    generateFilterCategories(selectedCategories = []) {
        const container = document.getElementById('filter-categories');
        if (!container) return;

        const categories = this.filterCategories.length ? this.filterCategories : [
            { key: 'adult', name: 'Contenu Adulte', description: 'Sites pour adultes', icon: 'fa-ban', color: '#ff4757' },
            { key: 'social-media', name: 'Réseaux Sociaux', description: 'Facebook, TikTok, etc.', icon: 'fa-users', color: '#3b5998' },
            { key: 'gaming', name: 'Jeux Vidéo', description: 'Sites de gaming', icon: 'fa-gamepad', color: '#9b59b6' },
            { key: 'streaming', name: 'Streaming', description: 'YouTube, Netflix, etc.', icon: 'fa-film', color: '#e74c3c' },
            { key: 'gambling', name: 'Jeux d\'Argent', description: 'Paris et casinos', icon: 'fa-dice', color: '#f39c12' },
            { key: 'malware', name: 'Malware', description: 'Sites malveillants', icon: 'fa-virus', color: '#c0392b' }
        ];

        container.innerHTML = categories.map(cat => `
            <label class="filter-category" style="--cat-color: ${cat.color};">
                <input type="checkbox" value="${cat.key}" ${selectedCategories.includes(cat.key) ? 'checked' : ''}>
                <div class="category-icon" style="${selectedCategories.includes(cat.key) ? `background: ${cat.color}; color: white;` : ''}">
                    <i class="fas ${cat.icon}"></i>
                </div>
                <div class="category-info">
                    <span class="category-name">${this.escapeHtml(cat.name)}</span>
                    <span class="category-desc">${this.escapeHtml(cat.description)}</span>
                </div>
            </label>
        `).join('');

        // Ajouter les événements de changement
        container.querySelectorAll('input[type="checkbox"]').forEach(cb => {
            cb.addEventListener('change', (e) => {
                const icon = e.target.closest('.filter-category').querySelector('.category-icon');
                const color = getComputedStyle(e.target.closest('.filter-category')).getPropertyValue('--cat-color');
                if (e.target.checked) {
                    icon.style.background = color;
                    icon.style.color = 'white';
                } else {
                    icon.style.background = '';
                    icon.style.color = '';
                }
            });
        });
    }

    closeParentalModal() {
        document.getElementById('parental-profile-modal').classList.remove('active');
    }

    async saveProfile() {
        const profileId = document.getElementById('profile-id').value;
        const name = document.getElementById('profile-name').value.trim();
        
        if (!name) {
            this.showToast({ title: 'Erreur', message: 'Le nom est requis', severity: 2 });
            return;
        }

        // Collecter les données
        const dto = {
            name: name,
            avatarUrl: document.getElementById('profile-avatar').value || '🧒',
            color: document.getElementById('profile-color').value || '#00d9ff',
            dailyTimeLimitMinutes: parseInt(document.getElementById('profile-time-limit').value) || 0,
            isActive: true,
            blockedMessage: "L'accès Internet est temporairement désactivé.",
            deviceMacs: [],
            schedules: [],
            blockedCategories: [],
            blockedDomains: []
        };

        // Appareils sélectionnés
        document.querySelectorAll('#profile-devices-list input[type="checkbox"]:checked').forEach(cb => {
            dto.deviceMacs.push(cb.value);
        });

        // Planning horaire
        for (let day = 0; day < 7; day++) {
            const enabledCb = document.querySelector(`.schedule-enabled-check[data-day="${day}"]`);
            const startInput = document.querySelector(`.schedule-start[data-day="${day}"]`);
            const endInput = document.querySelector(`.schedule-end[data-day="${day}"]`);
            
            if (enabledCb && startInput && endInput) {
                dto.schedules.push({
                    dayOfWeek: day,
                    startTime: startInput.value,
                    endTime: endInput.value,
                    isEnabled: enabledCb.checked
                });
            }
        }

        // Catégories de filtrage
        document.querySelectorAll('#filter-categories input[type="checkbox"]:checked').forEach(cb => {
            dto.blockedCategories.push(cb.value);
        });

        // Domaines personnalisés
        const domainsText = document.getElementById('profile-blocked-domains').value;
        if (domainsText) {
            dto.blockedDomains = domainsText.split('\n').map(d => d.trim()).filter(d => d);
        }

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
            this.loadParental();
        } catch (error) {
            console.error('Erreur sauvegarde profil:', error);
            this.showToast({ title: 'Erreur', message: error.message || 'Impossible de sauvegarder', severity: 2 });
        }
    }

    async pauseProfile(profileId) {
        try {
            await this.api(`parentalcontrol/profiles/${profileId}/pause`, { method: 'POST' });
            this.loadParental();
            this.showToast({ title: 'Pause activée', message: 'Internet coupé pour ce profil', severity: 1 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible d\'activer la pause', severity: 2 });
        }
    }

    async unpauseProfile(profileId) {
        try {
            await this.api(`parentalcontrol/profiles/${profileId}/unpause`, { method: 'POST' });
            this.loadParental();
            this.showToast({ title: 'Pause désactivée', message: 'Accès Internet rétabli', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de désactiver la pause', severity: 2 });
        }
    }

    async deleteProfile(profileId) {
        if (!confirm('Supprimer ce profil ? Les appareils associés seront débloqués.')) return;

        try {
            await this.api(`parentalcontrol/profiles/${profileId}`, { method: 'DELETE' });
            this.loadParental();
            this.showToast({ title: 'Supprimé', message: 'Profil supprimé', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de supprimer', severity: 2 });
        }
    }

    async viewProfileUsage(profileId) {
        try {
            const history = await this.api(`parentalcontrol/profiles/${profileId}/usage/history?days=7`);
            const profile = this.parentalProfiles.find(p => p.profileId === profileId);
            
            // Créer un modal ou afficher les données
            const days = ['Dim', 'Lun', 'Mar', 'Mer', 'Jeu', 'Ven', 'Sam'];
            let html = `<h4 style="margin-bottom: 15px;">Historique d'utilisation - ${this.escapeHtml(profile?.profileName || 'Profil')}</h4>`;
            
            if (!history.length) {
                html += '<p>Aucune donnée d\'utilisation disponible.</p>';
            } else {
                html += '<div class="usage-chart">';
                history.reverse().forEach(day => {
                    const date = new Date(day.date);
                    const dayName = days[date.getDay()];
                    const maxMinutes = profile?.remainingMinutes > 0 ? (profile.remainingMinutes + day.minutesUsed) : 180;
                    const heightPercent = Math.min(100, (day.minutesUsed / maxMinutes) * 100);
                    const hours = Math.floor(day.minutesUsed / 60);
                    const mins = day.minutesUsed % 60;
                    
                    html += `
                        <div class="usage-bar-container">
                            <div class="usage-bar" style="height: ${heightPercent}%; background: var(--accent-primary);"></div>
                            <span class="usage-value">${hours}h${mins}</span>
                            <span class="usage-label">${dayName}</span>
                        </div>
                    `;
                });
                html += '</div>';
            }

            // Afficher dans le modal générique
            document.getElementById('modal-title').innerHTML = '<i class="fas fa-chart-bar"></i> Historique';
            document.getElementById('modal-body').innerHTML = html;
            document.getElementById('modal-footer').innerHTML = '<button class="btn btn-sm" onclick="document.getElementById(\'modal\').classList.remove(\'active\')">Fermer</button>';
            document.getElementById('modal').classList.add('active');

        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de charger l\'historique', severity: 2 });
        }
    }

    async connectParentalHub() {
        if (typeof signalR === 'undefined' || this.parentalHub) return;

        try {
            this.parentalHub = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/parental-control')
                .withAutomaticReconnect()
                .configureLogging(signalR.LogLevel.Warning)
                .build();

            this.parentalHub.on('ProfileStatusChanged', (status) => {
                console.log('Profil mis à jour:', status);
                const index = this.parentalProfiles.findIndex(p => p.profileId === status.profileId);
                if (index !== -1) {
                    this.parentalProfiles[index] = status;
                    if (this.currentPage === 'parental') {
                        this.renderParentalProfiles(this.parentalProfiles);
                    }
                }
            });

            this.parentalHub.on('ProfileCreated', (status) => {
                this.parentalProfiles.push(status);
                if (this.currentPage === 'parental') {
                    this.renderParentalProfiles(this.parentalProfiles);
                }
            });

            this.parentalHub.on('ProfileDeleted', (profileId) => {
                this.parentalProfiles = this.parentalProfiles.filter(p => p.profileId !== profileId);
                if (this.currentPage === 'parental') {
                    this.renderParentalProfiles(this.parentalProfiles);
                }
            });

            this.parentalHub.on('AutoBlockTriggered', (data) => {
                this.showToast({
                    title: `${data.profileName} bloqué`,
                    message: data.reason,
                    severity: 1
                });
            });

            this.parentalHub.on('TimeWarning', (data) => {
                this.showToast({
                    title: `${data.profileName}`,
                    message: `Plus que ${data.remainingMinutes} minutes d'écran`,
                    severity: 1
                });
            });

            await this.parentalHub.start();
            console.log('Connecté au ParentalControlHub');
        } catch (error) {
            console.error('Erreur connexion ParentalHub:', error);
        }
    }
}

// Initialize application
const app = new FirewallApp();
