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
        this.dhcpConfigLevel = 'easy'; // Niveau par défaut pour DHCP
    }

    // ==========================================
    // NAVIGATION SETUP
    // ==========================================

    setupNavigation() {
        // Gestion du clic sur les éléments de navigation
        document.querySelectorAll('.nav-item').forEach(item => {
            item.addEventListener('click', (e) => {
                const page = item.dataset.page;
                if (page) {
                    // Cas spécial pour la page setup - redirection vers une autre page HTML
                    if (page === 'setup') {
                        window.location.href = '/setup.html';
                        return;
                    }
                    this.navigateTo(page);
                }
            });
        });
    }

    setupEventListeners() {
        // Fermeture des modals
        document.querySelectorAll('.modal-close').forEach(btn => {
            btn.addEventListener('click', () => {
                btn.closest('.modal').classList.remove('active');
            });
        });

        // Clic en dehors du modal
        document.querySelectorAll('.modal').forEach(modal => {
            modal.addEventListener('click', (e) => {
                if (e.target === modal) {
                    modal.classList.remove('active');
                }
            });
        });

        // Filtres des appareils
        document.querySelectorAll('.btn-filter').forEach(btn => {
            btn.addEventListener('click', () => {
                document.querySelectorAll('.btn-filter').forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
                this.filterDevices(btn.dataset.filter);
            });
        });
    }

    setupSorting() {
        document.querySelectorAll('.sortable-table th.sortable').forEach(th => {
            th.addEventListener('click', () => {
                const table = th.closest('table');
                const column = th.dataset.sort;
                const tbody = table.querySelector('tbody');
                const rows = Array.from(tbody.querySelectorAll('tr'));

                // Toggle direction
                const currentDir = this.sortDirection[column] || 'asc';
                const newDir = currentDir === 'asc' ? 'desc' : 'asc';
                this.sortDirection[column] = newDir;

                // Update headers
                table.querySelectorAll('th.sortable').forEach(h => {
                    h.classList.remove('asc', 'desc');
                });
                th.classList.add(newDir);

                // Sort rows
                rows.sort((a, b) => {
                    const aVal = a.querySelector(`td:nth-child(${th.cellIndex + 1})`).textContent;
                    const bVal = b.querySelector(`td:nth-child(${th.cellIndex + 1})`).textContent;
                    
                    if (newDir === 'asc') {
                        return aVal.localeCompare(bVal, 'fr', { numeric: true });
                    }
                    return bVal.localeCompare(aVal, 'fr', { numeric: true });
                });

                rows.forEach(row => tbody.appendChild(row));
            });
        });
    }

    // ==========================================
    // API HELPER
    // ==========================================

    async api(endpoint, options = {}) {
        const url = `/api/${endpoint}`;
        const config = {
            headers: {
                'Content-Type': 'application/json',
                ...options.headers
            },
            ...options
        };

        const response = await fetch(url, config);
        
        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(errorText || `HTTP ${response.status}`);
        }

        const contentType = response.headers.get('content-type');
        if (contentType && contentType.includes('application/json')) {
            return response.json();
        }
        return response.text();
    }

    // ==========================================
    // SIGNALR / NOTIFICATIONS
    // ==========================================

    async connectNotifications() {
        try {
            const connection = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/alerts')
                .withAutomaticReconnect()
                .build();

            connection.on('NewAlert', (alert) => {
                this.showToast(alert);
                this.updateAlertBadge();
            });

            await connection.start();
            console.log('Connecté au hub des alertes');
        } catch (error) {
            console.error('Erreur connexion hub alertes:', error);
        }
    }

    async connectDeviceHub() {
        try {
            this.deviceHub = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/devices')
                .withAutomaticReconnect()
                .build();

            this.deviceHub.on('DeviceUpdated', (device) => {
                if (this.currentPage === 'devices') {
                    this.loadDevices();
                }
            });

            this.deviceHub.on('NewDeviceDetected', (device) => {
                this.showToast({
                    title: 'Nouvel appareil',
                    message: `${device.macAddress} détecté sur le réseau`,
                    severity: 1
                });
                if (this.currentPage === 'devices') {
                    this.loadDevices();
                }
            });

            await this.deviceHub.start();
            console.log('Connecté au hub des appareils');
        } catch (error) {
            console.error('Erreur connexion hub appareils:', error);
        }
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
                this.api('alerts?count=5').catch(() => []),
                this.api('settings/system').catch(() => ({}))
            ]);

            // Update traffic overview
            this.updateTrafficCharts(security);
            this.updateTopThreats(security.recentThreats || []);
            this.updateFirewallRules();
            this.updateSystemStatus(security);
            this.updateAdminStatus(security);

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

    updateAdminStatus(security) {
        const status = document.getElementById('service-status');
        if (status) {
            status.textContent = security.adminStatus === 'running' ? 'En cours' : 'Arrêté';
            status.className = `status-badge ${security.adminStatus === 'running' ? 'online' : 'offline'}`;
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

            // Initialiser les sections
            let hardwareHtml = '';
            let networkHtml = '';
            let systemHtml = '';

            // Tenter de parser le JSON des détails
            let details = null;
            if (agent.detailsJson) {
                try {
                    // Le detailsJson peut être échappé, donc on décode
                    let jsonStr = agent.detailsJson;
                    if (jsonStr.startsWith('"') && jsonStr.endsWith('"')) {
                        jsonStr = JSON.parse(jsonStr);
                    }
                    details = typeof jsonStr === 'string' ? JSON.parse(jsonStr) : jsonStr;
                } catch (e) {
                    console.warn('Impossible de parser detailsJson:', e, agent.detailsJson);
                }
            }

            if (details) {
                // ========== HARDWARE ==========
                if (details.hardware) {
                    const hw = details.hardware;
                    hardwareHtml = `
                        <div class="detail-row"><strong>Processeur:</strong> ${this.escapeHtml(hw.cpuModel || 'Inconnu')}</div>
                        <div class="detail-row"><strong>Coeurs:</strong> ${hw.cpuCores || '-'}</div>
                        ${hw.cpuFreqMhz ? `<div class="detail-row"><strong>Fréquence:</strong> ${hw.cpuFreqMhz} MHz</div>` : ''}
                        ${hw.cpuTemp && hw.cpuTemp !== 'N/A' ? `<div class="detail-row"><strong>Température:</strong> ${hw.cpuTemp}°C</div>` : ''}
                        <div class="detail-row"><strong>RAM:</strong> ${this.escapeHtml(hw.usedRam || '-')} / ${this.escapeHtml(hw.totalRam || '-')}</div>
                        ${hw.totalSwap && hw.totalSwap !== '0 GB' ? `<div class="detail-row"><strong>Swap:</strong> ${this.escapeHtml(hw.usedSwap || '0')} / ${this.escapeHtml(hw.totalSwap)}</div>` : ''}
                        <div class="detail-row"><strong>Disque /:</strong> ${this.escapeHtml(hw.diskUsed || '-')} / ${this.escapeHtml(hw.diskTotal || '-')}</div>
                    `;
                }

                // ========== SYSTEM ==========
                if (details.system) {
                    const sys = details.system;
                    systemHtml = `
                        <div class="detail-row"><strong>Kernel:</strong> ${this.escapeHtml(sys.kernel || '-')}</div>
                        <div class="detail-row"><strong>Architecture:</strong> ${this.escapeHtml(sys.arch || '-')}</div>
                        <div class="detail-row"><strong>Uptime:</strong> ${this.escapeHtml(sys.uptime || '-')}</div>
                        <div class="detail-row"><strong>Dernier boot:</strong> ${this.escapeHtml(sys.lastBoot || '-')}</div>
                        <div class="detail-row"><strong>Load Average:</strong> ${sys.loadAvg1 || 0} / ${sys.loadAvg5 || 0} / ${sys.loadAvg15 || 0}</div>
                        <div class="detail-row"><strong>Processus:</strong> ${sys.processCount || '-'}</div>
                    `;
                }

                // ========== NETWORK ==========
                if (details.network) {
                    const net = details.network;
                    networkHtml = `
                        <div class="detail-row"><strong>Interface:</strong> ${this.escapeHtml(net.primaryInterface || '-')}</div>
                        <div class="detail-row"><strong>IP:</strong> ${this.escapeHtml(net.primaryIp || agent.ipAddress || '-')}</div>
                        <div class="detail-row"><strong>MAC:</strong> ${this.escapeHtml(net.primaryMac || agent.macAddress || '-')}</div>
                        <div class="detail-row"><strong>Trafic RX:</strong> ${net.rxTotalGb || 0} GB</div>
                        <div class="detail-row"><strong>Trafic TX:</strong> ${net.txTotalGb || 0} GB</div>
                    `;
                    
                    // Interfaces supplémentaires
                    if (net.interfaces && Array.isArray(net.interfaces) && net.interfaces.length > 1) {
                        networkHtml += '<div class="detail-row" style="margin-top: 10px;"><strong>Autres interfaces:</strong></div>';
                        net.interfaces.forEach(iface => {
                            if (iface.name !== net.primaryInterface) {
                                const ips = iface.ips ? iface.ips.join(', ') : '-';
                                networkHtml += `<div class="detail-row" style="font-size: 0.85rem; margin-left: 10px;">• ${this.escapeHtml(iface.name)}: ${this.escapeHtml(ips)}</div>`;
                            }
                        });
                    }
                }

                // ========== SERVICES ==========
                if (details.services) {
                    const svc = details.services;
                    systemHtml += '<div class="detail-row" style="margin-top: 15px; border-top: 1px solid var(--border-color); padding-top: 10px;"><strong>Services:</strong></div>';
                    
                    const getStatusBadge = (status) => {
                        if (status === 'active') return '<span class="status-badge online">Actif</span>';
                        if (status === 'inactive') return '<span class="status-badge offline">Inactif</span>';
                        if (status === 'not installed') return '<span class="status-badge unknown">Non installé</span>';
                        return `<span class="status-badge unknown">${status}</span>`;
                    };
                    
                    systemHtml += `<div class="detail-row" style="margin-left: 10px;">SSH: ${getStatusBadge(svc.ssh)}</div>`;
                    systemHtml += `<div class="detail-row" style="margin-left: 10px;">Docker: ${getStatusBadge(svc.docker)}`;
                    if (svc.docker === 'active' && svc.dockerContainers > 0) {
                        systemHtml += ` (${svc.dockerContainers} conteneur${svc.dockerContainers > 1 ? 's' : ''})`;
                    }
                    systemHtml += '</div>';
                }

                // ========== DISKS ==========
                if (details.disks && Array.isArray(details.disks) && details.disks.length > 0) {
                    hardwareHtml += '<div class="detail-row" style="margin-top: 15px; border-top: 1px solid var(--border-color); padding-top: 10px;"><strong>Disques:</strong></div>';
                    details.disks.forEach(disk => {
                        const usageColor = disk.percent > 90 ? 'var(--danger)' : disk.percent > 70 ? 'var(--warning)' : 'var(--success)';
                        hardwareHtml += `
                            <div class="detail-row" style="margin-left: 10px;">
                                <span>${this.escapeHtml(disk.mount)}</span>
                                <div style="display: inline-block; width: 100px; height: 8px; background: var(--bg-primary); border-radius: 4px; margin: 0 10px; vertical-align: middle;">
                                    <div style="width: ${disk.percent}%; height: 100%; background: ${usageColor}; border-radius: 4px;"></div>
                                </div>
                                <span>${disk.used}G / ${disk.total}G (${disk.percent}%)</span>
                            </div>
                        `;
                    });
                }
            } else {
                // Fallback: afficher les métriques de base
                hardwareHtml = `
                    <div class="detail-row"><strong>CPU:</strong> ${agent.cpuUsage?.toFixed(1) || 0}%</div>
                    <div class="detail-row"><strong>Mémoire:</strong> ${agent.memoryUsage?.toFixed(1) || 0}%</div>
                    <div class="detail-row"><strong>Disque:</strong> ${agent.diskUsage?.toFixed(1) || 0}%</div>
                `;
                
                networkHtml = `
                    <div class="detail-row"><strong>IP:</strong> ${this.escapeHtml(agent.ipAddress || '-')}</div>
                    <div class="detail-row"><strong>MAC:</strong> ${this.escapeHtml(agent.macAddress || '-')}</div>
                `;
                
                systemHtml = `
                    <div class="detail-row"><strong>OS:</strong> ${this.escapeHtml(agent.os || '-')}</div>
                    <div class="detail-row"><strong>Version Agent:</strong> ${this.escapeHtml(agent.version || '1.0.0')}</div>
                    <div class="detail-row"><strong>Enregistré le:</strong> ${this.formatDate(agent.registeredAt)}</div>
                `;
            }

            // Mettre à jour le DOM
            document.getElementById('agent-hardware-info').innerHTML = hardwareHtml || '<p>Aucune information</p>';
            document.getElementById('agent-network-info').innerHTML = networkHtml || '<p>Aucune information</p>';
            document.getElementById('agent-system-info').innerHTML = systemHtml || '<p>Aucune information</p>';

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
            const result = await this.api(`agents/install-script?os=${os}`);
            const container = document.getElementById('install-script-container');
            const command = document.getElementById('install-command');
            
            command.textContent = result.command;
            container.style.display = 'block';
        } catch (error) {
            console.error('Erreur génération script:', error);
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
            // Utiliser l'endpoint known pour approuver (marquer comme connu et de confiance)
            await this.api(`devices/${id}/known`, { 
                method: 'POST',
                body: JSON.stringify({ known: true, description: null })
            });
            // Aussi marquer comme trusted
            await this.api(`devices/${id}/trust`, { 
                method: 'POST',
                body: JSON.stringify({ trusted: true })
            });
            this.loadDevices();
            this.showToast({ title: 'Succes', message: 'Appareil approuve', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible d\'approuver: ' + error.message, severity: 2 });
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
    // OTHER PAGE LOADERS
    // ==========================================

    async loadCameras() {
        // Les caméras ne sont pas implémentées côté backend pour le moment
        const grid = document.getElementById('cameras-grid');
        if (grid) {
            grid.innerHTML = '<div class="empty-state"><i class="fas fa-video empty-state-icon"></i><p>Fonctionnalité en développement</p></div>';
        }
    }

    async loadTraffic() {
        try {
            const stats = await this.api('traffic/stats').catch(() => ({}));
            
            document.getElementById('total-packets').textContent = stats.totalPackets || 0;
            document.getElementById('inbound-packets').textContent = stats.inboundPackets || 0;
            document.getElementById('outbound-packets').textContent = stats.outboundPackets || 0;
            document.getElementById('suspicious-packets').textContent = stats.suspiciousPackets || 0;
            
            // Afficher les protocoles
            const protocolsChart = document.getElementById('protocols-chart');
            if (protocolsChart && stats.protocols) {
                protocolsChart.innerHTML = Object.entries(stats.protocols).map(([proto, count]) => `
                    <div class="protocol-item">
                        <span class="protocol-name">${this.escapeHtml(proto)}</span>
                        <span class="protocol-count">${count}</span>
                    </div>
                `).join('');
            }
        } catch (error) {
            console.error('Error loading traffic:', error);
        }
    }

    async loadDhcp() {
        try {
            // Charger la config avec l'aide du niveau actuel
            const [configResponse, leases, status] = await Promise.all([
                this.api(`dhcp/config/${this.dhcpConfigLevel}`),
                this.api('dhcp/leases').catch(() => []),
                this.api('dhcp/status').catch(() => ({}))
            ]);

            this.dhcpHelp = configResponse.help || [];
            const config = configResponse.config || {};

            // Mettre à jour les boutons de niveau
            document.querySelectorAll('.dhcp-level-btn').forEach(btn => {
                btn.classList.toggle('active', btn.dataset.level === this.dhcpConfigLevel);
            });

            // Afficher le statut
            this.updateDhcpStatus(status);

            // Remplir le formulaire selon le niveau
            this.renderDhcpForm(config);
            
            // Afficher les baux
            this.renderDhcpLeases(leases);

        } catch (error) {
            console.error('Error loading DHCP:', error);
            this.showToast({ title: 'Erreur', message: 'Impossible de charger la configuration DHCP', severity: 2 });
        }
    }

    setDhcpLevel(level) {
        this.dhcpConfigLevel = level;
        this.loadDhcp();
    }

    updateDhcpStatus(status) {
        const statusEl = document.getElementById('dhcp-server-status');
        if (statusEl) {
            const isRunning = status.isRunning;
            statusEl.innerHTML = `
                <span class="status-badge ${isRunning ? 'online' : 'offline'}">
                    ${isRunning ? 'En cours' : 'Arrêté'}
                </span>
                ${status.serverIp ? `<small style="margin-left: 10px;">IP: ${status.serverIp}</small>` : ''}
                ${status.activeLeases !== undefined ? `<small style="margin-left: 10px;">Baux: ${status.activeLeases}/${status.totalIps || '?'}</small>` : ''}
            `;
        }
    }

    renderDhcpForm(config) {
        const form = document.getElementById('dhcp-config-form');
        if (!form) return;

        const getHelp = (name) => this.dhcpHelp.find(h => h.name === name);

        let html = '';

        // Switch pour activer/désactiver
        const enabledHelp = getHelp('enabled');
        html += this.createDhcpField('enabled', 'Activer le serveur DHCP', config.enabled, 'checkbox', enabledHelp);

        // === NIVEAU FACILE ===
        html += '<h4 class="dhcp-section-title"><i class="fas fa-play-circle"></i> Configuration de base</h4>';
        
        html += '<div class="form-row">';
        html += this.createDhcpField('rangeStart', 'IP de début', config.rangeStart, 'text', getHelp('rangeStart'));
        html += this.createDhcpField('rangeEnd', 'IP de fin', config.rangeEnd, 'text', getHelp('rangeEnd'));
        html += '</div>';

        html += this.createDhcpField('gateway', 'Passerelle (routeur)', config.gateway, 'text', getHelp('gateway'));

        html += '<div class="form-row">';
        html += this.createDhcpField('dns1', 'DNS Principal', config.dns1, 'text', getHelp('dns1'));
        html += this.createDhcpField('dns2', 'DNS Secondaire', config.dns2, 'text', getHelp('dns2'));
        html += '</div>';

        // === NIVEAU INTERMÉDIAIRE ===
        if (this.dhcpConfigLevel !== 'easy') {
            html += '<h4 class="dhcp-section-title"><i class="fas fa-sliders-h"></i> Options réseau</h4>';
            
            html += '<div class="form-row">';
            html += this.createDhcpField('subnetMask', 'Masque de sous-réseau', config.subnetMask, 'text', getHelp('subnetMask'));
            html += this.createDhcpField('domainName', 'Nom de domaine', config.domainName, 'text', getHelp('domainName'));
            html += '</div>';

            html += '<div class="form-row">';
            html += this.createDhcpField('leaseTimeMinutes', 'Durée du bail (min)', config.leaseTimeMinutes, 'number', getHelp('leaseTimeMinutes'));
            html += this.createDhcpField('networkInterface', 'Interface réseau', config.networkInterface, 'text', getHelp('networkInterface'));
            html += '</div>';

            html += '<div class="form-row">';
            html += this.createDhcpField('ntpServer1', 'Serveur NTP', config.ntpServer1, 'text', getHelp('ntpServer1'));
            html += this.createDhcpField('authoritativeMode', 'Mode autoritaire', config.authoritativeMode, 'checkbox', getHelp('authoritativeMode'));
            html += '</div>';
        }

        // === NIVEAU EXPERT ===
        if (this.dhcpConfigLevel === 'expert') {
            html += '<h4 class="dhcp-section-title"><i class="fas fa-cogs"></i> Options avancées</h4>';
            
            html += '<div class="form-row">';
            html += this.createDhcpField('renewalTimeMinutes', 'Temps T1 (min)', config.renewalTimeMinutes, 'number', getHelp('renewalTimeMinutes'));
            html += this.createDhcpField('rebindingTimeMinutes', 'Temps T2 (min)', config.rebindingTimeMinutes, 'number', getHelp('rebindingTimeMinutes'));
            html += '</div>';

            html += '<div class="form-row">';
            html += this.createDhcpField('minLeaseTimeMinutes', 'Bail minimum (min)', config.minLeaseTimeMinutes, 'number', getHelp('minLeaseTimeMinutes'));
            html += this.createDhcpField('maxLeaseTimeMinutes', 'Bail maximum (min)', config.maxLeaseTimeMinutes, 'number', getHelp('maxLeaseTimeMinutes'));
            html += '</div>';

            html += '<h4 class="dhcp-section-title"><i class="fas fa-server"></i> Boot réseau (PXE)</h4>';
            
            html += '<div class="form-row">';
            html += this.createDhcpField('nextServerIp', 'Serveur PXE', config.nextServerIp, 'text', getHelp('nextServerIp'));
            html += this.createDhcpField('bootFileName', 'Fichier de boot', config.bootFileName, 'text', getHelp('bootFileName'));
            html += '</div>';
            
            html += this.createDhcpField('tftpServerName', 'Serveur TFTP', config.tftpServerName, 'text', getHelp('tftpServerName'));

            html += '<h4 class="dhcp-section-title"><i class="fas fa-shield-alt"></i> Sécurité</h4>';
            
            html += '<div class="form-row">';
            html += this.createDhcpField('allowUnknownClients', 'Autoriser clients inconnus', config.allowUnknownClients, 'checkbox', getHelp('allowUnknownClients'));
            html += this.createDhcpField('conflictDetection', 'Détection de conflit', config.conflictDetection, 'checkbox', getHelp('conflictDetection'));
            html += '</div>';

            html += '<div class="form-row">';
            html += this.createDhcpField('conflictDetectionAttempts', 'Tentatives de détection', config.conflictDetectionAttempts, 'number', getHelp('conflictDetectionAttempts'));
            html += this.createDhcpField('offerDelayMs', 'Délai offre (ms)', config.offerDelayMs, 'number', getHelp('offerDelayMs'));
            html += '</div>';

            html += this.createDhcpField('logAllPackets', 'Journaliser tous les paquets', config.logAllPackets, 'checkbox', getHelp('logAllPackets'));
        }

        form.innerHTML = html;
    }

    createDhcpField(name, label, value, type, help) {
        const helpHtml = help ? `
            <div class="dhcp-help" title="${this.escapeHtml(help.description)}">
                <i class="fas fa-question-circle"></i>
                <div class="dhcp-help-tooltip">
                    <strong>${this.escapeHtml(help.description)}</strong>
                    ${help.example ? `<br><em>Exemple: ${this.escapeHtml(help.example)}</em>` : ''}
                    ${help.warning ? `<br><span class="text-warning">${this.escapeHtml(help.warning)}</span>` : ''}
                </div>
            </div>
        ` : '';

        if (type === 'checkbox') {
            return `
                <div class="form-group dhcp-field">
                    <label class="checkbox-label">
                        <input type="checkbox" id="dhcp-${name}" name="${name}" ${value ? 'checked' : ''}>
                        ${this.escapeHtml(label)}
                        ${helpHtml}
                    </label>
                </div>
            `;
        }

        return `
            <div class="form-group dhcp-field">
                <label for="dhcp-${name}">
                    ${this.escapeHtml(label)}
                    ${helpHtml}
                </label>
                <input type="${type}" id="dhcp-${name}" name="${name}" value="${this.escapeHtml(value || '')}" class="form-control">
            </div>
        `;
    }

    renderDhcpLeases(leases) {
        const tbody = document.getElementById('dhcp-leases-table');
        if (!tbody) return;

        if (!leases || !leases.length) {
            tbody.innerHTML = '<tr><td colspan="5" class="empty-state">Aucun bail actif</td></tr>';
            return;
        }

        tbody.innerHTML = leases.map(lease => `
            <tr>
                <td>${this.escapeHtml(lease.ipAddress)}</td>
                <td class="device-mac">${this.escapeHtml(lease.macAddress)}</td>
                <td>${this.escapeHtml(lease.hostname || '-')}</td>
                <td>${this.formatDate(lease.expiration)}</td>
                <td>
                    <button class="btn btn-sm btn-danger" onclick="app.releaseDhcpLease('${this.escapeHtml(lease.macAddress)}')" title="Libérer">
                        <i class="fas fa-times"></i>
                    </button>
                </td>
            </tr>
        `).join('');
    }

    async saveDhcpConfig() {
        const form = document.getElementById('dhcp-config-form');
        if (!form) return;

        // Collecter les données du formulaire
        const getValue = (name) => {
            const el = document.getElementById(`dhcp-${name}`);
            if (!el) return undefined;
            if (el.type === 'checkbox') return el.checked;
            if (el.type === 'number') return parseInt(el.value) || 0;
            return el.value;
        };

        let endpoint = 'dhcp/config/easy';
        let config = {
            enabled: getValue('enabled'),
            rangeStart: getValue('rangeStart',
            rangeEnd: getValue('rangeEnd'),
            gateway: getValue('gateway'),
            dns1: getValue('dns1'),
            dns2: getValue('dns2')
        };

        if (this.dhcpConfigLevel === 'intermediate' || this.dhcpConfigLevel === 'expert') {
            endpoint = 'dhcp/config/intermediate';
            config = {
                ...config,
                subnetMask: getValue('subnetMask'),
                leaseTimeMinutes: getValue('leaseTimeMinutes'),
                domainName: getValue('domainName'),
                networkInterface: getValue('networkInterface'),
                ntpServer1: getValue('ntpServer1'),
                authoritativeMode: getValue('authoritativeMode'),
                staticReservations: [] // À implémenter séparément
            };
        }

        if (this.dhcpConfigLevel === 'expert') {
            endpoint = 'dhcp/config/expert';
            config = {
                ...config,
                renewalTimeMinutes: getValue('renewalTimeMinutes'),
                rebindingTimeMinutes: getValue('rebindingTimeMinutes'),
                minLeaseTimeMinutes: getValue('minLeaseTimeMinutes'),
                maxLeaseTimeMinutes: getValue('maxLeaseTimeMinutes'),
                nextServerIp: getValue('nextServerIp'),
                bootFileName: getValue('bootFileName'),
                tftpServerName: getValue('tftpServerName'),
                allowUnknownClients: getValue('allowUnknownClients'),
                conflictDetection: getValue('conflictDetection'),
                conflictDetectionAttempts: getValue('conflictDetectionAttempts'),
                offerDelayMs: getValue('offerDelayMs'),
                logAllPackets: getValue('logAllPackets')
            };
        }

        try {
            await this.api(endpoint, {
                method: 'POST',
                body: JSON.stringify(config)
            });
            this.showToast({ title: 'Succès', message: 'Configuration DHCP enregistrée', severity: 0 });
            this.loadDhcp();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message || 'Impossible de sauvegarder', severity: 2 });
        }
    }

    async releaseDhcpLease(macAddress) {
        if (!confirm(`Libérer le bail pour ${macAddress} ?`)) return;

        try {
            await this.api(`dhcp/leases/${encodeURIComponent(macAddress)}`, { method: 'DELETE' });
            this.showToast({ title: 'Succès', message: 'Bail libéré', severity: 0 });
            this.loadDhcp();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    // ==========================================
    // PI-HOLE ADDITIONAL METHODS
    // ==========================================

    async enablePihole() {
        try {
            await this.api('pihole/enable', { method: 'POST' });
            this.showToast({ title: 'Pi-hole', message: 'Blocage activé', severity: 0 });
            this.loadPihole();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    showDisablePiholeModal() {
        document.getElementById('modal-title').innerHTML = '<i class="fas fa-pause"></i> Désactiver Pi-hole';
        document.getElementById('modal-body').innerHTML = `
            <p>Choisissez la durée de désactivation :</p>
            <div class="form-group">
                <select id="pihole-disable-duration" class="form-control">
                    <option value="60">1 minute</option>
                    <option value="300">5 minutes</option>
                    <option value="600">10 minutes</option>
                    <option value="1800">30 minutes</option>
                    <option value="3600">1 heure</option>
                    <option value="0">Indéfiniment</option>
                </select>
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-warning" onclick="app.disablePihole()">
                <i class="fas fa-pause"></i> Désactiver
            </button>
        `;
        document.getElementById('modal').classList.add('active');
    }

    async disablePihole() {
        const duration = document.getElementById('pihole-disable-duration').value;
        
        try {
            await this.api(`pihole/disable?seconds=${duration}`, { method: 'POST' });
            document.getElementById('modal').classList.remove('active');
            this.showToast({ title: 'Pi-hole', message: 'Blocage désactivé', severity: 1 });
            this.loadPihole();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    showPiholePasswordModal() {
        document.getElementById('modal-title').innerHTML = '<i class="fas fa-key"></i> Changer le mot de passe Pi-hole';
        document.getElementById('modal-body').innerHTML = `
            <div class="form-group">
                <label>Nouveau mot de passe</label>
                <input type="password" id="pihole-new-password" class="form-control" placeholder="Nouveau mot de passe">
            </div>
            <div class="form-group">
                <label>Confirmer</label>
                <input type="password" id="pihole-confirm-password" class="form-control" placeholder="Confirmer le mot de passe">
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-primary" onclick="app.changePiholePassword()">
                <i class="fas fa-save"></i> Changer
            </button>
        `;
        document.getElementById('modal').classList.add('active');
    }

    async changePiholePassword() {
        const password = document.getElementById('pihole-new-password').value;
        const confirm = document.getElementById('pihole-confirm-password').value;
        
        if (password !== confirm) {
            this.showToast({ title: 'Erreur', message: 'Les mots de passe ne correspondent pas', severity: 2 });
            return;
        }
        
        try {
            await this.api('pihole/password', {
                method: 'POST',
                body: JSON.stringify({ password })
            });
            document.getElementById('modal').classList.remove('active');
            this.showToast({ title: 'Succès', message: 'Mot de passe changé', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async uninstallPihole() {
        if (!confirm('Désinstaller Pi-hole ? Cette action est irréversible.')) return;
        
        try {
            await this.api('pihole/uninstall', { method: 'POST' });
            this.showToast({ title: 'Succès', message: 'Pi-hole désinstallé', severity: 0 });
            this.loadPihole();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    // ==========================================
    // DEVICES
    // ==========================================

    async loadDevices() {
        try {
            const devices = await this.api('devices');
            this.currentDevices = devices;
            this.renderDevicesTable(devices);
        } catch (error) {
            console.error('Error loading devices:', error);
        }
    }

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
            
            return `
                <tr data-id="${device.id}" data-status="${statusClass}">
                    <td><span class="status-badge ${statusClass}">${statusText}</span></td>
                    <td class="device-mac">${this.escapeHtml(device.macAddress)}</td>
                    <td>${this.escapeHtml(device.ipAddress || '-')}</td>
                    <td>${this.escapeHtml(device.vendor || '-')}</td>
                    <td>${this.escapeHtml(device.description || device.hostname || '-')}</td>
                    <td>${this.formatDate(device.lastSeen)}</td>
                    <td>
                        <div class="action-buttons">
                            <button class="btn btn-sm btn-secondary" onclick="app.viewDevice(${device.id})" title="Détails">
                                <i class="fas fa-eye"></i>
                            </button>
                            <button class="btn btn-sm btn-success" onclick="app.approveDevice(${device.id})" title="Approuver">
                                <i class="fas fa-check"></i>
                            </button>
                            <button class="btn btn-sm btn-danger" onclick="app.deleteDevice(${device.id})" title="Supprimer">
                                <i class="fas fa-trash"></i>
                            </button>
                        </div>
                    </td>
                </tr>
            `;
        }).join('');
    }

    filterDevices(filter) {
        const rows = document.querySelectorAll('#devices-table tr');
        rows.forEach(row => {
            if (filter === 'all') {
                row.style.display = '';
            } else {
                row.style.display = row.dataset.status === filter ? '' : 'none';
            }
        });
    }

    async scanNetwork() {
        const btn = document.getElementById('scan-network-btn');
        const icon = document.getElementById('scan-icon');
        const status = document.getElementById('scan-status');
        const logsPanel = document.getElementById('scan-logs-panel');
        
        if (btn) btn.disabled = true;
        if (icon) icon.classList.add('fa-spin');
        if (status) status.textContent = 'Scan en cours...';
        if (logsPanel) logsPanel.style.display = 'block';
        
        try {
            const result = await this.api('devices/scan', { method: 'POST' });
            this.showToast({ title: 'Succès', message: `Scan terminé. ${result.newDevices || 0} nouveaux appareils.`, severity: 0 });
            this.loadDevices();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        } finally {
            if (btn) btn.disabled = false;
            if (icon) icon.classList.remove('fa-spin');
            if (status) status.textContent = '';
        }
    }

    showAddDeviceModal() {
        document.getElementById('modal-title').textContent = 'Ajouter un appareil';
        document.getElementById('modal-body').innerHTML = `
            <div class="form-group">
                <label>Adresse MAC</label>
                <input type="text" id="new-device-mac" class="form-control" placeholder="AA:BB:CC:DD:EE:FF">
            </div>
            <div class="form-group">
                <label>Description</label>
                <input type="text" id="new-device-desc" class="form-control" placeholder="Mon appareil">
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-primary" onclick="app.addDevice()">Ajouter</button>
        `;
        document.getElementById('modal').classList.add('active');
    }

    async addDevice() {
        const mac = document.getElementById('new-device-mac').value;
        const desc = document.getElementById('new-device-desc').value;

        try {
            await this.api('devices', {
                method: 'POST',
                body: JSON.stringify({ macAddress: mac, description: desc })
            });
            document.getElementById('modal').classList.remove('active');
            this.loadDevices();
            this.showToast({ title: 'Succès', message: 'Appareil ajouté', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async cleanupDevices() {
        if (!confirm('Supprimer les appareils Docker et fantômes ?')) return;
        
        try {
            await this.api('devices/cleanup', { method: 'POST' });
            this.loadDevices();
            this.showToast({ title: 'Succès', message: 'Nettoyage effectué', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async purgeDevices() {
        if (!confirm('⚠️ Supprimer TOUS les appareils ? Cette action est irréversible.')) return;
        
        try {
            await this.api('devices/purge', { method: 'DELETE' });
            this.loadDevices();
            this.showToast({ title: 'Succès', message: 'Tous les appareils supprimés', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    clearScanLogs() {
        const logs = document.getElementById('device-scan-logs');
        if (logs) logs.textContent = '';
    }

    // ==========================================
    // PI-HOLE
    // ==========================================

    async loadPihole() {
        try {
            const status = await this.api('pihole/status');
            
            // Vérifier si c'est Linux
            const notLinux = document.getElementById('pihole-not-linux');
            const notInstalled = document.getElementById('pihole-not-installed');
            const installed = document.getElementById('pihole-installed');
            
            if (!status.isLinux) {
                if (notLinux) notLinux.style.display = 'block';
                if (notInstalled) notInstalled.style.display = 'none';
                if (installed) installed.style.display = 'none';
                return;
            }

            if (notLinux) notLinux.style.display = 'none';

            if (!status.isInstalled) {
                if (notInstalled) notInstalled.style.display = 'block';
                if (installed) installed.style.display = 'none';
                return;
            }

            if (notInstalled) notInstalled.style.display = 'none';
            if (installed) installed.style.display = 'block';

            // Mettre à jour le statut
            const statusCard = document.getElementById('pihole-status-card');
            const statusText = document.getElementById('pihole-status-text');
            const blockingText = document.getElementById('pihole-blocking-text');
            const versionText = document.getElementById('pihole-version');

            if (statusText) statusText.textContent = status.isRunning ? 'Actif' : 'Inactif';
            if (statusCard) statusCard.className = `stat-card ${status.isRunning ? 'success' : 'danger'}`;
            
            if (blockingText) blockingText.textContent = status.blockingEnabled ? 'Activé' : 'Désactivé';
            const blockingCard = document.getElementById('pihole-blocking-card');
            if (blockingCard) blockingCard.className = `stat-card ${status.blockingEnabled ? 'success' : 'warning'}`;

            if (versionText) versionText.textContent = status.version || '-';

            // Stats
            if (status.stats) {
                const el = (id) => document.getElementById(id);
                if (el('ph-queries')) el('ph-queries').textContent = status.stats.totalQueries || 0;
                if (el('ph-blocked')) el('ph-blocked').textContent = status.stats.blockedQueries || 0;
                if (el('ph-percent')) el('ph-percent').textContent = `${(status.stats.percentBlocked || 0).toFixed(1)}%`;
                if (el('ph-domains')) el('ph-domains').textContent = status.stats.domainsOnBlocklist || 0;
                if (el('ph-clients')) el('ph-clients').textContent = status.stats.uniqueClients || 0;
            }

            // Afficher/masquer les boutons
            const btnEnable = document.getElementById('btn-enable-pihole');
            const btnDisable = document.getElementById('btn-disable-pihole');
            if (btnEnable) btnEnable.style.display = status.blockingEnabled ? 'none' : 'inline-flex';
            if (btnDisable) btnDisable.style.display = status.blockingEnabled ? 'inline-flex' : 'none';

        } catch (error) {
            console.error('Error loading Pi-hole:', error);
        }
    }

    async installPihole() {
        const modal = document.getElementById('pihole-install-modal');
        if (modal) modal.classList.add('active');
        const logs = document.getElementById('pihole-install-logs');
        if (logs) logs.textContent = 'Démarrage de l\'installation...\n';

        try {
            await this.api('pihole/install', { method: 'POST' });
            if (logs) logs.textContent += 'Installation lancée. Veuillez patienter...\n';
            
            // Polling des logs
            this.piholeLogPolling = setInterval(async () => {
                try {
                    const logData = await this.api('pihole/install/logs');
                    if (logs) {
                        logs.textContent = logData.logs || '';
                        logs.scrollTop = logs.scrollHeight;
                    }
                    
                    if (logData.completed) {
                        clearInterval(this.piholeLogPolling);
                        this.showToast({ title: 'Pi-hole', message: 'Installation terminée', severity: 0 });
                        this.loadPihole();
                    }
                } catch (e) {}
            }, 2000);

        } catch (error) {
            if (logs) logs.textContent += `Erreur: ${error.message}\n`;
        }
    }

    closePiholeInstallModal() {
        if (this.piholeLogPolling) {
            clearInterval(this.piholeLogPolling);
        }
        const modal = document.getElementById('pihole-install-modal');
        if (modal) modal.classList.remove('active');
        this.loadPihole();
    }

    // ==========================================
    // PARENTAL CONTROL
    // ==========================================

    async loadParental() {
        try {
            const profiles = await this.api('parentalcontrol/profiles').catch(() => []);
            const grid = document.getElementById('parental-profiles-grid');
            const empty = document.getElementById('parental-empty');

            if (!profiles || profiles.length === 0) {
                if (grid) grid.innerHTML = '';
                if (empty) empty.style.display = 'block';
                return;
            }

            if (empty) empty.style.display = 'none';
            if (grid) grid.innerHTML = profiles.map(p => this.createProfileCard(p)).join('');
        } catch (error) {
            console.error('Error loading parental profiles:', error);
        }
    }

    createProfileCard(profile) {
        const statusClass = profile.isActive ? 'success' : 'danger';
        const statusText = profile.isActive ? 'Connecté' : 'Hors ligne';
        
        return `
            <div class="profile-card" style="--profile-color: ${profile.color || '#00d9ff'}">
                <div class="profile-status-indicator" style="background: ${profile.isBlocked ? 'var(--danger)' : 'var(--success)'}"></div>
                <div class="profile-header">
                    <div class="profile-avatar">
                        ${profile.avatar && profile.avatar.startsWith('http') 
                            ? `<img src="${profile.avatar}" class="profile-avatar-img">`
                            : `<span class="profile-avatar-emoji">${profile.avatar || '👤'}</span>`}
                    </div>
                    <div class="profile-info">
                        <div class="profile-name">${this.escapeHtml(profile.name)}</div>
                        <div class="profile-status" style="color: var(--${statusClass})">${statusText}</div>
                    </div>
                </div>
                <div class="profile-actions">
                    <button class="btn btn-sm btn-secondary" onclick="app.editProfile(${profile.id})">
                        <i class="fas fa-edit"></i> Modifier
                    </button>
                    <button class="btn btn-sm btn-danger" onclick="app.deleteProfile(${profile.id})">
                        <i class="fas fa-trash"></i>
                    </button>
                </div>
            </div>
        `;
    }

    showCreateProfileModal() {
        document.getElementById('profile-id').value = '';
        document.getElementById('profile-name').value = '';
        document.getElementById('profile-color').value = '#00d9ff';
        document.getElementById('parental-modal-title').innerHTML = '<i class="fas fa-child"></i> Nouveau Profil';
        document.getElementById('parental-profile-modal').classList.add('active');
        this.loadDevicesForProfile();
    }

    closeParentalModal() {
        document.getElementById('parental-profile-modal').classList.remove('active');
    }

    async loadDevicesForProfile() {
        const list = document.getElementById('profile-devices-list');
        try {
            const devices = await this.api('devices');
            if (list) {
                list.innerHTML = devices.map(d => `
                    <label class="device-checkbox">
                        <input type="checkbox" value="${d.id}" name="profile-device">
                        <span>${this.escapeHtml(d.hostname || d.macAddress)}</span>
                    </label>
                `).join('');
            }
        } catch (e) {
            if (list) list.innerHTML = '<p class="text-muted">Impossible de charger les appareils</p>';
        }
    }

    async saveProfile() {
        const id = document.getElementById('profile-id')?.value;
        const data = {
            name: document.getElementById('profile-name')?.value,
            color: document.getElementById('profile-color')?.value,
            timeLimitMinutes: parseInt(document.getElementById('profile-time-limit')?.value) || 0,
            avatar: document.getElementById('profile-avatar')?.value,
            deviceIds: Array.from(document.querySelectorAll('input[name="profile-device"]:checked')).map(c => parseInt(c.value)),
            blockedDomains: (document.getElementById('profile-blocked-domains')?.value || '').split('\n').filter(d => d.trim())
        };

        try {
            if (id) {
                await this.api(`parentalcontrol/profiles/${id}`, { method: 'PUT', body: JSON.stringify(data) });
            } else {
                await this.api('parentalcontrol/profiles', { method: 'POST', body: JSON.stringify(data) });
            }
            this.closeParentalModal();
            this.loadParental();
            this.showToast({ title: 'Succès', message: 'Profil enregistré', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async deleteProfile(id) {
        if (!confirm('Supprimer ce profil ?')) return;
        try {
            await this.api(`parentalcontrol/profiles/${id}`, { method: 'DELETE' });
            this.loadParental();
            this.showToast({ title: 'Succès', message: 'Profil supprimé', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    // ==========================================
    // SNIFFER
    // ==========================================

    async loadSniffer() {
        const tbody = document.getElementById('sniffer-packets-table');
        if (tbody) {
            tbody.innerHTML = '<tr><td colspan="6" class="empty-state">Cliquez sur Démarrer pour capturer les paquets</td></tr>';
        }
    }

    async startSniffer() {
        const btnStart = document.getElementById('btn-start-sniffer');
        const btnStop = document.getElementById('btn-stop-sniffer');
        if (btnStart) btnStart.style.display = 'none';
        if (btnStop) btnStop.style.display = 'inline-flex';
        
        const tbody = document.getElementById('sniffer-packets-table');
        if (tbody) tbody.innerHTML = '';

        try {
            await this.api('sniffer/start', { method: 'POST' });
            
            this.snifferInterval = setInterval(async () => {
                try {
                    const packets = await this.api('sniffer/packets?count=50');
                    this.renderSnifferPackets(packets);
                } catch (e) {}
            }, 1000);
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async stopSniffer() {
        const btnStart = document.getElementById('btn-start-sniffer');
        const btnStop = document.getElementById('btn-stop-sniffer');
        if (btnStart) btnStart.style.display = 'inline-flex';
        if (btnStop) btnStop.style.display = 'none';

        if (this.snifferInterval) {
            clearInterval(this.snifferInterval);
        }

        try {
            await this.api('sniffer/stop', { method: 'POST' });
        } catch (error) {
            console.error('Error stopping sniffer:', error);
        }
    }

    renderSnifferPackets(packets) {
        const tbody = document.getElementById('sniffer-packets-table');
        if (!packets || !packets.length || !tbody) return;

        tbody.innerHTML = packets.map(p => `
            <tr>
                <td>${this.formatDate(p.timestamp)}</td>
                <td>${this.escapeHtml(p.sourceIp || '')}:${p.sourcePort || ''}</td>
                <td>${this.escapeHtml(p.destIp || '')}:${p.destPort || ''}</td>
                <td>${this.escapeHtml(p.protocol || '')}</td>
                <td>${p.size || 0} B</td>
                <td>${this.escapeHtml(p.direction || '')}</td>
            </tr>
        `).join('');
    }

    clearSnifferPackets() {
        const tbody = document.getElementById('sniffer-packets-table');
        if (tbody) {
            tbody.innerHTML = '<tr><td colspan="6" class="empty-state">Capture effacée</td></tr>';
        }
    }

    // ==========================================
    // ROUTER / SECURITY RULES
    // ==========================================

    async loadRouter() {
        await this.loadRouterInterfaces();
        await this.loadPortMappings();
    }

    async loadRouterInterfaces() {
        try {
            const interfaces = await this.api('router/interfaces').catch(() => []);
            const container = document.getElementById('router-interfaces-list');
            
            if (!container) return;
            
            if (!interfaces || !interfaces.length) {
                container.innerHTML = '<p class="empty-state">Aucune interface détectée</p>';
                return;
            }

            container.innerHTML = interfaces.map(iface => `
                <div class="interface-item" style="display: flex; justify-content: space-between; padding: 10px; background: var(--bg-secondary); margin-bottom: 8px; border-radius: 6px;">
                    <span>${this.escapeHtml(iface.name)}</span>
                    <span>${this.escapeHtml(iface.ipAddress || '-')}</span>
                    <span class="status-badge ${iface.isUp ? 'online' : 'offline'}">${iface.isUp ? 'Up' : 'Down'}</span>
                </div>
            `).join('');
        } catch (error) {
            console.error('Error loading interfaces:', error);
        }
    }

    async loadPortMappings() {
        try {
            const mappings = await this.api('router/mappings').catch(() => []);
            const tbody = document.getElementById('router-mappings-table');
            
            if (!tbody) return;
            
            if (!mappings || !mappings.length) {
                tbody.innerHTML = '<tr><td colspan="5" class="empty-state">Aucune règle de transfert</td></tr>';
                return;
            }

            tbody.innerHTML = mappings.map(m => `
                <tr>
                    <td>${this.escapeHtml(m.name)}</td>
                    <td>${m.localPort}</td>
                    <td>${this.escapeHtml(m.targetIp)}:${m.targetPort}</td>
                    <td>${this.escapeHtml(m.protocol)}</td>
                    <td>
                        <button class="btn btn-sm btn-danger" onclick="app.deleteMapping(${m.id})">
                            <i class="fas fa-trash"></i>
                        </button>
                    </td>
                </tr>
            `).join('');
        } catch (error) {
            console.error('Error loading mappings:', error);
        }
    }

    showAddMappingModal() {
        document.getElementById('modal-title').textContent = 'Ajouter une règle NAT';
        document.getElementById('modal-body').innerHTML = `
            <div class="form-group">
                <label>Nom</label>
                <input type="text" id="mapping-name" class="form-control" placeholder="SSH externe">
            </div>
            <div class="form-row">
                <div class="form-group">
                    <label>Port local</label>
                    <input type="number" id="mapping-local-port" class="form-control" placeholder="2222">
                </div>
                <div class="form-group">
                    <label>Protocole</label>
                    <select id="mapping-protocol" class="form-control">
                        <option value="TCP">TCP</option>
                        <option value="UDP">UDP</option>
                    </select>
                </div>
            </div>
            <div class="form-row">
                <div class="form-group">
                    <label>IP cible</label>
                    <input type="text" id="mapping-target-ip" class="form-control" placeholder="192.168.1.100">
                </div>
                <div class="form-group">
                    <label>Port cible</label>
                    <input type="number" id="mapping-target-port" class="form-control" placeholder="22">
                </div>
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-primary" onclick="app.addMapping()">Ajouter</button>
        `;
        document.getElementById('modal').classList.add('active');
    }

    async addMapping() {
        const data = {
            name: document.getElementById('mapping-name')?.value,
            localPort: parseInt(document.getElementById('mapping-local-port')?.value),
            targetIp: document.getElementById('mapping-target-ip')?.value,
            targetPort: parseInt(document.getElementById('mapping-target-port')?.value),
            protocol: document.getElementById('mapping-protocol')?.value
        };

        try {
            await this.api('router/mappings', { method: 'POST', body: JSON.stringify(data) });
            document.getElementById('modal').classList.remove('active');
            this.loadPortMappings();
            this.showToast({ title: 'Succès', message: 'Règle ajoutée', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async deleteMapping(id) {
        if (!confirm('Supprimer cette règle ?')) return;
        try {
            await this.api(`router/mappings/${id}`, { method: 'DELETE' });
            this.loadPortMappings();
            this.showToast({ title: 'Succès', message: 'Règle supprimée', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    // ==========================================
    // SETTINGS
    // ==========================================

    async loadSettings() {
        try {
            const [sysInfo, interfaces] = await Promise.all([
                this.api('settings/system').catch(() => ({})),
                this.api('settings/interfaces').catch(() => [])
            ]);

            // System info
            const sysContainer = document.getElementById('system-info');
            if (sysContainer) {
                sysContainer.innerHTML = `
                    <div class="detail-row"><strong>OS:</strong> ${this.escapeHtml(sysInfo.os || '-')}</div>
                    <div class="detail-row"><strong>Hostname:</strong> ${this.escapeHtml(sysInfo.hostname || '-')}</div>
                    <div class="detail-row"><strong>Uptime:</strong> ${this.escapeHtml(sysInfo.uptime || '-')}</div>
                `;
            }

            // Interfaces
            const ifaceContainer = document.getElementById('interfaces-list');
            if (ifaceContainer && interfaces && interfaces.length) {
                ifaceContainer.innerHTML = interfaces.map(iface => `
                    <div class="interface-item" style="display: flex; justify-content: space-between; padding: 10px; background: var(--bg-secondary); margin-bottom: 8px; border-radius: 6px;">
                        <span>${this.escapeHtml(iface.name)}</span>
                        <span>${this.escapeHtml(iface.ipAddress || '-')}</span>
                        <span class="status-badge ${iface.isUp ? 'online' : 'offline'}">${iface.isUp ? 'Actif' : 'Inactif'}</span>
                    </div>
                `).join('');
            }
        } catch (error) {
            console.error('Error loading settings:', error);
        }
    }

    // ==========================================
    // ADMIN
    // ==========================================

    async loadAdmin() {
        try {
            const status = await this.api('admin/status').catch(() => ({}));
            
            const statusText = document.getElementById('service-status-text');
            const statusCard = document.getElementById('service-status-card');
            const appVersion = document.getElementById('app-version');
            
            if (statusText) statusText.textContent = status.isRunning ? 'En cours' : 'Arrêté';
            if (statusCard) statusCard.className = `stat-card ${status.isRunning ? 'success' : 'danger'}`;
            if (appVersion) appVersion.textContent = status.version || '1.0.0';
        } catch (error) {
            console.error('Error loading admin:', error);
        }
    }

    async startService() {
        try {
            await this.api('admin/start', { method: 'POST' });
            this.showToast({ title: 'Succès', message: 'Service démarré', severity: 0 });
            this.loadAdmin();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async stopService() {
        try {
            await this.api('admin/stop', { method: 'POST' });
            this.showToast({ title: 'Succès', message: 'Service arrêté', severity: 0 });
            this.loadAdmin();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async restartService() {
        try {
            await this.api('admin/restart', { method: 'POST' });
            this.showToast({ title: 'Succès', message: 'Service redémarré', severity: 0 });
            this.loadAdmin();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async installService() {
        try {
            const result = await this.api('admin/install', { method: 'POST' });
            const resultEl = document.getElementById('install-result');
            if (resultEl) resultEl.innerHTML = `<pre>${this.escapeHtml(result.message || 'OK')}</pre>`;
            this.showToast({ title: 'Succès', message: 'Service installé', severity: 0 });
        } catch (error) {
            const resultEl = document.getElementById('install-result');
            if (resultEl) resultEl.innerHTML = `<pre class="error">${this.escapeHtml(error.message)}</pre>`;
        }
    }

    async uninstallService() {
        if (!confirm('Désinstaller le service ?')) return;
        try {
            await this.api('admin/uninstall', { method: 'POST' });
            this.showToast({ title: 'Succès', message: 'Service désinstallé', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async checkForUpdates() {
        const statusEl = document.getElementById('update-status');
        if (statusEl) statusEl.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Vérification...';
        
        try {
            const result = await this.api('admin/updates/check');
            if (statusEl) {
                if (result.updateAvailable) {
                    statusEl.innerHTML = `<span style="color: var(--success)"><i class="fas fa-arrow-up"></i> Mise à jour disponible: ${result.latestVersion}</span>`;
                } else {
                    statusEl.innerHTML = '<span style="color: var(--success)"><i class="fas fa-check"></i> À jour</span>';
                }
            }
        } catch (error) {
            if (statusEl) statusEl.innerHTML = `<span style="color: var(--danger)"><i class="fas fa-times"></i> Erreur</span>`;
        }
    }

    async updateFromGithub() {
        const resultEl = document.getElementById('update-result');
        if (resultEl) resultEl.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Mise à jour en cours...';
        
        try {
            const result = await this.api('admin/updates/apply', { method: 'POST' });
            if (resultEl) resultEl.innerHTML = `<pre>${this.escapeHtml(result.message || 'Mise à jour effectuée')}</pre>`;
            this.showToast({ title: 'Succès', message: 'Mise à jour effectuée. Redémarrez le service.', severity: 0 });
        } catch (error) {
            if (resultEl) resultEl.innerHTML = `<pre class="error">${this.escapeHtml(error.message)}</pre>`;
        }
    }

    async loadServiceLogs() {
        const container = document.getElementById('service-logs');
        if (!container) return;
        container.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Chargement...';
        
        try {
            const logs = await this.api('admin/logs').catch(() => []);
            if (!logs || !logs.length) {
                container.innerHTML = '<p class="empty-state">Aucun log disponible</p>';
                return;
            }
            container.innerHTML = logs.map(log => `
                <div class="log-entry ${(log.level || '').toLowerCase()}">
                    <span class="log-time">${this.formatDate(log.timestamp)}</span>
                    <span class="log-message">${this.escapeHtml(log.message)}</span>
                </div>
            `).join('');
        } catch (error) {
            container.innerHTML = `<p class="error">Erreur: ${this.escapeHtml(error.message)}</p>`;
        }
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
            sniffer: 'Analyse de paquets',
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
            case 'cameras': this.loadCameras(); break;
            case 'traffic': this.loadTraffic(); break;
            case 'dhcp': this.loadDhcp(); break;
            case 'sniffer': this.loadSniffer(); break;
            case 'router': this.loadRouter(); break;
            case 'settings': this.loadSettings(); break;
            case 'admin': this.loadAdmin(); break;
        }
    }
}

// Instanciation de l'application au chargement de la page
const app = new FirewallApp();
